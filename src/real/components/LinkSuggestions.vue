<script setup lang="ts">
import { useI18n } from "vue-i18n";
import Money from "../money/Money.vue";
import { useAccountsStore } from "../stores/accounts";
import type { LinkLine, LinkResponse } from "../api/types";
import { useLinksStore } from "../stores/links";

const { t } = useI18n();
const links = useLinksStore();
const accounts = useAccountsStore();

const accountName = (id: string) => accounts.items.find((a) => a.id === id)?.name ?? "—";
/** "YYYY-MM-DD" -> "dd/mm/yyyy" por TEXTO (sem Date: sem deslocamento de fuso). */
function fmtDate(d: string): string {
  return /^\d{4}-\d{2}-\d{2}$/.test(d) ? `${d.slice(8, 10)}/${d.slice(5, 7)}/${d.slice(0, 4)}` : d;
}
/** Linhas presentes do vínculo (lado vazio = vínculo "externo", com uma linha só). */
function sidesOf(l: LinkResponse): { key: "out" | "in"; line: LinkLine }[] {
  const r: { key: "out" | "in"; line: LinkLine }[] = [];
  if (l.out) r.push({ key: "out", line: l.out });
  if (l.in) r.push({ key: "in", line: l.in });
  return r;
}
const btn =
  "rounded border border-gray-600 px-2 py-1 text-sm focus-visible:outline focus-visible:outline-2 focus-visible:outline-blue-300 disabled:opacity-50";
</script>

<template>
  <section v-if="links.suggested.length > 0" class="mt-4" data-test="suggestions" :aria-label="t('real.links.suggestionsTitle')">
    <h2 class="font-semibold">{{ t("real.links.suggestionsTitle") }}</h2>
    <p class="text-sm text-gray-400">{{ t("real.links.suggestionsHelp") }}</p>
    <ul class="mt-2 space-y-3">
      <li v-for="l in links.suggested" :key="l.id" class="rounded border border-amber-700 p-3" data-test="suggestion">
        <p class="text-xs text-gray-400">{{ t(`real.links.kind.${l.kind}`) }}</p>
        <template v-for="side in sidesOf(l)" :key="side.key">
          <div class="flex flex-wrap items-baseline justify-between gap-2">
            <span>{{ t(`real.links.${side.key}`) }}: {{ side.line.description }}</span>
            <span><Money :value="side.line.amount" signed /></span>
          </div>
          <p class="text-xs text-gray-400">{{ fmtDate(side.line.date) }} · {{ accountName(side.line.accountId) }}</p>
        </template>
        <p v-if="l.external" class="text-xs text-amber-300" data-test="link-external">{{ t(`real.links.external.${l.kind}`) }}</p>
        <div class="mt-2 flex gap-2">
          <button type="button" :class="btn" :disabled="links.busy" data-test="link-confirm" @click="links.confirm(l.id)">{{ t("real.links.confirm") }}</button>
          <button type="button" :class="btn" :disabled="links.busy" data-test="link-reject" @click="links.reject(l.id)">{{ t("real.links.reject") }}</button>
        </div>
      </li>
    </ul>
  </section>
</template>
