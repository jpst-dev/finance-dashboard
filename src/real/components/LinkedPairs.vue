<script setup lang="ts">
import { useI18n } from "vue-i18n";
import Money from "../money/Money.vue";
import { useAccountsStore } from "../stores/accounts";
import type { LinkResponse } from "../api/types";
import { useLinksStore } from "../stores/links";

const { t } = useI18n();
const links = useLinksStore();
const accounts = useAccountsStore();

const accountName = (id: string) => accounts.items.find((a) => a.id === id)?.name ?? "—";
function fmtDate(d: string): string {
  return /^\d{4}-\d{2}-\d{2}$/.test(d) ? `${d.slice(8, 10)}/${d.slice(5, 7)}/${d.slice(0, 4)}` : d;
}
/** Valor mostrado: o da entrada em pares; a única linha existente em vínculos de lado vazio. */
const amountOf = (l: LinkResponse) => (l.in ?? l.out)?.amount;
const btn =
  "rounded border border-gray-600 px-2 py-1 text-sm focus-visible:outline focus-visible:outline-2 focus-visible:outline-blue-300 disabled:opacity-50";
</script>

<template>
  <section v-if="links.confirmed.length > 0" class="mt-4" data-test="linked-pairs" :aria-label="t('real.links.linkedTitle')">
    <h2 class="font-semibold">{{ t("real.links.linkedTitle") }}</h2>
    <ul class="mt-2 space-y-2">
      <li v-for="l in links.confirmed" :key="l.id" class="rounded border border-gray-700 p-3" data-test="linked">
        <p class="text-xs text-gray-400">{{ t(`real.links.kind.${l.kind}`) }}</p>
        <div class="flex flex-wrap items-baseline justify-between gap-2">
          <span>{{ l.out && l.in ? `${l.out.description} → ${l.in.description}` : (l.out ?? l.in)?.description }}</span>
          <span v-if="amountOf(l)"><Money :value="amountOf(l)!" signed /></span>
        </div>
        <p class="text-xs text-gray-400">
          <template v-if="l.out && l.in">
            {{ fmtDate(l.out.date) }} · {{ accountName(l.out.accountId) }} → {{ fmtDate(l.in.date) }} · {{ accountName(l.in.accountId) }}
          </template>
          <template v-else-if="l.out ?? l.in">{{ fmtDate((l.out ?? l.in)!.date) }} · {{ accountName((l.out ?? l.in)!.accountId) }}</template>
        </p>
        <p v-if="l.external" class="text-xs text-amber-300" data-test="link-external">{{ t(`real.links.external.${l.kind}`) }}</p>
        <button type="button" :class="[btn, 'mt-2']" :disabled="links.busy" data-test="link-undo" @click="links.undo(l.id)">{{ t("real.links.undo") }}</button>
      </li>
    </ul>
  </section>
</template>
