<script setup lang="ts">
import { computed, onMounted, ref, watch } from "vue";
import { Bar } from "vue-chartjs";
import { useI18n } from "vue-i18n";
import { chartMoneyOptions, toPlot } from "../money/chart";
import { formatMoney } from "../money/format";
import Money from "../money/Money.vue";
import { todayISO } from "../money/today";
import { useValuesVisibility } from "../money/visibility";
import { staleBases, useInstallmentsStore } from "../stores/installments";
import { monthLabel } from "../stores/summary";
import "../components/chartSetup";

const { t, locale } = useI18n();
const store = useInstallmentsStore();
const visibility = useValuesVisibility();
const chart = ref<{ chart?: { update: () => void } } | null>(null);
const loc = () => String(locale.value);

onMounted(() => void store.load());

const months = computed(() => store.data?.months ?? []);
const groups = computed(() => store.data?.groups ?? []);
const stale = computed(() =>
  staleBases(store.data, todayISO()).map((a) => ({
    id: a.accountId,
    text: t("real.installments.staleBase", { account: a.accountName, month: `${a.basedOn.slice(5, 7)}/${a.basedOn.slice(0, 4)}` }),
  })),
);
const empty = computed(() => !!store.data && groups.value.length === 0 && months.value.length === 0);

const data = computed(() => ({
  labels: months.value.map((m) => monthLabel(m.month, loc(), "short")),
  datasets: [{ label: t("real.installments.chartTitle"), data: months.value.map((m) => toPlot(m.total)), backgroundColor: "#f59e0b" }],
}));
const options = computed(() => ({ responsive: true, maintainAspectRatio: false, ...chartMoneyOptions(loc) }));
watch([() => visibility.hidden, () => locale.value], () => chart.value?.chart?.update());

const account = (g: { accountName: string; cardSuffix: string | null }) =>
  g.cardSuffix ? `${g.accountName} (${g.cardSuffix})` : g.accountName;
</script>

<template>
  <section>
    <h1 class="text-xl font-semibold">{{ t("real.nav.installments") }}</h1>
    <p class="mt-1 text-sm text-gray-400">{{ t("real.installments.help") }}</p>

    <p v-if="store.error" class="mt-3 text-sm text-red-300" role="alert" data-test="error">
      {{ t(`real.errors.${store.error}`) }}
      <button type="button" class="ml-2 underline" data-test="retry" @click="store.load()">{{ t("real.installments.retry") }}</button>
    </p>
    <p v-else-if="store.loading && !store.data" class="mt-3 text-gray-400" role="status" aria-busy="true" data-test="loading">
      {{ t("real.installments.loading") }}
    </p>

    <ul v-if="stale.length" class="mt-3 space-y-1" role="status">
      <li
        v-for="s in stale"
        :key="s.id"
        class="rounded border border-amber-600 bg-amber-950/40 px-3 py-2 text-sm"
        data-test="stale-base"
      >
        {{ s.text }}
      </li>
    </ul>

    <section v-if="empty" class="mt-4 rounded border border-gray-700 p-4" aria-labelledby="inst-empty-h" data-test="empty">
      <h2 id="inst-empty-h" class="font-semibold">{{ t("real.installments.emptyTitle") }}</h2>
      <p class="mt-1 text-sm text-gray-300">{{ t("real.installments.emptyBody") }}</p>
      <RouterLink to="/import" class="mt-2 inline-block underline focus-visible:outline focus-visible:outline-2 focus-visible:outline-blue-300">{{
        t("real.installments.emptyAction")
      }}</RouterLink>
    </section>

    <template v-else-if="store.data">
      <p class="mt-4" data-test="total">
        {{ t("real.installments.total") }}: <strong><Money :value="store.data.totalCommitted" /></strong>
      </p>
      <h2 class="mt-4 mb-2 text-lg font-semibold">{{ t("real.installments.chartTitle") }}</h2>
      <div class="h-64">
        <Bar ref="chart" :data="data" :options="options" role="img" :aria-label="t('real.installments.chartAlt')" />
      </div>
      <ul class="sr-only" data-test="chart-alt">
        <li v-for="m in months" :key="m.month">
          {{ monthLabel(m.month, loc()) }}:
          <template v-if="visibility.hidden">{{ t("real.installments.valuesHidden") }}</template>
          <template v-else>{{ formatMoney(m.total, loc()) }}</template>
        </li>
      </ul>

      <h2 class="mt-6 mb-2 text-lg font-semibold">{{ t("real.installments.groupsTitle") }}</h2>
      <table class="w-full text-left text-sm" data-test="groups-table" :aria-label="t('real.installments.groupsTitle')">
        <thead>
          <tr class="border-b border-gray-700 text-gray-400">
            <th scope="col" class="py-1 pr-3">{{ t("real.installments.colDescription") }}</th>
            <th scope="col" class="py-1 pr-3">{{ t("real.installments.colAccount") }}</th>
            <th scope="col" class="py-1 pr-3">{{ t("real.installments.colProgress") }}</th>
            <th scope="col" class="py-1 pr-3">{{ t("real.installments.colRemaining") }}</th>
            <th scope="col" class="py-1 pr-3">{{ t("real.installments.colAmount") }}</th>
            <th scope="col" class="py-1">{{ t("real.installments.colEnds") }}</th>
          </tr>
        </thead>
        <tbody>
          <tr
            v-for="(g, i) in groups"
            :key="`${g.accountId}-${g.description}-${g.installmentNumber}-${g.amount}-${i}`"
            class="border-b border-gray-800"
            data-test="group-row"
          >
            <td class="py-1 pr-3 break-all" data-test="group-description">
              {{ g.description }}<span v-if="g.multiplicity > 1" class="ml-1 text-gray-400">{{ t("real.installments.times", { k: g.multiplicity }) }}</span>
            </td>
            <td class="py-1 pr-3 break-all">{{ account(g) }}</td>
            <td class="py-1 pr-3" data-test="group-progress">{{ t("real.installments.nOfT", { n: g.installmentNumber, total: g.installmentTotal }) }}</td>
            <td class="py-1 pr-3" data-test="group-remaining">{{ t("real.plural.installmentsRemaining", { n: g.remaining }, g.remaining) }}</td>
            <td class="py-1 pr-3" data-test="group-amount"><Money :value="g.amount" /></td>
            <td class="py-1" data-test="group-ends">{{ monthLabel(g.endsOn, loc()) }}</td>
          </tr>
        </tbody>
      </table>
    </template>
  </section>
</template>
