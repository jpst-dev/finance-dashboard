<template>
  <div class="bg-white dark:bg-gray-800 p-6 rounded-lg shadow">
    <h3 class="text-lg font-semibold mb-4">Expenses by Category</h3>
    <div class="h-64">
      <Pie :data="chartData" :options="chartOptions" />
    </div>
  </div>
</template>

<script setup lang="ts">
import { Pie } from "vue-chartjs";
import { Chart as ChartJS, ArcElement, Tooltip, Legend } from "chart.js";
import { computed } from "vue";
import { useTransactionStore } from "@/store/transactions";
import { storeToRefs } from "pinia";

ChartJS.register(ArcElement, Tooltip, Legend);

const transactionStore = useTransactionStore();
const { transactions } = storeToRefs(transactionStore);

const chartData = computed(() => {
  const expenses = transactions.value.filter((t) => t.type === "expense");
  const categories = new Map<string, number>();

  expenses.forEach((expense) => {
    const current = categories.get(expense.category) || 0;
    categories.set(expense.category, current + expense.amount);
  });

  return {
    labels: Array.from(categories.keys()),
    datasets: [
      {
        data: Array.from(categories.values()),
        backgroundColor: [
          "#FF6384",
          "#36A2EB",
          "#FFCE56",
          "#4BC0C0",
          "#9966FF",
          "#FF9F40",
          "#8AC24A",
          "#FF5252",
          "#7E57C2",
          "#26A69A",
        ],
      },
    ],
  };
});

const chartOptions = {
  responsive: true,
  maintainAspectRatio: false,
  plugins: {
    legend: {
      position: "right" as const,
      labels: {
        color: "#6B7280",
        font: {
          size: 12,
        },
      },
    },
  },
};
</script>
