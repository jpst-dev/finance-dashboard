<script setup lang="ts">
import { computed, nextTick, ref } from "vue";
import { useI18n } from "vue-i18n";
import type { ImportLine, ImportWarning, TransactionState } from "../api/types";
import Money from "../money/Money.vue";
import { lineId, useImportsStore } from "../stores/imports";
import CategorySelect from "./CategorySelect.vue";

const { t, te, locale } = useI18n();
const store = useImportsStore();

type TabKey = "new" | "pending" | "existing" | "warnings";
const TAB_KEYS: TabKey[] = ["new", "pending", "existing", "warnings"];
const active = ref<TabKey>("new");
const tabEls = ref<HTMLElement[]>([]);
const ack = ref(false);

const done = computed(() => store.alreadyImported);
const counts = computed<Record<TabKey, number>>(() =>
  // Arquivo já importado: contagens do servidor (nada novo), não as calculadas a partir das linhas.
  done.value
    ? { new: store.preview?.newCount ?? 0, pending: 0, existing: store.preview?.existingCount ?? 0, warnings: store.warnings.length }
    : {
        new: store.newLines.length,
        pending: store.pendingLines.length,
        existing: store.existingLines.length,
        warnings: store.warnings.length,
      },
);

function select(k: TabKey, focus = false) {
  active.value = k;
  if (focus) void nextTick(() => tabEls.value[TAB_KEYS.indexOf(k)]?.focus());
}

function onKey(e: KeyboardEvent, i: number) {
  const n = TAB_KEYS.length;
  let j: number | null = null;
  if (e.key === "ArrowRight") j = (i + 1) % n;
  else if (e.key === "ArrowLeft") j = (i - 1 + n) % n;
  else if (e.key === "Home") j = 0;
  else if (e.key === "End") j = n - 1;
  if (j === null) return;
  e.preventDefault();
  select(TAB_KEYS[j], true);
}

function fmtDate(d: string | null | undefined): string {
  return d && /^\d{4}-\d{2}-\d{2}$/.test(d) ? `${d.slice(8, 10)}/${d.slice(5, 7)}/${d.slice(0, 4)}` : "—";
}

const expiresText = computed(() => {
  const e = store.preview?.expiresAt;
  if (!e) return null;
  const d = new Date(e);
  if (Number.isNaN(d.getTime())) return null;
  return new Intl.DateTimeFormat(String(locale.value), { dateStyle: "short", timeStyle: "short" }).format(d);
});

function warningText(w: ImportWarning): string {
  // Texto do servidor (inglês) nunca é exibido: traduzimos pelo código.
  const key = `real.import.warnings.${w.code}`;
  return te(key) ? t(key) : t("real.import.warnings.unknown");
}

function stateOf(l: ImportLine): TransactionState {
  return store.overrides.get(lineId(l))?.state ?? l.state;
}
function categoryOf(l: ImportLine): string | null {
  return store.overrides.get(lineId(l))?.categoryId ?? null;
}
function onState(l: ImportLine, e: Event) {
  store.setOverride(l, { state: (e.target as HTMLSelectElement).value as TransactionState });
}
function onCategory(l: ImportLine, v: string | null) {
  store.setOverride(l, { categoryId: v });
}

const newCount = computed(() => store.preview?.newCount ?? 0);
const canConfirm = computed(
  () => !done.value && !store.busy && !store.expired && (!store.ackRequired || ack.value),
);

async function confirm() {
  if (!canConfirm.value) return;
  await store.confirm(store.ackRequired && ack.value);
}

const field =
  "rounded border border-gray-600 bg-gray-800 px-2 py-1 focus-visible:outline focus-visible:outline-2 focus-visible:outline-blue-400";
const focusRing = "focus-visible:outline focus-visible:outline-2 focus-visible:outline-blue-400";
</script>

