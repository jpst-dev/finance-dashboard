<script setup lang="ts">
import { computed, ref, watch } from "vue";
import { useI18n } from "vue-i18n";
import type { Account, Transaction, TransactionState } from "../api/types";
import { parseMoneyInput } from "../money/input";
import { todayLocalISO } from "../money/today";
import { DESCRIPTION_MAX } from "../stores/transactions";
import CategorySelect from "./CategorySelect.vue";

const props = defineProps<{
  /** Contas ativas (criação). */
  accounts: Account[];
  /** Presente = edição. */
  transaction?: Transaction | null;
  accountName?: string;
  busy: boolean;
}>();
const emit = defineEmits<{
  submit: [v: { accountId: string; date: string; amount: string; description: string; categoryId: string | null; state: TransactionState }];
  cancel: [];
}>();
const { t } = useI18n();

const tx = props.transaction;
const editing = computed(() => !!props.transaction);
const accountId = ref(tx?.accountId ?? props.accounts[0]?.id ?? "");
const date = ref(tx?.date ?? todayLocalISO());
const kind = ref<"expense" | "income">(tx && !tx.amount.startsWith("-") ? "income" : "expense");
const amountText = ref(tx ? tx.amount.replace(/^-/, "") : "");
const description = ref(tx?.description ?? "");
const categoryId = ref<string | null>(tx?.categoryId ?? null);
const state = ref<TransactionState>(tx?.state ?? "Normal");
const errorKey = ref<string | null>(null);

// As contas podem carregar depois que o formulário abriu: segue a lista enquanto a escolha não existir nela.
watch(
  () => props.accounts,
  (accs) => {
    if (!editing.value && !accs.some((a) => a.id === accountId.value)) accountId.value = accs[0]?.id ?? "";
  },
);

// Digitar "-" no início equivale a escolher Despesa.
watch(amountText, (v) => {
  if (v.trim().startsWith("-")) kind.value = "expense";
});

/** String no formato da API ("-12.50") ou null se inválida/zero. */
const effective = computed<string | null>(() => {
  let txt = amountText.value.trim();
  let neg = kind.value === "expense";
  if (txt.startsWith("-")) {
    neg = true;
    txt = txt.slice(1);
  }
  const p = parseMoneyInput(txt);
  if (p === null || p.startsWith("-")) return null;
  const norm = p.replace(/^0+(?=\d)/, "");
  if (/^0(\.0+)?$/.test(norm)) return null;
  return (neg ? "-" : "") + norm;
});

const selectedAccount = computed(() => props.accounts.find((a) => a.id === accountId.value));
const isCard = computed(() => selectedAccount.value?.type === "CreditCard");

function onSubmit() {
  if (!accountId.value) {
    errorKey.value = "real.errors.invalid";
    return;
  }
  if (effective.value === null) {
    errorKey.value = "real.errors.invalid_amount";
    return;
  }
  if (!/^\d{4}-\d{2}-\d{2}$/.test(date.value)) {
    errorKey.value = "real.errors.invalid_date";
    return;
  }
  const d = description.value.trim();
  if (d.length < 1 || d.length > DESCRIPTION_MAX) {
    errorKey.value = "real.errors.invalid_description";
    return;
  }
  errorKey.value = null;
  emit("submit", {
    accountId: accountId.value,
    date: date.value,
    amount: effective.value,
    description: d,
    categoryId: categoryId.value,
    state: editing.value ? state.value : "Normal",
  });
}

const field =
  "rounded border border-gray-600 bg-gray-800 px-2 py-1 focus-visible:outline focus-visible:outline-2 focus-visible:outline-blue-400";
</script>

<template>
  <form data-test="tx-form" class="mt-3 grid gap-3 rounded border border-gray-700 p-3" novalidate @submit.prevent="onSubmit">
    <h2 class="font-medium">{{ editing ? t("real.transactions.editTitle") : t("real.transactions.newTitle") }}</h2>
    <div>
      <template v-if="editing">
        <span id="tx-account-label" class="mb-1 block text-sm">{{ t("real.transactions.account") }}</span>
        <p aria-labelledby="tx-account-label" data-test="tx-account-fixed">{{ accountName }}</p>
      </template>
      <template v-else>
        <label for="tx-account" class="mb-1 block text-sm">{{ t("real.transactions.account") }}</label>
        <select id="tx-account" v-model="accountId" :class="field">
          <option v-for="a in accounts" :key="a.id" :value="a.id">{{ a.name }}</option>
        </select>
      </template>
      <p v-if="isCard" class="mt-1 text-xs text-gray-400" data-test="card-hint">{{ t("real.transactions.cardHint") }}</p>
    </div>
    <div>
      <label for="tx-date" class="mb-1 block text-sm">{{ t("real.transactions.date") }}</label>
      <input id="tx-date" v-model="date" type="date" :class="field" />
    </div>
    <fieldset>
      <legend class="mb-1 text-sm">{{ t("real.transactions.kind") }}</legend>
      <label class="mr-4"><input v-model="kind" type="radio" name="tx-kind" value="expense" data-test="kind-expense" /> {{ t("real.transactions.expense") }}</label>
      <label><input v-model="kind" type="radio" name="tx-kind" value="income" data-test="kind-income" /> {{ t("real.transactions.income") }}</label>
    </fieldset>
    <div>
      <label for="tx-amount" class="mb-1 block text-sm">{{ t("real.transactions.amount") }}</label>
      <input
        id="tx-amount"
        v-model="amountText"
        inputmode="decimal"
        autocomplete="off"
        aria-describedby="tx-amount-hint"
        :aria-invalid="errorKey === 'real.errors.invalid_amount'"
        :class="field"
      />
      <p id="tx-amount-hint" class="mt-1 text-xs text-gray-400">{{ t("real.transactions.amountHint") }}</p>
    </div>
    <div>
      <label for="tx-description" class="mb-1 block text-sm">{{ t("real.transactions.description") }}</label>
      <input id="tx-description" v-model="description" :maxlength="DESCRIPTION_MAX" autocomplete="off" :class="field" />
    </div>
    <div>
      <label for="tx-category" class="mb-1 block text-sm">{{ t("real.transactions.category") }}</label>
      <CategorySelect id="tx-category" v-model="categoryId" :amount="effective ?? ''" />
    </div>
    <div v-if="editing">
      <label for="tx-state" class="mb-1 block text-sm">{{ t("real.transactions.state") }}</label>
      <select id="tx-state" v-model="state" :class="field">
        <option value="Normal">{{ t("real.transactions.states.Normal") }}</option>
        <option value="PendingClassification">{{ t("real.transactions.states.PendingClassification") }}</option>
      </select>
    </div>
    <p v-if="errorKey" class="text-sm text-red-400" role="alert" data-test="form-error">{{ t(errorKey) }}</p>
    <div class="flex gap-2">
      <button type="submit" :disabled="busy" class="rounded bg-blue-600 px-3 py-1 text-white disabled:opacity-50 focus-visible:outline focus-visible:outline-2 focus-visible:outline-blue-300">
        {{ t("real.transactions.save") }}
      </button>
      <button type="button" :disabled="busy" class="rounded border border-gray-600 px-3 py-1 disabled:opacity-50 focus-visible:outline focus-visible:outline-2 focus-visible:outline-blue-300" @click="emit('cancel')">
        {{ t("real.transactions.cancel") }}
      </button>
    </div>
  </form>
</template>
