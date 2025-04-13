<template>
  <div class="bg-white dark:bg-gray-800 p-6 rounded-lg shadow">
    <h3 class="text-lg font-semibold mb-4">Monthly Overview</h3>
    <div class="h-64">
      <Bar :data="chartData" :options="chartOptions" />
    </div>
  </div>
</template>

<script setup lang="ts">
import { Bar } from "vue-chartjs";
import {
  Chart as ChartJS,
  CategoryScale,
  LinearScale,
  BarElement,
  Title,
  Tooltip,
  Legend,
} from "chart.js";
import { computed } from "vue";
import { useTransactionStore } from "@/store/transactions";
import { storeToRefs } from "pinia";

ChartJS.register(
  CategoryScale,
  LinearScale,
  BarElement,
  Title,
  Tooltip,
  Legend
);

const transactionStore = useTransactionStore();
const { transactions } = storeToRefs(transactionStore);

const chartData = computed(() => {
  const monthlyData = new Map<string, { income: number; expense: number }>();

  transactions.value.forEach((transaction) => {
    const month = new Date(transaction.date).toLocaleString("default", {
      month: "short",
    });
    const current = monthlyData.get(month) || { income: 0, expense: 0 };

    if (transaction.type === "income") {
      current.income += transaction.amount;
    } else {
      current.expense += transaction.amount;
    }

    monthlyData.set(month, current);
  });

  const months = Array.from(monthlyData.keys());

  return {
    labels: months,
    datasets: [
      {
        label: "Income",
        data: months.map((month) => monthlyData.get(month)?.income || 0),
        backgroundColor: "#4CAF50",
      },
      {
        label: "Expenses",
        data: months.map((month) => monthlyData.get(month)?.expense || 0),
        backgroundColor: "#F44336",
      },
    ],
  };
});

const chartOptions = {
  responsive: true,
  maintainAspectRatio: false,
  plugins: {
    legend: {
      position: "top" as const,
      labels: {
        color: "#6B7280",
        font: {
          size: 12,
        },
      },
    },
  },
  scales: {
    y: {
      beginAtZero: true,
      ticks: {
        color: "#6B7280",
      },
    },
    x: {
      ticks: {
        color: "#6B7280",
      },
    },
  },
};
</script>
