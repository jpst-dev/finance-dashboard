<script setup lang="ts">
import { computed, ref, watch } from "vue";
import { Bar } from "vue-chartjs";
import { useI18n } from "vue-i18n";
import { chartMoneyOptions, toPlot } from "../money/chart";
import { formatMoney } from "../money/format";
import { useValuesVisibility } from "../money/visibility";
import { monthLabel, type HistoryEntry } from "../stores/summary";
import "./chartSetup";

const props = defineProps<{ history: HistoryEntry[] }>();
const emit = defineEmits<{ retry: [month: string] }>();
const { t, locale } = useI18n();
const visibility = useValuesVisibility();
const chart = ref<{ chart?: { update: () => void } } | null>(null);

const loc = () => String(locale.value);
const labels = computed(() => props.history.map((h) => monthLabel(h.month, loc(), "short")));

// Despesas chegam negativas do servidor; desenha-se a magnitude (só no desenho, via toPlot).
const data = computed(() => ({
  labels: labels.value,
  datasets: [
    {
      label: t("real.overview.income"),
      data: props.history.map((h) => (h.summary ? toPlot(h.summary.income) : null)),
      backgroundColor: "#10b981",
    },
    {
      label: t("real.overview.expenses"),
      data: props.history.map((h) => (h.summary ? Math.abs(toPlot(h.summary.expenses)) : null)),
      backgroundColor: "#f43f5e",
    },
  ],
}));

const options = computed(() => ({
  responsive: true,
  maintainAspectRatio: false,
  ...chartMoneyOptions(loc),
}));

// Callbacks leem o estado ao serem chamados; o gráfico só precisa ser redesenhado.
watch([() => visibility.hidden, () => locale.value], () => chart.value?.chart?.update());

const ariaLabel = computed(() => t("real.overview.sixMonthAlt"));
const failed = computed(() => props.history.filter((h) => !h.summary));
</script>

<template>
  <section aria-labelledby="six-h" data-test="six-month">
    <h2 id="six-h" class="mb-2 text-lg font-semibold">{{ t("real.overview.sixMonthTitle") }}</h2>
    <div class="h-64">
      <Bar ref="chart" :data="data" :options="options" role="img" :aria-label="ariaLabel" />
    </div>
    <ul class="sr-only" data-test="chart-alt">
      <li v-for="h in history" :key="h.month">
        <template v-if="!h.summary">{{ monthLabel(h.month, loc()) }}: {{ t("real.overview.monthUnavailable") }}</template>
        <template v-else-if="visibility.hidden">{{ monthLabel(h.month, loc()) }}: {{ t("real.overview.valuesHidden") }}</template>
        <template v-else>
          {{ monthLabel(h.month, loc()) }}: {{ t("real.overview.income") }} {{ formatMoney(h.summary.income, loc()) }},
          {{ t("real.overview.expenses") }} {{ formatMoney(h.summary.expenses, loc()) }}
        </template>
      </li>
    </ul>
    <ul v-if="failed.length" class="mt-2 space-y-1 text-sm" data-test="chart-gaps">
      <li v-for="h in failed" :key="h.month">
        {{ t("real.overview.monthFailed", { month: monthLabel(h.month, loc()) }) }}
        <button
          type="button"
          class="ml-2 underline focus-visible:outline focus-visible:outline-2 focus-visible:outline-blue-300"
          data-test="retry-month"
          @click="emit('retry', h.month)"
        >
          {{ t("real.overview.retry") }}<span class="sr-only"> {{ monthLabel(h.month, loc()) }}</span>
        </button>
      </li>
    </ul>
  </section>
</template>
