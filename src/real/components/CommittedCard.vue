<script setup lang="ts">
import { computed } from "vue";
import { useI18n } from "vue-i18n";
import Money from "../money/Money.vue";
import { todayISO } from "../money/today";
import { nextMonthsTotal, useInstallmentsStore } from "../stores/installments";

const { t } = useI18n();
const store = useInstallmentsStore();

// Total fora do MonthPicker: sempre o mês corrente + 2 seguintes. Zero (ou sem dados/erro) esconde o cartão.
const total = computed(() => nextMonthsTotal(store.data, todayISO()));
const visible = computed(() => /[1-9]/.test(total.value));
</script>

<template>
  <section
    v-if="visible"
    class="rounded border border-gray-700 p-4"
    aria-labelledby="committed-h"
    data-test="committed-card"
  >
    <h2 id="committed-h" class="text-sm text-gray-400">{{ t("real.overview.committedTitle") }}</h2>
    <p class="mt-1 text-2xl font-semibold" data-test="committed-total"><Money :value="total" /></p>
    <p class="mt-1 text-xs text-gray-400">{{ t("real.overview.committedHint") }}</p>
    <RouterLink
      to="/installments"
      class="mt-2 inline-block text-sm underline focus-visible:outline focus-visible:outline-2 focus-visible:outline-blue-300"
      >{{ t("real.overview.committedLink") }}</RouterLink
    >
  </section>
</template>
