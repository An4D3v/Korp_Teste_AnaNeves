using System.Net;
using System.Net.Http.Json;
using Korp.Faturamento.Api.Dominio;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Korp.Tests;

/// <summary>
/// O requisito obrigatorio numero 2 do enunciado: um dos microservicos falha,
/// o sistema se recupera e da um retorno apropriado ao usuario.
///
/// Cada teste aqui derruba o Estoque de proposito e conferee tres coisas:
///   1. a resposta explica o que houve, em vez de estourar erro cru;
///   2. NENHUM saldo foi mexido;
///   3. a nota nao ficou num estado que impeca tentar de novo.
/// </summary>
[Collection("korp")]
public class FalhaDoEstoqueTestes(AmbienteKorp korp) : IAsyncLifetime
{
    public Task InitializeAsync() => Task.CompletedTask;

    // Religa o Estoque E fecha o disjuntor, mesmo se o teste falhar no meio.
    // Sem isso, o proximo teste herdaria um disjuntor meio-aberto e falharia
    // por um motivo que nao e dele. Ver AmbienteKorp.RestaurarEstoqueAsync.
    public Task DisposeAsync() => korp.RestaurarEstoqueAsync();

    [Fact]
    public async Task Estoque_fora_do_ar_devolve_503_com_explicacao_e_nao_mexe_no_saldo()
    {
        var produto = await korp.CriarProdutoAsync(saldo: 10);
        var nota = await korp.CriarNotaAsync((produto, 3));

        await korp.CaosAsync(true);
        var resposta = await korp.ImprimirAsync(nota.Id, $"caos-{Guid.NewGuid()}");
        await korp.CaosAsync(false);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, resposta.StatusCode);
        Assert.Equal("ESTOQUE_INDISPONIVEL", await AmbienteKorp.CodigoDoErroAsync(resposta));

        // A mensagem precisa dizer ao usuario o que fazer, e nao so "erro".
        var texto = await resposta.Content.ReadAsStringAsync();
        Assert.Contains("Estoque", texto);

        // O que mais importa: o saldo continua intacto.
        Assert.Equal(10, await korp.SaldoAsync(produto.Id));

        var depois = await korp.Faturamento.GetFromJsonAsync<NotaDto>($"/notas/{nota.Id}", AmbienteKorp.Json);
        Assert.NotEqual("Fechada", depois!.Status);
    }

    [Fact]
    public async Task Depois_que_o_estoque_volta_a_mesma_nota_imprime_normalmente()
    {
        var produto = await korp.CriarProdutoAsync(saldo: 6);
        var nota = await korp.CriarNotaAsync((produto, 2));

        await korp.CaosAsync(true);
        var durante = await korp.ImprimirAsync(nota.Id, $"caos-{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, durante.StatusCode);
        await korp.CaosAsync(false);

        // O disjuntor abriu por causa das falhas; ele fecha sozinho. No ambiente de teste
        // essa espera e de 1 segundo (em producao, 10).
        var depois = await TentarAteImprimirAsync(nota.Id, tentativas: 12);

        Assert.Equal(HttpStatusCode.OK, depois.StatusCode);

        var resultado = await depois.Content.ReadFromJsonAsync<ImpressaoDto>(AmbienteKorp.Json);
        Assert.Equal("Fechada", resultado!.Nota.Status);
        Assert.Equal(4, await korp.SaldoAsync(produto.Id));
    }

    [Fact]
    public async Task Nota_presa_em_processando_se_resolve_no_proximo_clique()
    {
        var produto = await korp.CriarProdutoAsync(saldo: 5);
        var nota = await korp.CriarNotaAsync((produto, 1));

        await korp.CaosAsync(true);
        await korp.ImprimirAsync(nota.Id, $"caos-{Guid.NewGuid()}");
        await korp.CaosAsync(false);

        // Com o Estoque fora, o Faturamento nao consegue nem perguntar se a baixa
        // aconteceu, entao a nota fica em Processando de proposito.
        var presa = await korp.Faturamento.GetFromJsonAsync<NotaDto>($"/notas/{nota.Id}", AmbienteKorp.Json);
        Assert.Equal("Processando", presa!.Status);

        // O proximo clique resolve: o sistema pergunta ao Estoque o que houve com a
        // tentativa antiga antes de agir. Como nada foi baixado, ele libera e imprime.
        var resposta = await TentarAteImprimirAsync(nota.Id, tentativas: 12);
        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);

        var resultado = await resposta.Content.ReadFromJsonAsync<ImpressaoDto>(AmbienteKorp.Json);
        Assert.Equal("Fechada", resultado!.Nota.Status);
        Assert.Equal(4, await korp.SaldoAsync(produto.Id));
    }

    [Fact]
    public async Task Nota_nao_pode_ficar_fechada_com_saldo_intacto()
    {
        // A pior inconsistencia possivel neste sistema: a nota dizer que foi impressa
        // sem o estoque ter saido. Este teste existe para travar essa possibilidade.
        var produto = await korp.CriarProdutoAsync(saldo: 4);
        var nota = await korp.CriarNotaAsync((produto, 2));

        await korp.CaosAsync(true);
        await korp.ImprimirAsync(nota.Id, $"caos-{Guid.NewGuid()}");
        await korp.CaosAsync(false);

        var depois = await korp.Faturamento.GetFromJsonAsync<NotaDto>($"/notas/{nota.Id}", AmbienteKorp.Json);
        var saldo = await korp.SaldoAsync(produto.Id);

        var fechadaSemBaixar = depois!.Status == "Fechada" && saldo == 4;
        Assert.False(fechadaSemBaixar, "A nota ficou Fechada sem o saldo ter sido baixado.");
    }

    /// <summary>
    /// Insiste na impressao ate o disjuntor fechar. Isso e exatamente o que a pessoa
    /// faria na tela: clicar de novo depois de ver o aviso.
    /// </summary>
    private async Task<HttpResponseMessage> TentarAteImprimirAsync(int notaId, int tentativas)
    {
        HttpResponseMessage ultima = null!;

        for (var i = 0; i < tentativas; i++)
        {
            ultima = await korp.ImprimirAsync(notaId, $"retomada-{notaId}");
            if (ultima.StatusCode == HttpStatusCode.OK) return ultima;
            await Task.Delay(300);
        }

        return ultima;
    }
}

