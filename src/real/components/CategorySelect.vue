<script setup lang="ts">
import { computed } from "vue";
import { useI18n } from "vue-i18n";
import { useCategoriesStore } from "../stores/categories";

const props = defineProps<{ modelValue: string | null; amount: string; label?: string; id?: string }>();
const emit = defineEmits<{ "update:modelValue": [value: string | null] }>();
const { t } = useI18n();
const store = useCategoriesStore();

const options = computed(() => {
  const list = store.forAmount(props.amount).map((c) => ({ id: c.id, text: c.name }));
  // Decisão: se a categoria atual deixou de combinar com o sinal do valor, ela NÃO é descartada
  // em silêncio; continua selecionável, marcada como incompatível, e quem edita decide.
  const cur = props.modelValue;
  if (cur && !list.some((o) => o.id === cur)) {
    const found = store.items.find((c) => c.id === cur);
    if (found) list.push({ id: found.id, text: `${found.name} (${t("real.categories.mismatch")})` });
  }
  return list;
});

function onChange(e: Event) {
  const v = (e.target as HTMLSelectElement).value;
  emit("update:modelValue", v === "" ? null : v);
}
</script>

<template>
  <select
    :id="id"
    :aria-label="label ?? t('real.categories.label')"
    :value="modelValue ?? ''"
    class="rounded border border-gray-600 bg-gray-800 px-2 py-1 focus-visible:outline focus-visible:outline-2 focus-visible:outline-blue-400"
    @change="onChange"
  >
    <option value="">{{ t("real.categories.none") }}</option>
    <option v-for="o in options" :key="o.id" :value="o.id">{{ o.text }}</option>
  </select>
</template>
