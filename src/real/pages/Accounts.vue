<script setup lang="ts">
import { useI18n } from "vue-i18n";
const { t } = useI18n();
</script>

<template>
  <section>
    <h1 class="text-xl font-semibold">{{ t("real.nav.accounts") }}</h1>
    <p class="mt-2 text-sm text-gray-400">{{ t("real.app.placeholder") }}</p>
  </section>
</template>
