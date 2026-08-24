using System.Net;
using System.Net.Http.Json;

namespace Korp.Tests;

[Collection("korp")]
public class EstoqueTestes(AmbienteKorp korp)
{
    // ==================================================================
    // Cadastro
    // ==================================================================

    [Fact]
    public async Task Cadastra_produto_e_encontra_na_listagem()
    {
        var produto = await korp.CriarProdutoAsync(saldo: 7);

        var pagina = await korp.Estoque.GetFromJsonAsync<PaginaDto<ProdutoDto>>(
            $"/produtos?busca={produto.Codigo}", AmbienteKorp.Json);

        Assert.NotNull(pagina);
        var encontrado = Assert.Single(pagina.Itens);
        Assert.Equal(produto.Codigo, encontrado.Codigo);
        Assert.Equal(7, encontrado.Saldo);
    }

    [Fact]
    public async Task Codigo_repetido_e_recusado()
    {
        var produto = await korp.CriarProdutoAsync(saldo: 1);

        var resposta = await korp.Estoque.PostAsJsonAsync("/produtos", new
        {
            codigo = produto.Codigo,
            descricao = "tentativa de duplicar",
            saldo = 5,
        });

        Assert.Equal(HttpStatusCode.Conflict, resposta.StatusCode);
        Assert.Equal("CODIGO_JA_EXISTE", await AmbienteKorp.CodigoDoErroAsync(resposta));
    }

    [Fact]
    public async Task Produto_invalido_devolve_os_erros_de_validacao()
    {
        var resposta = await korp.Estoque.PostAsJsonAsync("/produtos", new
        {
            codigo = "",
            descricao = "",
            saldo = -3,
        });

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
        Assert.Equal("DADOS_INVALIDOS", await AmbienteKorp.CodigoDoErroAsync(resposta));
    }

    // ==================================================================
    // Baixa de saldo
    // ==================================================================

    [Fact]
    public async Task Baixa_reduz_o_saldo_como_no_exemplo_do_enunciado()
    {
        // O enunciado: saldo anterior 10, nota usa 2, novo saldo 8.
        var produto = await korp.CriarProdutoAsync(saldo: 10);

        var resposta = await korp.BaixarAsync($"chave-{Guid.NewGuid()}", new
        {
            notaFiscalId = 1,
            notaNumero = 1,
            itens = new[] { new { produtoId = produto.Id, quantidade = 2 } },
        });

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);

