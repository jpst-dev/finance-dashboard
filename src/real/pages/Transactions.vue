<script setup lang="ts">
import { computed, onMounted, ref } from "vue";
import { useI18n } from "vue-i18n";
import { useRouter } from "vue-router";
import type { Transaction, TransactionState } from "../api/types";
import Pagination from "../components/Pagination.vue";
import TransactionForm from "../components/TransactionForm.vue";
import TransactionTable from "../components/TransactionTable.vue";
import { useAccountsStore } from "../stores/accounts";
import { suggestPattern } from "../rules/suggestPattern";
import { useCategoriesStore } from "../stores/categories";
import { useCategoryRulesStore } from "../stores/categoryRules";
import { useTransactionsStore, type TransactionInput } from "../stores/transactions";

const { t } = useI18n();
const store = useTransactionsStore();
const accounts = useAccountsStore();
const categories = useCategoriesStore();

const creating = ref(false);
const editing = ref<Transaction | null>(null);
const confirming = ref<Transaction | null>(null);
const status = ref("");
const rules = useCategoryRulesStore();
const router = useRouter();
/** Atalho "Sempre categorizar…" mostrado depois de salvar uma edição com categoria. */
const shortcut = ref<{ pattern: string; categoryId: string } | null>(null);

onMounted(async () => {
  void accounts.load();
  void categories.load();
  void store.loadPendingCount();
  await store.resetFilters({});
});

const accountName = (id: string) => accounts.items.find((a) => a.id === id)?.name ?? "—";
const categoryName = (id: string | null) => (id ? (categories.items.find((c) => c.id === id)?.name ?? "—") : t("real.categories.none"));
const errorText = computed(() => {
  const e = store.error ?? accounts.error ?? categories.error;
  return e ? t(`real.errors.${e}`) : "";
});

function filterVal(e: Event): string | undefined {
  return (e.target as HTMLInputElement | HTMLSelectElement).value || undefined;
}
function startEdit(tx: Transaction) {
  shortcut.value = null;
  editing.value = tx;
  creating.value = false;
}

async function onCreate(v: TransactionInput & { accountId: string }) {
  if (await store.create(v)) {
    creating.value = false;
    status.value = t("real.transactions.created");
  }
}
async function onEdit(v: TransactionInput) {
  const cur = editing.value;
  if (cur && (await store.update(cur.id, v))) {
    editing.value = null;
    status.value = t("real.transactions.updated");
    // Só quando a categoria mudou para um valor não nulo.
    // O trecho vem da descrição ORIGINAL (a renomeada não é a que a regra casa).
    const changed = !!v.categoryId && v.categoryId !== cur.categoryId;
    const p = changed ? suggestPattern(cur.originalDescription) : null;
    shortcut.value = p && v.categoryId ? { pattern: p, categoryId: v.categoryId } : null;
  }
}
function goCreateRule() {
  if (!shortcut.value) return;
  rules.suggestion = { ...shortcut.value };
  shortcut.value = null;
  void router.push("/rules");
}
async function onDelete() {
  const tx = confirming.value;
  if (!tx) return;
  confirming.value = null;
  if (await store.remove(tx.id)) status.value = t("real.transactions.deleted");
}
const field =
  "rounded border border-gray-600 bg-gray-800 px-2 py-1 focus-visible:outline focus-visible:outline-2 focus-visible:outline-blue-400";
const btn =
  "rounded border border-gray-600 px-3 py-1 text-sm focus-visible:outline focus-visible:outline-2 focus-visible:outline-blue-300";
const stateOptions: TransactionState[] = ["Normal", "PendingClassification"];
</script>

