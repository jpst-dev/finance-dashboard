<script setup lang="ts">
import { computed, onMounted, ref } from "vue";
import { useI18n } from "vue-i18n";
import ImportHistory from "../components/ImportHistory.vue";
import ImportPreview from "../components/ImportPreview.vue";
import ImportUpload from "../components/ImportUpload.vue";
import { useAccountsStore } from "../stores/accounts";
import { useCategoriesStore } from "../stores/categories";
import { useImportsStore } from "../stores/imports";

const { t } = useI18n();
const store = useImportsStore();
const accounts = useAccountsStore();
const categories = useCategoriesStore();

// Evita piscar "Nenhuma conta ativa" antes de as contas chegarem.
const accountsReady = ref(accounts.loaded);

onMounted(() => {
  store.result = null;
  store.error = null;
  store.notice = null;
  void accounts.load().finally(() => (accountsReady.value = true));
  void categories.load();
  void store.loadHistory();
});

// Só texto traduzido por código; nunca o texto do servidor, valores ou descrições.
const errorText = computed(() => (store.error ? t(`real.errors.${store.error}`) : ""));
const btn = "rounded px-3 py-1.5 focus-visible:outline focus-visible:outline-2 focus-visible:outline-blue-400";
</script>

<template>
  <section>
    <h1 class="text-xl font-semibold">{{ t("real.nav.import") }}</h1>
    <p class="mt-1 text-sm text-gray-400">{{ t("real.import.help") }}</p>

    <div v-if="errorText" class="mt-3 flex flex-wrap items-center gap-3" role="alert">
      <p class="text-sm text-red-400" data-test="error">{{ errorText }}</p>
      <button v-if="store.expired" type="button" :class="[btn, 'border border-gray-600']" data-test="resend" @click="store.backToUpload()">
        {{ t("real.import.resend") }}
      </button>
    </div>

    <p v-if="store.notice" class="mt-3 text-sm text-amber-300" role="status" aria-live="polite" data-test="notice">
      {{ t(`real.import.notices.${store.notice}`) }}
    </p>

    <div v-if="store.result" class="mt-4 rounded border border-emerald-700 p-3" role="status" aria-live="polite" data-test="result">
      <h2 class="font-semibold">{{ t("real.import.resultTitle") }}</h2>
      <p class="mt-1">{{ t("real.import.result", { created: store.result.created, skipped: store.result.skipped }) }}</p>
      <p class="mt-1" data-test="result-links">{{ t("real.import.resultLinks", {
          linked: t("real.plural.pairs", { n: store.result.linked }, store.result.linked),
          suggested: t("real.plural.suggestions", { n: store.result.suggested }, store.result.suggested),
        }) }}</p>
      <div class="mt-2 flex flex-wrap gap-3">
        <RouterLink to="/transactions" :class="[btn, 'border border-gray-600']">{{ t("real.import.goTransactions") }}</RouterLink>
        <RouterLink to="/pending" :class="[btn, 'border border-gray-600']">{{ t("real.import.goPending") }}</RouterLink>
        <button type="button" :class="[btn, 'border border-gray-600']" data-test="another" @click="store.reset()">
          {{ t("real.import.another") }}
        </button>
      </div>
    </div>
    <p v-else-if="!store.preview && !accountsReady" class="mt-4 text-gray-400" data-test="accounts-loading">{{ t("real.accounts.loading") }}</p>
    <ImportUpload v-else-if="!store.preview" />
    <ImportPreview v-else />

    <ImportHistory />
  </section>
</template>
