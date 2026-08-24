import {
  Component,
  Input,
  OnChanges,
  SimpleChanges,
  signal,
} from '@angular/core';
import { MatTooltipModule } from '@angular/material/tooltip';

/**
 * Bolinha de status de um servico na barra do topo.
 *
 * Este componente existe tambem para demonstrar o ciclo de vida ngOnChanges:
 * ele nao guarda estado proprio de negocio, ele REAGE a mudanca do @Input.
 * Quando o servico cai ou volta, o texto e a cor sao recalculados aqui,
 * e nao espalhados pelo template do pai.
 */
@Component({
  selector: 'korp-indicador-servico',
  imports: [MatTooltipModule],
  template: `
    <span class="indicador" [class]="classe()" [matTooltip]="dica()">
      <span class="bolinha"></span>
      {{ nome }}
    </span>
  `,
  styles: `
    .indicador {
      display: inline-flex;
      align-items: center;
      gap: 6px;
      font-size: 12px;
      font-weight: 500;
      padding: 3px 10px 3px 8px;
      border-radius: 999px;
      background: rgba(255, 255, 255, 0.14);
      white-space: nowrap;
    }
    .bolinha {
      width: 8px;
      height: 8px;
      border-radius: 50%;
      background: #bdbdbd;
      flex: none;
    }
    .no-ar .bolinha {
      background: #69f0ae;
      box-shadow: 0 0 6px #69f0ae;
    }
    .fora .bolinha {
      background: #ff5252;
      box-shadow: 0 0 6px #ff5252;
      animation: pulsar 1.1s ease-in-out infinite;
    }
    .fora {
      background: rgba(255, 82, 82, 0.28);
    }
    @keyframes pulsar {
      50% {
        opacity: 0.35;
      }
    }
  `,
})
export class IndicadorServico implements OnChanges {
  /** Nome curto exibido ao lado da bolinha. */
  @Input({ required: true }) nome!: string;

  /** null = ainda verificando, true = no ar, false = fora do ar. */
  @Input({ required: true }) noAr!: boolean | null;

  protected readonly classe = signal('verificando');
  protected readonly dica = signal('Verificando...');

  ngOnChanges(mudancas: SimpleChanges): void {
    if (!('noAr' in mudancas)) return;

    if (this.noAr === null) {
      this.classe.set('verificando');
      this.dica.set(`Verificando o servico de ${this.nome}...`);
      return;
    }

    if (this.noAr) {
      this.classe.set('no-ar');
      this.dica.set(`Servico de ${this.nome} respondendo normalmente.`);
      return;
    }

    this.classe.set('fora');
    this.dica.set(
      `Servico de ${this.nome} nao esta respondendo. As operacoes que dependem dele vao falhar com aviso.`,
    );
  }
}
