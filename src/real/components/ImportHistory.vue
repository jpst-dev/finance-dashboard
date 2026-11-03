<script setup lang="ts">
import { computed, nextTick, ref } from "vue";
import { useI18n } from "vue-i18n";
import type { ImportBatch } from "../api/types";
import { useAccountsStore } from "../stores/accounts";
import { useImportsStore } from "../stores/imports";

const { t, locale } = useI18n();
const store = useImportsStore();
const accounts = useAccountsStore();

/** Lote em confirmação de desfazer; `edited`: undefined = carregando, null = desconhecido. */
const pending = ref<{ id: string; edited: number | null | undefined } | null>(null);
let trigger: HTMLElement | null = null;
const yesBtn = ref<HTMLButtonElement | null>(null);
const titleEl = ref<HTMLElement | null>(null);
const dialogEl = ref<HTMLElement | null>(null);
const status = ref("");

const accountName = (id: string) => accounts.items.find((a) => a.id === id)?.name ?? "—";
function fmtDate(d: string | null | undefined): string {
  return d && /^\d{4}-\d{2}-\d{2}$/.test(d) ? `${d.slice(8, 10)}/${d.slice(5, 7)}/${d.slice(0, 4)}` : "—";
}
function fmtStamp(s: string): string {
  const d = new Date(s);
  return Number.isNaN(d.getTime())
    ? "—"
    : new Intl.DateTimeFormat(String(locale.value), { dateStyle: "short", timeStyle: "short" }).format(d);
}

const errorText = computed(() => (store.historyError ? t(`real.errors.${store.historyError}`) : ""));

async function openUndo(b: ImportBatch, e: Event) {
  trigger = e.currentTarget as HTMLElement;
  status.value = "";
  pending.value = { id: b.batchId, edited: undefined };
  // O foco entra no diálogo já ao abrir: Escape funciona mesmo enquanto o editedCount carrega.
  await nextTick();
  dialogEl.value?.focus();
  await loadEdited(b.batchId);
}

async function loadEdited(id: string) {
  const n = await store.editedCount(id);
  if (pending.value?.id !== id) return;
  pending.value = { id, edited: n };
  // Com o aviso do editedCount na tela, o foco vai para a ação; em falha, para o diálogo (que tem "Tentar novamente").
  await nextTick();
  if (n === null) dialogEl.value?.focus();
  else yesBtn.value?.focus();
}

async function retryEdited() {
  const p = pending.value;
  if (!p) return;
  pending.value = { id: p.id, edited: undefined };
  await loadEdited(p.id);
}

async function close() {
  pending.value = null;
  await nextTick();
  if (trigger?.isConnected) trigger.focus();
  else titleEl.value?.focus();
  trigger = null;
}

async function doUndo() {
  const p = pending.value;
  if (!p || store.undoing) return;
  const ok = await store.undo(p.id);
  if (ok) status.value = t("real.import.history.undone");
  await close();
}
</script>

