import { formatMoney } from "./format";
import { MASK, useValuesVisibility } from "./visibility";

/**
 * ÚNICO ponto em que dinheiro vira `number`: só para desenhar no Chart.js.
 * Nunca some/subtraia o resultado; o valor exibido vem sempre da string.
 */
export function toPlot(value: string): number {
  return Number(value);
}

// O Chart.js chama os callbacks depois; o estado é lido no momento da chamada.
function hiddenNow(): boolean {
  return useValuesVisibility().hidden;
}

function fmt(value: number | string, locale: string): string {
  // Número vindo do gráfico: 2 casas só para exibição, nunca de volta ao estado.
  return formatMoney(typeof value === "number" ? value.toFixed(2) : value, locale);
}

export const chartMoney = {
  tooltip(value: number | string, locale: string): string {
    return hiddenNow() ? MASK : fmt(value, locale);
  },
  tick(value: number | string, locale: string): string {
    return hiddenNow() ? MASK : fmt(value, locale);
  },
};

export interface TooltipLabelContext {
  parsed: { y: number | null };
  dataset: { label?: string };
}

/** Opções Chart.js (tooltip.label e eixo y) que respeitam "esconder valores". */
export function chartMoneyOptions(getLocale: () => string) {
  return {
    plugins: {
      tooltip: {
        callbacks: {
          label(ctx: TooltipLabelContext): string {
            if (hiddenNow()) return MASK; // nem o nome da série acompanha o valor
            const v = ctx.parsed.y ?? 0;
            const name = ctx.dataset.label ? `${ctx.dataset.label}: ` : "";
            return name + chartMoney.tooltip(v, getLocale());
          },
        },
      },
    },
    scales: {
      y: {
        ticks: {
          callback(value: number | string): string {
            return chartMoney.tick(value, getLocale());
          },
        },
      },
    },
  };
}