<template>
  <section v-if="store.preview" class="mt-4" aria-labelledby="import-preview-title">
    <h2 id="import-preview-title" class="text-lg font-semibold">{{ t("real.import.previewTitle") }}</h2>
    <p class="text-sm text-gray-400">
      <template v-if="store.preview.periodStart || store.preview.periodEnd">
        {{ t("real.import.period", { start: fmtDate(store.preview.periodStart), end: fmtDate(store.preview.periodEnd) }) }}
      </template>
      <template v-else>{{ t("real.import.noPeriod") }}</template>
    </p>
    <p v-if="store.preview.checkpoint" class="text-sm text-gray-400" data-test="checkpoint">
      {{ t("real.import.checkpoint", { date: fmtDate(store.preview.checkpoint.date) }) }}
      <Money :value="store.preview.checkpoint.amount" signed />
    </p>
    <p v-if="expiresText" class="text-xs text-gray-400" data-test="expires">{{ t("real.import.expires", { when: expiresText }) }}</p>

    <div role="tablist" :aria-label="t('real.import.tabsLabel')" class="mt-3 flex flex-wrap gap-1 border-b border-gray-700">
      <button
        v-for="(k, i) in TAB_KEYS"
        :id="`imp-tab-${k}`"
        :key="k"
        :ref="(el) => { if (el) tabEls[i] = el as HTMLElement; }"
        type="button"
        role="tab"
        :aria-selected="active === k ? 'true' : 'false'"
        :aria-controls="`imp-panel-${k}`"
        :tabindex="active === k ? 0 : -1"
        class="rounded-t px-3 py-1.5"
        :class="[active === k ? 'bg-gray-700 font-semibold' : 'text-gray-300', focusRing]"
        @click="select(k)"
        @keydown="onKey($event, i)"
      >
        <span>{{ t(`real.import.tabs.${k}`) }}</span>
        <span class="ml-1 rounded-full bg-gray-600 px-1.5 text-xs">{{ counts[k] }}</span>
      </button>
    </div>

    <div id="imp-panel-new" role="tabpanel" aria-labelledby="imp-tab-new" tabindex="0" :hidden="active !== 'new'" class="py-3" data-test="new-lines">
      <p v-if="done" class="text-sm text-amber-300" data-test="already-imported">{{ t("real.import.alreadyImportedHelp") }}</p>
      <p v-else class="text-sm text-gray-400">{{ t("real.import.newHelp") }}</p>
      <p v-if="store.newLines.length === 0" class="mt-2 text-gray-400">{{ t("real.import.noLines") }}</p>
      <ul v-else class="mt-2 divide-y divide-gray-800" :class="{ 'opacity-60': done }">
        <li v-for="(l, idx) in store.newLines" :key="`${l.sourceKey}-${idx}`" class="flex flex-wrap justify-between gap-2 py-1.5">
          <span><span class="text-xs text-gray-400">{{ fmtDate(l.date) }}</span> {{ l.description }}</span>
          <Money :value="l.amount" signed />
        </li>
      </ul>
    </div>

    <div id="imp-panel-pending" role="tabpanel" aria-labelledby="imp-tab-pending" tabindex="0" :hidden="active !== 'pending'" class="py-3">
      <p v-if="done" class="text-sm text-amber-300">{{ t("real.import.alreadyImportedHelp") }}</p>
      <p v-else class="text-sm text-gray-300">{{ t("real.import.pendingHelp") }}</p>
      <p v-if="store.pendingLines.length === 0" class="mt-2 text-gray-400">{{ t("real.import.noLines") }}</p>
      <ul v-else class="mt-2 space-y-2" :class="{ 'opacity-60': done }">
        <li v-for="(l, idx) in store.pendingLines" :key="`${l.sourceKey}-${idx}`" class="rounded border border-gray-700 p-2" data-test="pending-line">
          <div class="flex flex-wrap justify-between gap-2">
            <span><span class="text-xs text-gray-400">{{ fmtDate(l.date) }}</span> {{ l.description }}</span>
            <Money :value="l.amount" signed />
          </div>
          <div v-if="!done" class="mt-2 flex flex-wrap items-end gap-3">
            <div>
              <label :for="`imp-st-${idx}`" class="mb-1 block text-sm">{{ t("real.import.state") }}</label>
              <select :id="`imp-st-${idx}`" :class="field" :value="stateOf(l)" data-test="line-state" @change="onState(l, $event)">
                <option value="PendingClassification">{{ t("real.import.keepPending") }}</option>
                <option value="Normal">{{ t("real.import.normal") }}</option>
              </select>
            </div>
            <div>
              <label :for="`imp-cat-${idx}`" class="mb-1 block text-sm">{{ t("real.categories.label") }}</label>
              <CategorySelect
                :id="`imp-cat-${idx}`"
                :model-value="categoryOf(l)"
                :amount="l.amount"
                data-test="line-category"
                @update:model-value="onCategory(l, $event)"
              />
            </div>
          </div>
        </li>
      </ul>
    </div>

    <div id="imp-panel-existing" role="tabpanel" aria-labelledby="imp-tab-existing" tabindex="0" :hidden="active !== 'existing'" class="py-3">
      <p class="text-sm text-gray-400">{{ t("real.import.existingHelp") }}</p>
      <p v-if="store.existingLines.length === 0" class="mt-2 text-gray-400">{{ t("real.import.noLines") }}</p>
      <ul v-else class="mt-2 divide-y divide-gray-800 opacity-60">
        <li v-for="(l, idx) in store.existingLines" :key="`${l.sourceKey}-${idx}`" class="flex flex-wrap justify-between gap-2 py-1.5">
          <span><span class="text-xs text-gray-400">{{ fmtDate(l.date) }}</span> {{ l.description }}</span>
          <Money :value="l.amount" signed />
        </li>
      </ul>
    </div>

    <div id="imp-panel-warnings" role="tabpanel" aria-labelledby="imp-tab-warnings" tabindex="0" :hidden="active !== 'warnings'" class="py-3">
      <p class="text-sm text-gray-400">{{ t("real.import.warningsHelp") }}</p>
      <p v-if="store.warnings.length === 0" class="mt-2 text-gray-400">{{ t("real.import.noWarnings") }}</p>
      <ul v-else class="mt-2 space-y-1">
        <li v-for="(w, idx) in store.warnings" :key="`${w.code}-${idx}`" data-test="warning" :class="w.severity === 'blocking' ? 'text-amber-300' : 'text-gray-200'">
          <strong>{{ w.severity === "blocking" ? t("real.import.blocking") : t("real.import.notice") }}:</strong>
          {{ warningText(w) }}
        </li>
      </ul>
    </div>

    <div class="mt-3 flex flex-wrap items-center gap-3">
      <label v-if="store.ackRequired && !done" class="flex items-center gap-2 text-sm text-amber-300">
        <input v-model="ack" type="checkbox" :class="focusRing" data-test="ack" />
        {{ t("real.import.ack") }}
      </label>
      <button
        type="button"
        class="rounded bg-blue-700 px-3 py-1.5 text-white disabled:opacity-50"
        :class="focusRing"
        :disabled="!canConfirm"
        data-test="confirm"
        @click="confirm"
      >
        {{ store.busy ? t("real.import.confirming") : t("real.import.confirm", { n: newCount }, newCount) }}
      </button>
      <button type="button" class="rounded border border-gray-600 px-3 py-1.5" :class="focusRing" :disabled="store.busy" data-test="cancel" @click="store.cancel()">
        {{ t("real.import.cancel") }}
      </button>
    </div>
  </section>
</template>
