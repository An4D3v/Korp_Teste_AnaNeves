import { Routes } from '@angular/router';

export const routes: Routes = [
  { path: '', redirectTo: 'produtos', pathMatch: 'full' },

  // Carregamento sob demanda (lazy loading): cada tela vira um pedaco separado
  // do bundle e so e baixada quando a pessoa entra nela.
  {
    path: 'produtos',
    title: 'Produtos | Korp',
    loadComponent: () =>
      import('./funcionalidades/produtos/produtos-pagina').then((m) => m.ProdutosPagina),
  },
  {
    path: 'notas',
    title: 'Notas fiscais | Korp',
    loadComponent: () =>
      import('./funcionalidades/notas/notas-pagina').then((m) => m.NotasPagina),
  },
  {
    path: 'notas/nova',
    title: 'Nova nota | Korp',
    loadComponent: () =>
      import('./funcionalidades/notas/nota-nova').then((m) => m.NotaNova),
  },
  {
    path: 'notas/:id',
    title: 'Nota fiscal | Korp',
    loadComponent: () =>
      import('./funcionalidades/notas/nota-detalhe').then((m) => m.NotaDetalhe),
  },

  { path: '**', redirectTo: 'produtos' },
];
