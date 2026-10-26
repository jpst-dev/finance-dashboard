<template>
  <div class="bg-white dark:bg-gray-800 p-6 rounded-lg shadow">
    <h3 class="text-lg font-semibold mb-4">
      {{ t("dashboard.charts.expensesByCategory") }}
    </h3>
    <div class="h-64">
      <Doughnut :data="chartData" :options="chartOptions" />
    </div>
  </div>
</template>

<script setup lang="ts">
import { Doughnut } from "vue-chartjs";
import { Chart as ChartJS, ArcElement, Tooltip, Legend } from "chart.js";
import { computed } from "vue";
import { useTransactionStore } from "@/store/transactions";
import { storeToRefs } from "pinia";
import { useI18n } from "vue-i18n";

const { t } = useI18n();

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
          "#c6f432",
          "#7dd3fc",
          "#f0abfc",
          "#fca5a5",
          "#fcd34d",
          "#5eead4",
          "#c4b5fd",
          "#fdba74",
          "#94a3b8",
          "#3a414c",
        ],
        borderColor: "#121418",
        borderWidth: 3,
      },
    ],
  };
});

const chartOptions = {
  responsive: true,
  maintainAspectRatio: false,
  cutout: "62%",
  plugins: {
    legend: {
      position: "right" as const,
      labels: {
        color: "#8b93a1",
        font: {
          size: 12,
        },
      },
    },
  },
};
</script>