/// <summary>
/// O reconciliador em segundo plano: a rede de seguranca para quando NINGUEM clica de novo.
/// Sobe uma instancia propria do Faturamento com os tempos curtos, para nao deixar
/// um servico de fundo agressivo atrapalhando os outros testes.
/// </summary>
[Collection("korp")]
public class ReconciliadorTestes(AmbienteKorp korp) : IAsyncLifetime
{
    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync() => korp.RestaurarEstoqueAsync();

    [Fact]
    public async Task Nota_presa_e_regularizada_sozinha_em_segundo_plano()
    {
        var produto = await korp.CriarProdutoAsync(saldo: 5);
        var nota = await korp.CriarNotaAsync((produto, 1));

        // Deixa a nota presa em Processando, com o Estoque fora.
        await korp.CaosAsync(true);
        await korp.ImprimirAsync(nota.Id, $"caos-{Guid.NewGuid()}");
        await korp.CaosAsync(false);

        var presa = await korp.Faturamento.GetFromJsonAsync<NotaDto>($"/notas/{nota.Id}", AmbienteKorp.Json);
        Assert.Equal("Processando", presa!.Status);

        // Agora sobe um Faturamento com o reconciliador acordado, apontando para o
        // MESMO banco. Ninguem vai clicar em nada: quem tem que resolver e ele.
        await using var comReconciliador = new WebApplicationFactory<PontoDeEntradaFaturamento>()
            .WithWebHostBuilder(b =>
            {
                b.UseEnvironment("Development");
                b.UseSetting("ConnectionStrings:Faturamento", AmbienteKorp.ConexaoFaturamento);
                b.UseSetting("Reconciliacao:IntervaloSegundos", "1");
                b.UseSetting("Reconciliacao:IdadeMinimaSegundos", "1");
                b.ConfigureServices(s => AmbienteKorp.LigarNoEstoqueDeTeste(s, korp.AppEstoque));
            });

        // Forca o host a iniciar (e com ele o servico de fundo).
        using var cliente = comReconciliador.CreateClient();
        await cliente.GetAsync("/health");

        var status = await EsperarStatusAsync(cliente, nota.Id, alvo: "Aberta", segundos: 15);

        Assert.Equal("Aberta", status);

        // A baixa nunca aconteceu, entao o saldo tem que estar inteiro.
        Assert.Equal(5, await korp.SaldoAsync(produto.Id));
    }

    private static async Task<string> EsperarStatusAsync(
        HttpClient cliente, int notaId, string alvo, int segundos)
    {
        var limite = DateTime.UtcNow.AddSeconds(segundos);
        var ultimo = "";

        while (DateTime.UtcNow < limite)
        {
            var nota = await cliente.GetFromJsonAsync<NotaDto>($"/notas/{notaId}", AmbienteKorp.Json);
            ultimo = nota!.Status;
            if (ultimo == alvo) return ultimo;
            await Task.Delay(400);
        }

        return ultimo;
    }
}
