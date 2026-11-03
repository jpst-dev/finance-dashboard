<script setup lang="ts">
import { computed, onMounted } from "vue";
import { useI18n } from "vue-i18n";
import AccountStrip from "../components/AccountStrip.vue";
import CategoryChart from "../components/CategoryChart.vue";
import CommittedCard from "../components/CommittedCard.vue";
import MonthPicker from "../components/MonthPicker.vue";
import PendingNotice from "../components/PendingNotice.vue";
import RecentTransactions from "../components/RecentTransactions.vue";
import SixMonthChart from "../components/SixMonthChart.vue";
import SummaryCards from "../components/SummaryCards.vue";
import { useAccountsStore } from "../stores/accounts";
import { useInstallmentsStore } from "../stores/installments";
import { useSummaryStore } from "../stores/summary";
import { useTransactionsStore } from "../stores/transactions";

const { t } = useI18n();
const summary = useSummaryStore();
const accounts = useAccountsStore();
const transactions = useTransactionsStore();
const installments = useInstallmentsStore();

onMounted(() => {
  void accounts.load();
  void transactions.loadPendingCount();
  void summary.loadAll();
  void summary.loadRecent();
  void installments.load();
});

// Sem nenhum lançamento: oferece importar em vez de gráficos vazios.
const empty = computed(() => summary.recentTotal === 0 && !summary.recentError);
</script>

<template>
  <div class="space-y-6">
    <h1 class="text-xl font-semibold">{{ t("real.nav.overview") }}</h1>

    <AccountStrip />
    <PendingNotice />
    <CommittedCard />

    <MonthPicker :month="summary.month" :can-next="summary.canGoNext" @prev="summary.prev()" @next="summary.next()" />

    <p v-if="summary.error && !summary.loading" class="text-sm text-red-300" role="alert" data-test="summary-error">
      {{ t(`real.errors.${summary.error}`) }}
      <button type="button" class="ml-2 underline" data-test="summary-retry" @click="summary.loadAll()">
        {{ t("real.overview.retry") }}
      </button>
    </p>
    <p v-else-if="summary.loading || !summary.current" class="text-sm text-gray-400" role="status" aria-busy="true" data-test="summary-loading">
      {{ t("real.overview.loading") }}
    </p>
    <SummaryCards v-else :summary="summary.current" />

    <section v-if="empty" class="rounded border border-gray-700 p-4" aria-labelledby="empty-h" data-test="empty-state">
      <h2 id="empty-h" class="font-semibold">{{ t("real.overview.emptyTitle") }}</h2>
      <p class="mt-1 text-sm text-gray-300">{{ t("real.overview.emptyBody") }}</p>
      <RouterLink to="/import" class="mt-2 inline-block underline focus-visible:outline focus-visible:outline-2 focus-visible:outline-blue-300">{{
        t("real.overview.emptyAction")
      }}</RouterLink>
    </section>
    <template v-else-if="!summary.loading">
      <SixMonthChart v-if="summary.history.some((h) => h.summary)" :history="summary.history" @retry="summary.retryMonth($event)" />
      <CategoryChart v-if="summary.current" :summary="summary.current" />
      <RecentTransactions />
    </template>
  </div>
</template>
