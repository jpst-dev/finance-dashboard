<script setup lang="ts">
import { computed } from "vue";
import i18n from "../i18n";
import { moneyParts } from "./format";
import { MASK, useValuesVisibility } from "./visibility";

const props = defineProps<{
  value: string;
  signed?: boolean;
  locale?: string;
  hiddenLabel?: string;
}>();

const visibility = useValuesVisibility();
const locale = computed(() => props.locale ?? String(i18n.global.locale.value));
const parts = computed(() => moneyParts(props.value, locale.value));

const label = computed(() => props.hiddenLabel ?? String(i18n.global.t("real.money.hidden")));

const text = computed(() => {
  const p = parts.value;
  if (!p) return props.value;
  // U+2212 nos dois modos (assinado ou não), para o sinal ficar igual em toda a interface.
  if (!props.signed) return (p.negative ? "−" : "") + p.text;
  // Sinal também em texto, não só em cor.
  return (p.zero ? "" : p.negative ? "−" : "+") + p.text;
});

const tone = computed(() => {
  const p = parts.value;
  if (!props.signed || !p) return undefined;
  return p.zero ? "money-zero" : p.negative ? "money-negative" : "money-positive";
});
</script>

<template>
  <span v-if="visibility.hidden" class="money" role="img" :aria-label="label">{{ MASK }}</span>
  <span v-else class="money" :class="tone">{{ text }}</span>
</template>

<style scoped>
.money {
  font-variant-numeric: tabular-nums;
  white-space: nowrap;
}
.money-positive {
  color: #047857;
}
.money-negative {
  color: #b91c1c;
}
.money-zero {
  color: inherit;
}
</style>