<template>
  <section>
    <h1 class="text-xl font-semibold">{{ t("real.nav.transactions") }}</h1>
    <p class="sr-only" role="status" aria-live="polite">{{ status }}</p>
    <p v-if="errorText" class="mt-2 text-sm text-red-400" role="alert" data-test="error">{{ errorText }}</p>

    <div role="status" aria-live="polite">
      <div v-if="shortcut" class="mt-3 flex flex-wrap items-center gap-3 rounded border border-blue-700 p-3 text-sm" data-test="rule-shortcut">
        <span data-test="rule-shortcut-text">{{ t("real.rules.shortcut", { pattern: shortcut.pattern, category: categoryName(shortcut.categoryId) }) }}</span>
        <button type="button" :class="btn" data-test="rule-shortcut-go" @click="goCreateRule">{{ t("real.rules.shortcutButton") }}</button>
        <button type="button" :class="btn" data-test="rule-shortcut-dismiss" @click="shortcut = null">{{ t("real.rules.shortcutDismiss") }}</button>
      </div>
    </div>

    <div class="mt-3 flex flex-wrap items-end gap-3" role="group" :aria-label="t('real.transactions.filters')">
      <div>
        <label for="f-account" class="mb-1 block text-sm">{{ t("real.transactions.account") }}</label>
        <select id="f-account" :class="field" :value="store.filters.accountId ?? ''" @change="store.setFilters({ accountId: filterVal($event) })">
          <option value="">{{ t("real.transactions.all") }}</option>
          <option v-for="a in accounts.items" :key="a.id" :value="a.id">
            {{ a.name }}{{ a.isArchived ? ` (${t("real.transactions.archived")})` : "" }}
          </option>
        </select>
      </div>
      <div>
        <label for="f-month" class="mb-1 block text-sm">{{ t("real.transactions.month") }}</label>
        <input id="f-month" type="month" :class="field" :value="store.filters.month ?? ''" @change="store.setFilters({ month: filterVal($event) })" />
      </div>
      <div>
        <label for="f-category" class="mb-1 block text-sm">{{ t("real.transactions.category") }}</label>
        <select id="f-category" :class="field" :value="store.filters.categoryId ?? ''" @change="store.setFilters({ categoryId: filterVal($event) })">
          <option value="">{{ t("real.transactions.all") }}</option>
          <option v-for="c in categories.items" :key="c.id" :value="c.id">{{ c.name }}</option>
        </select>
      </div>
      <div>
        <label for="f-state" class="mb-1 block text-sm">{{ t("real.transactions.state") }}</label>
        <select id="f-state" :class="field" :value="store.filters.state ?? ''" @change="store.setFilters({ state: filterVal($event) as TransactionState | undefined })">
          <option value="">{{ t("real.transactions.all") }}</option>
          <option v-for="s in stateOptions" :key="s" :value="s">{{ t(`real.transactions.states.${s}`) }}</option>
        </select>
      </div>
      <button v-if="!creating && !editing" type="button" :class="btn" data-test="new-tx" @click="creating = true">
        {{ t("real.transactions.new") }}
      </button>
    </div>

    <TransactionForm v-if="creating" :accounts="accounts.active" :busy="store.busy" @submit="onCreate" @cancel="creating = false" />
    <TransactionForm
      v-if="editing"
      :key="editing.id"
      :accounts="accounts.items"
      :transaction="editing"
      :account-name="accountName(editing.accountId)"
      :busy="store.busy"
      @submit="onEdit"
      @cancel="editing = null"
    />

    <div v-if="confirming" class="mt-3 rounded border border-amber-700 p-3" role="alertdialog" :aria-label="t('real.transactions.delete')" data-test="confirm-delete">
      <p>{{ confirming.origin === "Imported" ? t("real.transactions.deleteImported") : t("real.transactions.deleteManual") }}</p>
      <div class="mt-2 flex gap-2">
        <button type="button" :class="btn" data-test="confirm-yes" @click="onDelete">{{ t("real.transactions.deleteYes") }}</button>
        <button type="button" :class="btn" @click="confirming = null">{{ t("real.transactions.cancel") }}</button>
      </div>
    </div>

    <p v-if="store.loading && store.items.length === 0" class="mt-4 text-gray-400">{{ t("real.transactions.loading") }}</p>
    <p v-else-if="!store.loading && store.items.length === 0 && !store.error" class="mt-4 text-gray-400" data-test="empty">
      {{ t("real.transactions.empty") }}
    </p>
    <TransactionTable
      v-if="store.items.length > 0"
      :items="store.items"
      :account-name="accountName"
      :category-name="categoryName"
      :busy="store.busy"
      @edit="startEdit"
      @remove="confirming = $event"
      @own="store.markOwn($event.id)"
      @no-counterpart="store.markNoCounterpart($event.id)"
    />
    <Pagination :page="store.page" :size="store.size" :total="store.total" @update:page="store.setPage($event)" />
  </section>
</template>
