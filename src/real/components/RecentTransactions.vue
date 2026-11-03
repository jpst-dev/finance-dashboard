<script setup lang="ts">
import { useI18n } from "vue-i18n";
import Money from "../money/Money.vue";
import { useSummaryStore } from "../stores/summary";

const { t } = useI18n();
const store = useSummaryStore();
function fmtDate(d: string): string {
  return /^\d{4}-\d{2}-\d{2}$/.test(d) ? `${d.slice(8, 10)}/${d.slice(5, 7)}/${d.slice(0, 4)}` : d;
}
</script>

<template>
  <section aria-labelledby="recent-h" data-test="recent">
    <div class="mb-2 flex items-baseline justify-between">
      <h2 id="recent-h" class="text-lg font-semibold">{{ t("real.overview.recentTitle") }}</h2>
      <RouterLink to="/transactions" class="text-sm underline focus-visible:outline focus-visible:outline-2 focus-visible:outline-blue-300">{{
        t("real.overview.seeAll")
      }}</RouterLink>
    </div>
    <p v-if="store.recentError" class="text-sm text-red-300" role="alert" data-test="recent-error">
      {{ t(`real.errors.${store.recentError}`) }}
      <button type="button" class="ml-2 underline" @click="store.loadRecent()">{{ t("real.overview.retry") }}</button>
    </p>
    <p v-else-if="store.recentTotal === null" class="text-sm text-gray-400" data-test="recent-loading">{{ t("real.overview.loading") }}</p>
    <ul v-else class="divide-y divide-gray-800">
      <li v-for="x in store.recent" :key="x.id" class="flex items-center justify-between gap-3 py-2">
        <span
          ><span class="mr-2 text-xs text-gray-400">{{ fmtDate(x.date) }}</span
          >{{ x.description }}</span
        >
        <Money :value="x.amount" signed />
      </li>
    </ul>
  </section>
</template>