        var movimento = await resposta.Content.ReadFromJsonAsync<MovimentoDto>(AmbienteKorp.Json);
        Assert.Equal(8, movimento!.Itens.Single().SaldoResultante);
        Assert.False(movimento.Repetido);
        Assert.Equal(8, await korp.SaldoAsync(produto.Id));
    }

    [Fact]
    public async Task Saldo_insuficiente_nao_altera_nada()
    {
        var produto = await korp.CriarProdutoAsync(saldo: 3);

        var resposta = await korp.BaixarAsync($"chave-{Guid.NewGuid()}", new
        {
            notaFiscalId = 1,
            notaNumero = 1,
            itens = new[] { new { produtoId = produto.Id, quantidade = 4 } },
        });

        Assert.Equal(HttpStatusCode.Conflict, resposta.StatusCode);
        Assert.Equal("SALDO_INSUFICIENTE", await AmbienteKorp.CodigoDoErroAsync(resposta));
        Assert.Equal(3, await korp.SaldoAsync(produto.Id));
    }

    [Fact]
    public async Task Sem_chave_de_idempotencia_a_baixa_e_recusada()
    {
        var produto = await korp.CriarProdutoAsync(saldo: 5);

        var resposta = await korp.Estoque.PostAsJsonAsync("/estoque/baixas", new
        {
            notaFiscalId = 1,
            notaNumero = 1,
            itens = new[] { new { produtoId = produto.Id, quantidade = 1 } },
        });

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
        Assert.Equal("CHAVE_IDEMPOTENCIA_OBRIGATORIA", await AmbienteKorp.CodigoDoErroAsync(resposta));
        Assert.Equal(5, await korp.SaldoAsync(produto.Id));
    }

    // ==================================================================
    // IDEMPOTENCIA (opcional C do enunciado)
    // ==================================================================

    [Fact]
    public async Task Mesma_chave_duas_vezes_baixa_o_saldo_uma_vez_so()
    {
        var produto = await korp.CriarProdutoAsync(saldo: 10);
        var chave = $"duplo-clique-{Guid.NewGuid()}";
        var corpo = new
        {
            notaFiscalId = 1,
            notaNumero = 1,
            itens = new[] { new { produtoId = produto.Id, quantidade = 2 } },
        };

        var primeira = await korp.BaixarAsync(chave, corpo);
        var segunda = await korp.BaixarAsync(chave, corpo);

        Assert.Equal(HttpStatusCode.OK, primeira.StatusCode);
        Assert.Equal(HttpStatusCode.OK, segunda.StatusCode);

        var m1 = await primeira.Content.ReadFromJsonAsync<MovimentoDto>(AmbienteKorp.Json);
        var m2 = await segunda.Content.ReadFromJsonAsync<MovimentoDto>(AmbienteKorp.Json);

        Assert.False(m1!.Repetido);
        Assert.True(m2!.Repetido);

        // A segunda resposta e IGUAL a primeira, e nao um 200 vazio.
        Assert.Equal(m1.MovimentoId, m2.MovimentoId);
        Assert.Equal(m1.Itens.Single().SaldoResultante, m2.Itens.Single().SaldoResultante);

        // 10 - 2 = 8. Nunca 6.
        Assert.Equal(8, await korp.SaldoAsync(produto.Id));
    }

    [Fact]
    public async Task Mesma_chave_disparada_varias_vezes_ao_mesmo_tempo_baixa_uma_vez_so()
    {
        // Este e o caso dificil: nao e clicar duas vezes em sequencia, e sim varias
        // requisicoes identicas CHEGANDO JUNTAS. Quem segura e o indice unico do banco,
        // que faz as concorrentes esbarrarem uma na outra em vez de todas passarem.
        var produto = await korp.CriarProdutoAsync(saldo: 10);
        var chave = $"corrida-mesma-chave-{Guid.NewGuid()}";
        var corpo = new
        {
            notaFiscalId = 1,
            notaNumero = 1,
            itens = new[] { new { produtoId = produto.Id, quantidade = 1 } },
        };

        var respostas = await Task.WhenAll(
            Enumerable.Range(0, 6).Select(_ => korp.BaixarAsync(chave, corpo)));

        Assert.All(respostas, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        Assert.Equal(9, await korp.SaldoAsync(produto.Id));
    }

    // ==================================================================
    // CONCORRENCIA (opcional A do enunciado)
    // ==================================================================

    [Fact]
    public async Task Ultima_unidade_disputada_por_varias_notas_sai_uma_vez_so()
    {
        // O cenario que o enunciado descreve: produto com saldo 1 sendo usado ao mesmo
        // tempo por mais de uma nota. Aqui sao OITO pedidos simultaneos, cada um com a
        // propria chave (ou seja, pedidos legitimamente diferentes).
        var produto = await korp.CriarProdutoAsync(saldo: 1);

        var respostas = await Task.WhenAll(
            Enumerable.Range(0, 8).Select(i => korp.BaixarAsync($"disputa-{i}-{Guid.NewGuid()}", new
            {
                notaFiscalId = 100 + i,
                notaNumero = 100 + i,
                itens = new[] { new { produtoId = produto.Id, quantidade = 1 } },
            })));

        var venceram = respostas.Count(r => r.StatusCode == HttpStatusCode.OK);
        var perderam = respostas.Count(r => r.StatusCode == HttpStatusCode.Conflict);

        Assert.Equal(1, venceram);
        Assert.Equal(7, perderam);

        foreach (var recusada in respostas.Where(r => r.StatusCode == HttpStatusCode.Conflict))
            Assert.Equal("SALDO_INSUFICIENTE", await AmbienteKorp.CodigoDoErroAsync(recusada));

        // O que realmente importa: nunca ficou negativo.
        Assert.Equal(0, await korp.SaldoAsync(produto.Id));
    }

    [Fact]
    public async Task Com_saldo_cinco_e_dez_pedidos_simultaneos_exatamente_cinco_passam()
    {
        // Versao mais dura do teste anterior: prova que o limite respeitado e o SALDO,
        // e nao apenas "so um passa por acaso".
        var produto = await korp.CriarProdutoAsync(saldo: 5);

        var respostas = await Task.WhenAll(
            Enumerable.Range(0, 10).Select(i => korp.BaixarAsync($"lote-{i}-{Guid.NewGuid()}", new
            {
                notaFiscalId = 200 + i,
                notaNumero = 200 + i,
                itens = new[] { new { produtoId = produto.Id, quantidade = 1 } },
            })));

        Assert.Equal(5, respostas.Count(r => r.StatusCode == HttpStatusCode.OK));
        Assert.Equal(5, respostas.Count(r => r.StatusCode == HttpStatusCode.Conflict));
        Assert.Equal(0, await korp.SaldoAsync(produto.Id));
    }

    [Fact]
    public async Task Baixa_com_varios_itens_falha_inteira_se_um_item_nao_tiver_saldo()
    {
        // Ou vai tudo, ou nao vai nada: a baixa acontece dentro de uma transacao.
        var bom = await korp.CriarProdutoAsync(saldo: 10);
        var curto = await korp.CriarProdutoAsync(saldo: 1);

        var resposta = await korp.BaixarAsync($"parcial-{Guid.NewGuid()}", new
        {
            notaFiscalId = 300,
            notaNumero = 300,
            itens = new[]
            {
                new { produtoId = bom.Id, quantidade = 2 },
                new { produtoId = curto.Id, quantidade = 5 },
            },
        });

        Assert.Equal(HttpStatusCode.Conflict, resposta.StatusCode);

        // O primeiro item NAO pode ter sido baixado.
        Assert.Equal(10, await korp.SaldoAsync(bom.Id));
        Assert.Equal(1, await korp.SaldoAsync(curto.Id));
    }
}

public record PaginaDto<T>(List<T> Itens, int Total, int Pagina, int Tamanho);
