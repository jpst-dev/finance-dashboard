<script setup lang="ts">
import { useI18n } from "vue-i18n";
import type { Summary } from "../api/types";
import Money from "../money/Money.vue";

defineProps<{ summary: Summary }>();
const { t } = useI18n();
</script>

<template>
  <section aria-labelledby="cards-h" data-test="summary-cards">
    <h2 id="cards-h" class="sr-only">{{ t("real.overview.monthSummary") }}</h2>
    <dl class="grid gap-3 sm:grid-cols-3">
      <div class="rounded border border-gray-700 p-3">
        <dt class="text-sm text-gray-400">{{ t("real.overview.income") }}</dt>
        <dd class="text-xl" data-test="card-income"><Money :value="summary.income" /></dd>
      </div>
      <div class="rounded border border-gray-700 p-3">
        <dt class="text-sm text-gray-400">{{ t("real.overview.expenses") }}</dt>
        <dd class="text-xl" data-test="card-expenses"><Money :value="summary.expenses" /></dd>
      </div>
      <div class="rounded border border-gray-700 p-3">
        <dt class="text-sm text-gray-400">{{ t("real.overview.net") }}</dt>
        <dd class="text-xl" data-test="card-net"><Money :value="summary.net" signed /></dd>
      </div>
    </dl>
    <p v-if="summary.pending.count > 0" class="mt-2 text-sm text-gray-300" data-test="month-pending">
      {{ t("real.overview.pendingOutside", { count: summary.pending.count }, summary.pending.count) }}
      (<Money :value="summary.pending.total" />)
    </p>
  </section>
</template>
