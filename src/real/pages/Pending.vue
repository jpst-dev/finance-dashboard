<script setup lang="ts">
import { computed, onMounted, ref } from "vue";
import { useI18n } from "vue-i18n";
import type { TransactionState } from "../api/types";
import LinkedPairs from "../components/LinkedPairs.vue";
import LinkSuggestions from "../components/LinkSuggestions.vue";
import CategorySelect from "../components/CategorySelect.vue";
import Pagination from "../components/Pagination.vue";
import Money from "../money/Money.vue";
import { useAccountsStore } from "../stores/accounts";
import { useCategoriesStore } from "../stores/categories";
import { useLinksStore } from "../stores/links";
import { useTransactionsStore } from "../stores/transactions";

const { t } = useI18n();
const store = useTransactionsStore();
const accounts = useAccountsStore();
const categories = useCategoriesStore();
const links = useLinksStore();
const status = ref("");

onMounted(async () => {
  void accounts.load();
  void categories.load();
  void store.loadPendingCount();
  void links.load();
  await store.resetFilters({ state: "PendingClassification" });
});

const accountName = (id: string) => accounts.items.find((a) => a.id === id)?.name ?? "—";
const errorText = computed(() => {
  const e = store.error ?? accounts.error ?? categories.error ?? links.error;
  return e ? t(`real.errors.${e}`) : "";
});
function fmtDate(d: string): string {
  return /^\d{4}-\d{2}-\d{2}$/.test(d) ? `${d.slice(8, 10)}/${d.slice(5, 7)}/${d.slice(0, 4)}` : d;
}
async function setState(id: string, e: Event) {
  const state = (e.target as HTMLSelectElement).value as TransactionState;
  if (await store.classify(id, { state })) status.value = t("real.pending.saved");
}
async function setCategory(id: string, categoryId: string | null) {
  if (await store.classify(id, { categoryId })) status.value = t("real.pending.saved");
}
const field =
  "rounded border border-gray-600 bg-gray-800 px-2 py-1 focus-visible:outline focus-visible:outline-2 focus-visible:outline-blue-400";
</script>

<template>
  <section>
    <h1 class="text-xl font-semibold">{{ t("real.nav.pending") }}</h1>
    <p class="mt-1 text-sm text-gray-400">{{ t("real.pending.help") }}</p>
    <p class="sr-only" role="status" aria-live="polite">{{ status }}</p>
    <p v-if="errorText" class="mt-2 text-sm text-red-400" role="alert" data-test="error">{{ errorText }}</p>

    <LinkSuggestions />
    <LinkedPairs />

    <p v-if="store.loading && store.items.length === 0" class="mt-4 text-gray-400">{{ t("real.transactions.loading") }}</p>
    <p v-else-if="!store.loading && store.items.length === 0 && !store.error" class="mt-4 text-gray-400" data-test="empty">
      {{ t("real.pending.empty") }}
    </p>
    <ul class="mt-4 space-y-3">
      <li v-for="tx in store.items" :key="tx.id" class="rounded border border-gray-700 p-3" data-test="pending-row">
        <div class="flex flex-wrap items-baseline justify-between gap-2">
          <span data-test="tx-desc">{{ tx.description }}</span>
          <span data-test="tx-amount"><Money :value="tx.amount" signed /></span>
        </div>
        <p class="text-xs text-gray-400">{{ fmtDate(tx.date) }} · {{ accountName(tx.accountId) }}</p>
        <div class="mt-2 flex flex-wrap items-end gap-3">
          <div>
            <label :for="`ps-${tx.id}`" class="mb-1 block text-sm">{{ t("real.transactions.state") }}</label>
            <select :id="`ps-${tx.id}`" :class="field" :value="tx.state" data-test="state-select" @change="setState(tx.id, $event)">
              <option value="Normal">{{ t("real.transactions.states.Normal") }}</option>
              <option value="PendingClassification">{{ t("real.transactions.states.PendingClassification") }}</option>
            </select>
          </div>
          <div>
            <label :for="`pc-${tx.id}`" class="mb-1 block text-sm">{{ t("real.transactions.category") }}</label>
            <CategorySelect :id="`pc-${tx.id}`" :model-value="tx.categoryId" :amount="tx.amount" @update:model-value="setCategory(tx.id, $event)" />
          </div>
        </div>
      </li>
    </ul>
    <Pagination :page="store.page" :size="store.size" :total="store.total" @update:page="store.setPage($event)" />
  </section>
</template>
