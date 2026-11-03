<script setup lang="ts">
import { useI18n } from "vue-i18n";
import type { Transaction } from "../api/types";
import Money from "../money/Money.vue";

defineProps<{ items: Transaction[]; accountName: (id: string) => string; categoryName: (id: string | null) => string }>();
const emit = defineEmits<{ edit: [t: Transaction]; remove: [t: Transaction] }>();
const { t } = useI18n();

/** "YYYY-MM-DD" -> "dd/mm/yyyy" por TEXTO (sem Date: sem deslocamento de fuso). */
function fmtDate(d: string): string {
  return /^\d{4}-\d{2}-\d{2}$/.test(d) ? `${d.slice(8, 10)}/${d.slice(5, 7)}/${d.slice(0, 4)}` : d;
}
const btn =
  "rounded border border-gray-600 px-2 py-1 text-sm focus-visible:outline focus-visible:outline-2 focus-visible:outline-blue-300";
</script>

<template>
  <div class="overflow-x-auto">
    <table class="mt-3 w-full text-left text-sm" data-test="tx-table">
      <thead>
        <tr class="border-b border-gray-700 text-gray-400">
          <th scope="col" class="py-2 pr-3">{{ t("real.transactions.date") }}</th>
          <th scope="col" class="pr-3">{{ t("real.transactions.description") }}</th>
          <th scope="col" class="pr-3">{{ t("real.transactions.account") }}</th>
          <th scope="col" class="pr-3">{{ t("real.transactions.category") }}</th>
          <th scope="col" class="pr-3 text-right">{{ t("real.transactions.amount") }}</th>
          <th scope="col" class="pr-3">{{ t("real.transactions.state") }}</th>
          <th scope="col"><span class="sr-only">{{ t("real.transactions.actions") }}</span></th>
        </tr>
      </thead>
      <tbody>
        <tr v-for="tx in items" :key="tx.id" class="border-b border-gray-800" data-test="tx-row">
          <td class="py-2 pr-3 whitespace-nowrap">{{ fmtDate(tx.date) }}</td>
          <td class="pr-3" data-test="tx-desc">{{ tx.description }}</td>
          <td class="pr-3">{{ accountName(tx.accountId) }}</td>
          <td class="pr-3">{{ categoryName(tx.categoryId) }}</td>
          <td class="pr-3 text-right" data-test="tx-amount"><Money :value="tx.amount" signed /></td>
          <td class="pr-3">
            <span
              class="rounded px-2 py-0.5 text-xs"
              :class="tx.state === 'PendingClassification' ? 'bg-amber-900 text-amber-100' : 'bg-gray-800 text-gray-300'"
              data-test="tx-state"
            >
              {{ t(`real.transactions.states.${tx.state}`) }}
            </span>
          </td>
          <td class="whitespace-nowrap">
            <button type="button" :class="btn" data-test="tx-edit" :aria-label="t('real.transactions.editRow', { description: tx.description })" @click="emit('edit', tx)">{{ t("real.transactions.edit") }}</button>
            <button type="button" :class="[btn, 'ml-1']" data-test="tx-delete" :aria-label="t('real.transactions.deleteRow', { description: tx.description })" @click="emit('remove', tx)">{{ t("real.transactions.delete") }}</button>
          </td>
        </tr>
      </tbody>
    </table>
  </div>
</template>
