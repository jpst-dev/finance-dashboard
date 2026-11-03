<script setup lang="ts">
import { computed, onMounted, ref } from "vue";
import { useI18n } from "vue-i18n";
import type { CategoryRule } from "../api/types";
import CategorySelect from "../components/CategorySelect.vue";
import { useCategoriesStore } from "../stores/categories";
import { PATTERN_MAX } from "../rules/suggestPattern";
import { useCategoryRulesStore } from "../stores/categoryRules";

const { t } = useI18n();
const store = useCategoryRulesStore();
const categories = useCategoriesStore();

const pattern = ref("");
const categoryId = ref<string | null>(null);
const fromSuggestion = ref(false);
const localError = ref<string | null>(null);
const confirming = ref<CategoryRule | null>(null);
const status = ref("");

onMounted(() => {
  // A sugestão vem da store (atalho em Lançamentos); é consumida uma única vez.
  const s = store.suggestion;
  if (s) {
    pattern.value = s.pattern;
    categoryId.value = s.categoryId;
    fromSuggestion.value = true;
    store.suggestion = null;
  }
  store.lastApplied = null;
  void categories.load();
  void store.load();
});

const errorText = computed(() => {
  const e = localError.value ?? store.error ?? categories.error;
  return e ? t(`real.errors.${e}`) : "";
});
const appliedText = computed(() =>
  store.lastApplied === null ? "" : t("real.plural.categorized", { n: store.lastApplied }, store.lastApplied),
);

async function onSubmit() {
  status.value = "";
  localError.value = null;
  const p = pattern.value.trim();
  if (!p || !categoryId.value) {
    localError.value = "invalid";
    return;
  }
  if (await store.create(p, categoryId.value)) {
    pattern.value = "";
    categoryId.value = null;
    fromSuggestion.value = false;
    status.value = t("real.rules.created");
  }
}
async function onDelete() {
  const r = confirming.value;
  if (!r) return;
  confirming.value = null;
  status.value = "";
  localError.value = null;
  if (await store.remove(r.id)) status.value = t("real.rules.deleted");
}
async function onApply() {
  status.value = "";
  localError.value = null;
  await store.apply();
}
const field =
  "rounded border border-gray-600 bg-gray-800 px-2 py-1 focus-visible:outline focus-visible:outline-2 focus-visible:outline-blue-400";
const btn =
  "rounded border border-gray-600 px-3 py-1 text-sm focus-visible:outline focus-visible:outline-2 focus-visible:outline-blue-300 disabled:opacity-50";
</script>

<template>
  <section>
    <h1 class="text-xl font-semibold">{{ t("real.nav.rules") }}</h1>
    <p class="mt-1 text-sm text-gray-400">{{ t("real.rules.help") }}</p>
    <p class="sr-only" role="status" aria-live="polite" data-test="status">{{ status }}</p>
    <p v-if="errorText" class="mt-2 text-sm text-red-400" role="alert" data-test="error">{{ errorText }}</p>

    <div class="mt-3 flex flex-wrap items-center gap-3">
      <button type="button" :class="btn" :disabled="store.busy" data-test="apply" @click="onApply">
        {{ t("real.rules.apply") }}
      </button>
      <p class="text-sm text-gray-300" role="status" aria-live="polite" data-test="apply-result">{{ appliedText }}</p>
    </div>

    <form class="mt-4 flex flex-wrap items-end gap-3" data-test="rule-form" @submit.prevent="onSubmit">
      <div>
        <label for="rule-pattern" class="mb-1 block text-sm">{{ t("real.rules.pattern") }}</label>
        <input id="rule-pattern" v-model="pattern" type="text" :maxlength="PATTERN_MAX" autocomplete="off" :class="field" data-test="pattern" />
      </div>
      <div>
        <label for="rule-category" class="mb-1 block text-sm">{{ t("real.rules.category") }}</label>
        <CategorySelect id="rule-category" v-model="categoryId" amount="" :label="t('real.rules.category')" />
      </div>
      <button type="submit" :class="btn" :disabled="store.busy" data-test="add">{{ t("real.rules.add") }}</button>
    </form>
    <p v-if="fromSuggestion" class="mt-1 text-xs text-gray-400" data-test="from-suggestion">{{ t("real.rules.fromSuggestion") }}</p>

    <div v-if="confirming" class="mt-3 rounded border border-amber-700 p-3" role="alertdialog" :aria-label="t('real.rules.delete')" data-test="confirm-delete">
      <p>{{ t("real.rules.deleteConfirm", { pattern: confirming.pattern }) }}</p>
      <div class="mt-2 flex gap-2">
        <button type="button" :class="btn" :disabled="store.busy" data-test="confirm-yes" @click="onDelete">{{ t("real.rules.deleteYes") }}</button>
        <button type="button" :class="btn" :disabled="store.busy" data-test="confirm-no" @click="confirming = null">{{ t("real.rules.deleteNo") }}</button>
      </div>
    </div>

    <p v-if="store.loading && store.rules.length === 0" class="mt-4 text-gray-400">{{ t("real.rules.loading") }}</p>
    <p v-else-if="!store.loading && store.rules.length === 0 && !store.error" class="mt-4 text-gray-400" data-test="empty">
      {{ t("real.rules.empty") }}
    </p>
    <template v-if="store.rules.length > 0">
      <table class="mt-4 w-full text-left text-sm" data-test="rules-table">
        <thead>
          <tr class="border-b border-gray-700 text-gray-400">
            <th scope="col" class="py-1 pr-3">{{ t("real.rules.tablePattern") }}</th>
            <th scope="col" class="py-1 pr-3">{{ t("real.rules.tableCategory") }}</th>
            <th scope="col" class="py-1 pr-3">{{ t("real.rules.tableMatches") }}</th>
            <th scope="col" class="py-1"><span class="sr-only">{{ t("real.rules.tableActions") }}</span></th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="r in store.rules" :key="r.id" class="border-b border-gray-800" data-test="rule-row">
            <td class="py-1 pr-3 break-all" data-test="rule-pattern">{{ r.pattern }}</td>
            <td class="py-1 pr-3" data-test="rule-category">{{ r.categoryName }}</td>
            <td class="py-1 pr-3" data-test="rule-matches">{{ r.matches }}</td>
            <td class="py-1">
              <button
                type="button"
                :class="btn"
                :disabled="store.busy"
                data-test="rule-delete"
                :aria-label="t('real.rules.deleteRow', { pattern: r.pattern })"
                @click="confirming = r"
              >
                {{ t("real.rules.delete") }}
              </button>
            </td>
          </tr>
        </tbody>
      </table>
      <p class="mt-2 text-xs text-gray-400" data-test="matches-hint">{{ t("real.rules.matchesHint") }}</p>
    </template>
  </section>
</template>
