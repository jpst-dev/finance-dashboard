<script setup lang="ts">
import { computed } from "vue";
import { useI18n } from "vue-i18n";

const props = defineProps<{ page: number; size: number; total: number }>();
const emit = defineEmits<{ "update:page": [page: number] }>();
const { t } = useI18n();

const pages = computed(() => Math.max(1, Math.ceil(props.total / Math.max(1, props.size))));
const btn =
  "rounded border border-gray-600 px-3 py-1 text-sm disabled:opacity-40 focus-visible:outline focus-visible:outline-2 focus-visible:outline-blue-300";
</script>

<template>
  <nav :aria-label="t('real.pagination.label')" class="mt-3 flex items-center gap-3" data-test="pagination">
    <button type="button" :class="btn" :disabled="page <= 1" :aria-label="t('real.pagination.prev')" data-test="prev" @click="emit('update:page', page - 1)">
      &lsaquo;
    </button>
    <span data-test="page-info" aria-live="polite">{{ t("real.pagination.info", { page, pages }) }}</span>
    <button type="button" :class="btn" :disabled="page >= pages" :aria-label="t('real.pagination.next')" data-test="next" @click="emit('update:page', page + 1)">
      &rsaquo;
    </button>
  </nav>
</template>
