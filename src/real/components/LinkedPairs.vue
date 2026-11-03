<script setup lang="ts">
import { useI18n } from "vue-i18n";
import Money from "../money/Money.vue";
import { useAccountsStore } from "../stores/accounts";
import { useLinksStore } from "../stores/links";

const { t } = useI18n();
const links = useLinksStore();
const accounts = useAccountsStore();

const accountName = (id: string) => accounts.items.find((a) => a.id === id)?.name ?? "—";
function fmtDate(d: string): string {
  return /^\d{4}-\d{2}-\d{2}$/.test(d) ? `${d.slice(8, 10)}/${d.slice(5, 7)}/${d.slice(0, 4)}` : d;
}
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
          <span>{{ l.out.description }} → {{ l.in.description }}</span>
          <span><Money :value="l.in.amount" signed /></span>
        </div>
        <p class="text-xs text-gray-400">
          {{ fmtDate(l.out.date) }} · {{ accountName(l.out.accountId) }} → {{ fmtDate(l.in.date) }} · {{ accountName(l.in.accountId) }}
        </p>
        <button type="button" :class="[btn, 'mt-2']" :disabled="links.busy" data-test="link-undo" @click="links.undo(l.id)">{{ t("real.links.undo") }}</button>
      </li>
    </ul>
  </section>
</template>
