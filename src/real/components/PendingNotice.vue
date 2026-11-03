<script setup lang="ts">
import { useI18n } from "vue-i18n";
import { useTransactionsStore } from "../stores/transactions";

const { t } = useI18n();
const tx = useTransactionsStore();
</script>

<template>
  <div
    v-if="tx.pendingCount"
    class="rounded border border-amber-600 bg-amber-950/40 px-3 py-2 text-sm"
    role="status"
    data-test="pending-notice"
  >
    {{ t("real.overview.pendingNotice", { count: tx.pendingCount }, tx.pendingCount) }}
    <RouterLink to="/pending" class="ml-2 underline focus-visible:outline focus-visible:outline-2 focus-visible:outline-blue-300">{{
      t("real.overview.pendingReview")
    }}</RouterLink>
  </div>
</template>
