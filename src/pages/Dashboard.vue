<template>
  <div class="space-y-8">
    <div class="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-4 gap-4">
      <div class="bg-white dark:bg-gray-800 p-6 rounded-lg shadow">
        <h3 class="text-lg font-semibold text-gray-700 dark:text-gray-300">
          Total Balance
        </h3>
        <p class="text-2xl font-bold">
          {{ formatCurrency(transactionStore.balance) }}
        </p>
      </div>
      <div class="bg-white dark:bg-gray-800 p-6 rounded-lg shadow">
        <h3 class="text-lg font-semibold text-gray-700 dark:text-gray-300">
          Monthly Income
        </h3>
        <p class="text-2xl font-bold text-green-600">
          {{ formatCurrency(transactionStore.totalIncome) }}
        </p>
      </div>
      <div class="bg-white dark:bg-gray-800 p-6 rounded-lg shadow">
        <h3 class="text-lg font-semibold text-gray-700 dark:text-gray-300">
          Monthly Expenses
        </h3>
        <p class="text-2xl font-bold text-red-600">
          {{ formatCurrency(transactionStore.totalExpenses) }}
        </p>
      </div>
      <div class="bg-white dark:bg-gray-800 p-6 rounded-lg shadow">
        <h3 class="text-lg font-semibold text-gray-700 dark:text-gray-300">
          Investments
        </h3>
        <p class="text-2xl font-bold">$0.00</p>
      </div>
    </div>

    <div class="grid grid-cols-1 lg:grid-cols-2 gap-8">
      <MonthlyChart />
      <ExpenseChart />
    </div>

    <div class="grid grid-cols-1 lg:grid-cols-2 gap-8">
      <TransactionList />
    </div>
  </div>
</template>

<script setup lang="ts">
import { useTransactionStore } from "@/store/transactions";
import TransactionList from "@/components/TransactionList.vue";
import MonthlyChart from "@/components/MonthlyChart.vue";
import ExpenseChart from "@/components/ExpenseChart.vue";

const transactionStore = useTransactionStore();

const formatCurrency = (amount: number) => {
  return new Intl.NumberFormat("en-US", {
    style: "currency",
    currency: "USD",
  }).format(amount);
};
</script>