<template>
  <section class="mt-8" aria-labelledby="import-history-title">
    <h2 id="import-history-title" ref="titleEl" tabindex="-1" class="text-lg font-semibold">{{ t("real.import.history.title") }}</h2>
    <p class="sr-only" role="status" aria-live="polite">{{ status }}</p>
    <p v-if="errorText" class="mt-2 text-sm text-red-400" role="alert" data-test="history-error">{{ errorText }}</p>
    <p v-if="store.historyLoading && store.history.length === 0" class="mt-2 text-gray-400">{{ t("real.import.history.loading") }}</p>
    <p v-else-if="store.history.length === 0 && !store.historyError" class="mt-2 text-gray-400" data-test="history-empty">
      {{ t("real.import.history.empty") }}
    </p>
    <div v-else class="mt-2 overflow-x-auto">
      <table class="w-full text-left text-sm">
        <thead class="text-gray-400">
          <tr>
            <th scope="col" class="py-1 pr-3">{{ t("real.import.history.created") }}</th>
            <th scope="col" class="py-1 pr-3">{{ t("real.import.history.account") }}</th>
            <th scope="col" class="py-1 pr-3">{{ t("real.import.history.period") }}</th>
            <th scope="col" class="py-1 pr-3">{{ t("real.import.history.counts") }}</th>
            <th scope="col" class="py-1 pr-3">{{ t("real.import.history.state") }}</th>
            <th scope="col" class="py-1"><span class="sr-only">{{ t("real.transactions.actions") }}</span></th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="b in store.history" :key="b.batchId" class="border-t border-gray-800" data-test="history-row">
            <td class="py-1.5 pr-3">{{ fmtStamp(b.createdAt) }}</td>
            <td class="py-1.5 pr-3">{{ accountName(b.accountId) }}</td>
            <td class="py-1.5 pr-3">{{ fmtDate(b.periodStart) }} – {{ fmtDate(b.periodEnd) }}</td>
            <td class="py-1.5 pr-3">{{ t("real.import.history.countsValue", { new: b.newCount, existing: b.existingCount }) }}</td>
            <td class="py-1.5 pr-3">{{ t(`real.import.history.states.${b.state}`) }}</td>
            <td class="py-1.5">
              <button
                v-if="b.state === 'Confirmed'"
                type="button"
                class="rounded border border-red-700 px-2 py-0.5 text-red-300 disabled:opacity-50 focus-visible:outline focus-visible:outline-2 focus-visible:outline-blue-400"
                :disabled="store.undoing"
                :aria-expanded="pending?.id === b.batchId ? 'true' : 'false'"
                aria-controls="import-undo-dialog"
                data-test="undo"
                @click="openUndo(b, $event)"
              >
                {{ t("real.import.history.undo") }}
              </button>
            </td>
          </tr>
        </tbody>
      </table>
    </div>

    <div
      v-if="pending"
      id="import-undo-dialog"
      ref="dialogEl"
      tabindex="-1"
      role="alertdialog"
      aria-modal="false"
      aria-labelledby="import-undo-title"
      aria-describedby="import-undo-body"
      class="mt-3 rounded border border-red-700 bg-gray-900 p-3"
      @keydown.esc.prevent="close"
    >
      <h3 id="import-undo-title" class="font-semibold">{{ t("real.import.history.undoTitle") }}</h3>
      <div id="import-undo-body" class="mt-1 space-y-1 text-sm">
        <p>{{ t("real.import.history.undoBody") }}</p>
        <p v-if="pending.edited === undefined" class="text-gray-400">{{ t("real.import.history.loading") }}</p>
        <div v-else-if="pending.edited === null" class="flex flex-wrap items-center gap-2" data-test="edited-error">
          <p class="text-red-400">{{ t("real.import.history.editedFailed") }}</p>
          <button
            type="button"
            class="rounded border border-gray-600 px-3 py-1 focus-visible:outline focus-visible:outline-2 focus-visible:outline-blue-400"
            data-test="edited-retry"
            @click="retryEdited"
          >
            {{ t("real.import.history.editedRetry") }}
          </button>
        </div>
        <p v-else data-test="edited-count">{{ t("real.import.history.edited", { n: pending.edited }) }}</p>
        <p class="text-amber-300">{{ t("real.import.history.crossBatch") }}</p>
      </div>
      <div class="mt-2 flex gap-2">
        <button
          ref="yesBtn"
          type="button"
          class="rounded bg-red-700 px-3 py-1 text-white disabled:opacity-50 focus-visible:outline focus-visible:outline-2 focus-visible:outline-blue-300"
          :disabled="store.undoing || typeof pending.edited !== 'number'"
          data-test="undo-yes"
          @click="doUndo"
        >
          {{ t("real.import.history.undoYes") }}
        </button>
        <button
          type="button"
          class="rounded border border-gray-600 px-3 py-1 focus-visible:outline focus-visible:outline-2 focus-visible:outline-blue-400"
          data-test="undo-no"
          @click="close"
        >
          {{ t("real.import.history.undoNo") }}
        </button>
      </div>
    </div>
  </section>
</template>
