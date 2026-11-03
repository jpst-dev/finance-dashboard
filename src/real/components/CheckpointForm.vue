<script setup lang="ts">
import { ref } from "vue";
import { useI18n } from "vue-i18n";
import { parseMoneyInput } from "../money/input";
import { todayLocalISO } from "../money/today";

const props = defineProps<{ accountId: string; busy: boolean }>();
const emit = defineEmits<{ submit: [date: string, amount: string]; cancel: [] }>();
const { t } = useI18n();

const date = ref(todayLocalISO());
const amount = ref("");
const errorKey = ref<string | null>(null);
const field =
  "rounded border border-gray-600 bg-gray-800 px-2 py-1 focus-visible:outline focus-visible:outline-2 focus-visible:outline-blue-400";

function onSubmit() {
  const parsed = parseMoneyInput(amount.value.trim());
  if (parsed === null) {
    errorKey.value = "real.errors.invalid_amount";
    return;
  }
  if (!/^\d{4}-\d{2}-\d{2}$/.test(date.value)) {
    errorKey.value = "real.errors.invalid_date";
    return;
  }
  errorKey.value = null;
  emit("submit", date.value, parsed);
}
</script>

<template>
  <form data-test="checkpoint-form" class="mt-2 grid gap-2 rounded border border-gray-700 p-3" novalidate @submit.prevent="onSubmit">
    <p class="text-sm text-gray-400">{{ t("real.accounts.checkpointHelp") }}</p>
    <div>
      <label :for="`cp-date-${props.accountId}`" class="mb-1 block text-sm">{{ t("real.accounts.checkpointDate") }}</label>
      <input :id="`cp-date-${props.accountId}`" v-model="date" type="date" :class="field" />
    </div>
    <div>
      <label :for="`cp-amount-${props.accountId}`" class="mb-1 block text-sm">{{ t("real.accounts.checkpointAmount") }}</label>
      <input
        :id="`cp-amount-${props.accountId}`"
        v-model="amount"
        inputmode="decimal"
        autocomplete="off"
        :aria-invalid="errorKey !== null"
        :class="field"
      />
    </div>
    <p v-if="errorKey" class="text-sm text-red-400" role="alert">{{ t(errorKey) }}</p>
    <div class="flex gap-2">
      <button
        type="submit"
        :disabled="busy"
        class="rounded bg-blue-600 px-3 py-1 text-white disabled:opacity-50 focus-visible:outline focus-visible:outline-2 focus-visible:outline-blue-300"
      >
        {{ t("real.accounts.checkpointSave") }}
      </button>
      <button
        type="button"
        :disabled="busy"
        class="rounded border border-gray-600 px-3 py-1 disabled:opacity-50 focus-visible:outline focus-visible:outline-2 focus-visible:outline-blue-300"
        @click="emit('cancel')"
      >
        {{ t("real.accounts.cancel") }}
      </button>
    </div>
  </form>
</template>
