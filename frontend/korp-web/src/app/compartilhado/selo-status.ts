import { Component, Input } from '@angular/core';
import { StatusNota } from '../nucleo/modelos';

/** Selo colorido do status da nota, usado na lista e no detalhe. */
@Component({
  selector: 'korp-selo-status',
  template: `<span class="selo" [class]="'selo-' + status.toLowerCase()">{{ status }}</span>`,
  styles: `
    .selo {
      display: inline-block;
      padding: 3px 12px;
      border-radius: 999px;
      font-size: 12px;
      font-weight: 700;
      letter-spacing: 0.4px;
      text-transform: uppercase;
    }
    .selo-aberta {
      background: rgba(214, 146, 20, 0.18);
      color: #8a5a00;
    }
    .selo-processando {
      background: rgba(103, 58, 183, 0.16);
      color: #5e35b1;
    }
    .selo-fechada {
      background: rgba(46, 125, 70, 0.16);
      color: #2e7d46;
    }
  `,
})
export class SeloStatus {
  @Input({ required: true }) status!: StatusNota;
}
