<script setup lang="ts">
import { computed, ref } from "vue";
import { useI18n } from "vue-i18n";
import { useImportsStore } from "../stores/imports";

const { t } = useI18n();
const store = useImportsStore();

const accountId = ref(store.lastAccountId ?? "");
const files = ref<File[]>([]);

function onFiles(e: Event) {
  const list = (e.target as HTMLInputElement).files;
  files.value = list ? Array.from(list) : [];
}

async function submit() {
  // A store valida extensão, tamanho, vazio, quantidade e conta antes de enviar qualquer coisa.
  await store.upload(accountId.value, files.value);
}

const accountType = computed(() => store.importableAccounts.find((a) => a.id === accountId.value)?.type);
const hintKey = computed(() =>
  accountType.value === "Checking" || accountType.value === "CreditCard"
    ? `real.import.fileHint.${accountType.value}`
    : "real.import.fileHint.none",
);

const field =
  "rounded border border-gray-600 bg-gray-800 px-2 py-1 focus-visible:outline focus-visible:outline-2 focus-visible:outline-blue-400";
</script>

<template>
  <form class="mt-4 space-y-3" data-test="upload-form" novalidate @submit.prevent="submit">
    <p v-if="store.importableAccounts.length === 0" class="text-sm text-amber-400" data-test="no-accounts">
      {{ t("real.import.noAccounts") }}
    </p>
    <div>
      <label for="import-account" class="mb-1 block text-sm">{{ t("real.import.account") }}</label>
      <select id="import-account" v-model="accountId" :class="field" aria-describedby="import-account-hint" data-test="account">
        <option value="" disabled>{{ t("real.import.chooseAccount") }}</option>
        <option v-for="a in store.importableAccounts" :key="a.id" :value="a.id">
          {{ a.name }} · {{ t(`real.accounts.types.${a.type}`) }}
        </option>
      </select>
      <p id="import-account-hint" class="mt-1 text-xs text-gray-400">{{ t("real.import.accountHint") }}</p>
    </div>
    <div>
      <label for="import-file" class="mb-1 block text-sm">{{ t("real.import.file") }}</label>
      <input
        id="import-file"
        type="file"
        accept=".ofx,.xlsx"
        aria-describedby="import-file-hint"
        :class="field"
        data-test="file"
        @change="onFiles"
      />
      <p id="import-file-hint" class="mt-1 text-xs text-gray-400" data-test="file-hint">{{ t(hintKey) }}</p>
    </div>
    <button
      type="submit"
      class="rounded bg-blue-700 px-3 py-1.5 text-white disabled:opacity-50 focus-visible:outline focus-visible:outline-2 focus-visible:outline-blue-300"
      :disabled="store.busy"
      data-test="upload"
    >
      {{ store.busy ? t("real.import.uploading") : t("real.import.upload") }}
    </button>
  </form>
</template>
