import { Component, inject } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import {
  MAT_DIALOG_DATA,
  MatDialogModule,
  MatDialogRef,
} from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { Produto } from '../../nucleo/modelos';

export interface DadosFormularioProduto {
  produto?: Produto;
}

/**
 * Formulario de produto em janela (dialogo), usado tanto para criar quanto para editar.
 *
 * Formularios reativos (ReactiveFormsModule): a validacao vive no TypeScript, nao no HTML,
 * o que deixa a regra testavel e evita validacao duplicada em dois lugares.
 * O codigo do produto nao pode ser trocado depois de criado, entao no modo edicao
 * o campo e desabilitado em vez de escondido: a pessoa continua vendo qual produto e.
 */
@Component({
  selector: 'korp-produto-formulario',
  imports: [
    ReactiveFormsModule,
    MatDialogModule,
    MatFormFieldModule,
    MatInputModule,
    MatButtonModule,
  ],
  template: `
    <h2 mat-dialog-title>{{ editando ? 'Editar produto' : 'Novo produto' }}</h2>

    <mat-dialog-content>
      <form [formGroup]="form" class="form" (ngSubmit)="salvar()">
        <mat-form-field appearance="outline">
          <mat-label>Codigo</mat-label>
          <input matInput formControlName="codigo" placeholder="P-007" maxlength="30" />
          @if (form.controls.codigo.hasError('required') && form.controls.codigo.touched) {
            <mat-error>Informe o codigo do produto.</mat-error>
          }
          @if (editando) {
            <mat-hint>O codigo nao muda depois que o produto e criado.</mat-hint>
          }
        </mat-form-field>

        <mat-form-field appearance="outline">
          <mat-label>Descricao</mat-label>
          <input matInput formControlName="descricao" placeholder="Caneta azul" maxlength="200" />
          @if (form.controls.descricao.hasError('required') && form.controls.descricao.touched) {
            <mat-error>Informe a descricao.</mat-error>
          }
        </mat-form-field>

        <mat-form-field appearance="outline">
          <mat-label>Saldo em estoque</mat-label>
          <input matInput type="number" formControlName="saldo" min="0" />
          @if (form.controls.saldo.hasError('min')) {
            <mat-error>O saldo nao pode ser negativo.</mat-error>
          }
        </mat-form-field>
      </form>
    </mat-dialog-content>

    <mat-dialog-actions align="end">
      <button matButton mat-dialog-close>Cancelar</button>
      <button matButton="filled" [disabled]="form.invalid" (click)="salvar()">Salvar</button>
    </mat-dialog-actions>
  `,
  styles: `
    .form {
      display: flex;
      flex-direction: column;
      gap: 6px;
      min-width: 340px;
      padding-top: 6px;
    }
  `,
})
export class ProdutoFormulario {
  private readonly fb = inject(FormBuilder);
  private readonly ref = inject(MatDialogRef<ProdutoFormulario>);
  private readonly dados = inject<DadosFormularioProduto>(MAT_DIALOG_DATA);

  protected readonly editando = !!this.dados?.produto;

  protected readonly form = this.fb.nonNullable.group({
    codigo: [
      { value: this.dados?.produto?.codigo ?? '', disabled: this.editando },
      [Validators.required, Validators.maxLength(30)],
    ],
    descricao: [
      this.dados?.produto?.descricao ?? '',
      [Validators.required, Validators.maxLength(200)],
    ],
    saldo: [this.dados?.produto?.saldo ?? 0, [Validators.required, Validators.min(0)]],
  });

  protected salvar(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    // getRawValue inclui os campos desabilitados (o codigo, no modo edicao).
    this.ref.close(this.form.getRawValue());
  }
}
