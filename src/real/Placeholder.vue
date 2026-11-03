<script setup lang="ts">
import { useI18n } from "vue-i18n";
const { t } = useI18n();
</script>

<template>
  <main class="p-6">
    <h1 class="text-xl font-semibold">{{ t("real.app.title") }}</h1>
    <p>{{ t("real.app.placeholder") }}</p>
  </main>
</template>
