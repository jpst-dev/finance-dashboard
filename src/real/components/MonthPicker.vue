<script setup lang="ts">
import { computed } from "vue";
import { useI18n } from "vue-i18n";
import { monthLabel } from "../stores/summary";

const props = defineProps<{ month: string; canNext: boolean }>();
const emit = defineEmits<{ prev: []; next: [] }>();
const { t, locale } = useI18n();
const label = computed(() => monthLabel(props.month, String(locale.value)));
const btn =
  "rounded border border-gray-600 px-3 py-1 text-sm disabled:opacity-40 focus-visible:outline focus-visible:outline-2 focus-visible:outline-blue-300";
</script>

<template>
  <div class="flex items-center gap-3" role="group" :aria-label="t('real.overview.monthPicker')" data-test="month-picker">
    <button type="button" :class="btn" :aria-label="t('real.overview.prevMonth')" data-test="month-prev" @click="emit('prev')">
      &#9666;
    </button>
    <span class="min-w-40 text-center font-medium capitalize" aria-live="polite" data-test="month-label">{{ label }}</span>
    <button
      type="button"
      :class="btn"
      :disabled="!canNext"
      :aria-label="t('real.overview.nextMonth')"
      data-test="month-next"
      @click="emit('next')"
    >
      &#9656;
    </button>
  </div>
</template>
