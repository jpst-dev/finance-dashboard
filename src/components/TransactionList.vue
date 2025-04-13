<template>
  <div class="bg-white dark:bg-gray-800 p-6 rounded-lg shadow">
    <h3 class="text-lg font-semibold mb-4">Recent Transactions</h3>
    <div class="space-y-4">
      <div
        v-if="transactions.length === 0"
        class="text-gray-500 dark:text-gray-400"
      >
        No transactions yet
      </div>
      <div
        v-for="transaction in transactions"
        :key="transaction.id"
        class="flex items-center justify-between p-4 border rounded-lg dark:border-gray-700"
      >
        <div class="flex items-center space-x-4">
          <div
            :class="[
              'w-10 h-10 rounded-full flex items-center justify-center',
              transaction.type === 'income'
                ? 'bg-green-100 text-green-600 dark:bg-green-900 dark:text-green-300'
                : 'bg-red-100 text-red-600 dark:bg-red-900 dark:text-red-300',
            ]"
          >
            {{ transaction.type === "income" ? "↑" : "↓" }}
          </div>
          <div>
            <p class="font-medium">{{ transaction.description }}</p>
            <p class="text-sm text-gray-500 dark:text-gray-400">
              {{ transaction.category }} • {{ formatDate(transaction.date) }}
            </p>
          </div>
        </div>
        <div class="text-right">
          <p
            :class="[
              'font-semibold',
              transaction.type === 'income'
                ? 'text-green-600 dark:text-green-400'
                : 'text-red-600 dark:text-red-400',
            ]"
          >
            {{ transaction.type === "income" ? "+" : "-" }}${{
              transaction.amount.toFixed(2)
            }}
          </p>
        </div>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { useTransactionStore } from "@/store/transactions";
import { storeToRefs } from "pinia";

const transactionStore = useTransactionStore();
const { recentTransactions: transactions } = storeToRefs(transactionStore);

const formatDate = (dateString: string) => {
  return new Date(dateString).toLocaleDateString("en-US", {
    year: "numeric",
    month: "short",
    day: "numeric",
  });
};
</script>
