<script setup lang="ts">
import { computed, ref, watch } from "vue";
import { Doughnut } from "vue-chartjs";
import { useI18n } from "vue-i18n";
import type { Summary } from "../api/types";
import { chartMoney, toPlot } from "../money/chart";
import { moneyParts } from "../money/format";
import Money from "../money/Money.vue";
import { MASK, useValuesVisibility } from "../money/visibility";
import "./chartSetup";

const props = defineProps<{ summary: Summary }>();
const { t, locale } = useI18n();
const visibility = useValuesVisibility();
const chart = ref<{ chart?: { update: () => void } } | null>(null);
const loc = () => String(locale.value);

const PALETTE = ["#3b82f6", "#f59e0b", "#10b981", "#a855f7", "#f43f5e", "#14b8a6", "#eab308", "#6366f1", "#84cc16", "#ec4899"];

// Só despesas: total negativo, decidido pelo sinal da string (sem aritmética). Uma categoria de despesa com saldo
// líquido positivo (ex.: estorno) é excluída DE PROPÓSITO do gráfico: não existe fatia negativa.
const rows = computed(() =>
  props.summary.byCategory
    .filter((c) => {
      const p = moneyParts(c.total, "en-US");
      return p !== null && p.negative && !p.zero;
    })
    .map((c) => ({
      key: c.categoryId ?? "none",
      name: c.categoryId && c.categoryName ? c.categoryName : t("real.overview.uncategorized"),
      total: c.total,
    })),
);

const data = computed(() => ({
  labels: rows.value.map((r) => r.name),
  datasets: [
    {
      data: rows.value.map((r) => Math.abs(toPlot(r.total))),
      backgroundColor: rows.value.map((_, i) => PALETTE[i % PALETTE.length]),
    },
  ],
}));

const options = computed(() => ({
  responsive: true,
  maintainAspectRatio: false,
  plugins: {
    legend: { display: false }, // a lista ao lado faz o papel da legenda (só nomes no gráfico)
    tooltip: {
      callbacks: {
        // Doughnut: `parsed` é número. Lido no momento da chamada, mascarado quando oculto.
        label(ctx: { parsed: number; label?: string }): string {
          if (visibility.hidden) return MASK;
          return `${ctx.label ?? ""}: ${chartMoney.tooltip(ctx.parsed, loc())}`;
        },
      },
    },
  },
}));

watch([() => visibility.hidden, () => locale.value], () => chart.value?.chart?.update());
</script>

<template>
  <section aria-labelledby="cat-h" data-test="category-chart">
    <h2 id="cat-h" class="mb-2 text-lg font-semibold">{{ t("real.overview.categoriesTitle") }}</h2>
    <p v-if="!rows.length" class="text-sm text-gray-400" data-test="category-empty">{{ t("real.overview.categoriesEmpty") }}</p>
    <div v-else class="grid items-center gap-4 sm:grid-cols-2">
      <div class="h-56">
        <Doughnut ref="chart" :data="data" :options="options" role="img" :aria-label="t('real.overview.categoriesAlt')" />
      </div>
      <ul class="space-y-1 text-sm">
        <li v-for="(r, i) in rows" :key="r.key" class="flex items-center justify-between gap-3">
          <span
            ><span
              class="mr-2 inline-block h-3 w-3 rounded-sm align-middle"
              :style="{ backgroundColor: PALETTE[i % PALETTE.length] }"
              aria-hidden="true"
            ></span
            >{{ r.name }}</span
          >
          <Money :value="r.total" />
        </li>
      </ul>
    </div>
  </section>
</template>
