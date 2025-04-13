<template>
  <div class="bg-white dark:bg-gray-800 p-6 rounded-lg shadow min-h-[105px]">
    <div class="flex items-center justify-between mb-4 h-[32px]">
      <div class="flex items-center gap-2">
        <h3 class="text-lg font-semibold text-gray-700 dark:text-gray-300">
          Alocação
        </h3>
        <button
          @click="isCollapsed = !isCollapsed"
          class="p-1 text-gray-500 hover:text-gray-700 dark:text-gray-400 dark:hover:text-gray-200"
          :title="isCollapsed ? 'Expand' : 'Collapse'"
        >
          <svg
            class="w-5 h-5 transition-transform"
            :class="{ 'rotate-180': isCollapsed }"
            fill="none"
            stroke="currentColor"
            viewBox="0 0 24 24"
          >
            <path
              stroke-linecap="round"
              stroke-linejoin="round"
              stroke-width="2"
              d="M19 9l-7 7-7-7"
            />
          </svg>
        </button>
      </div>
    </div>
    <div
      v-show="!isCollapsed"
      class="flex items-center gap-6 transition-all duration-300"
    >
      <div class="w-[165px] h-[165px] flex-shrink-0">
        <Doughnut v-if="!isLoading" :data="chartData" :options="chartOptions" />
        <div v-else class="w-full h-full flex items-center justify-center">
          <div
            class="animate-spin rounded-full h-8 w-8 border-b-2 border-gray-900 dark:border-gray-100"
          ></div>
        </div>
      </div>
      <div class="flex-1 space-y-2 min-w-[120px]">
        <template v-if="!isLoading">
          <div
            v-for="(allocation, index) in allocations"
            :key="allocation.symbol"
            class="flex items-center justify-between"
          >
            <div class="flex items-center space-x-2">
              <div
                class="w-3 h-3 rounded-full"
                :style="{ backgroundColor: chartColors[index] }"
              ></div>
              <span class="text-sm text-gray-600 dark:text-gray-400">{{
                allocation.symbol
              }}</span>
            </div>
            <span class="text-sm font-medium text-gray-900 dark:text-white"
              >{{ allocation.percentage.toFixed(2) }}%</span
            >
          </div>
        </template>
        <div v-else class="space-y-2">
          <div
            v-for="i in 3"
            :key="i"
            class="flex items-center justify-between"
          >
            <div class="flex items-center space-x-2">
              <div
                class="w-3 h-3 rounded-full bg-gray-200 dark:bg-gray-700"
              ></div>
              <div class="h-4 w-16 bg-gray-200 dark:bg-gray-700 rounded"></div>
            </div>
            <div class="h-4 w-12 bg-gray-200 dark:bg-gray-700 rounded"></div>
          </div>
        </div>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, computed, onMounted } from "vue";
import { Chart as ChartJS, ArcElement, Tooltip, Legend } from "chart.js";
import { Doughnut } from "vue-chartjs";
import { useInvestmentStore } from "@/store/investments";
import { storeToRefs } from "pinia";

ChartJS.register(ArcElement, Tooltip, Legend);

const investmentStore = useInvestmentStore();
const { investments } = storeToRefs(investmentStore);
const isLoading = ref(true);

const isCollapsed = ref(false);

const chartColors = [
  "rgb(59, 130, 246)", // Blue
  "rgb(16, 185, 129)", // Green
  "rgb(249, 115, 22)", // Orange
  "rgb(236, 72, 153)", // Pink
  "rgb(139, 92, 246)", // Purple
  "rgb(245, 158, 11)", // Yellow
];

const total = computed(() => {
  if (!investments?.value || !Array.isArray(investments.value)) return 0;

  return investments.value.reduce((sum, investment) => {
    if (!investment) return sum;
    return sum + investment.amount * (investment.currentPrice || 0);
  }, 0);
});

const allocations = computed(() => {
  if (!investments?.value || !Array.isArray(investments.value)) return [];

  const data = investments.value
    .filter((investment) => investment && investment.amount > 0)
    .map((investment) => {
      const value = investment.amount * (investment.currentPrice || 0);
      const totalValue = total.value || 1;

      return {
        symbol: investment.symbol || "Unknown",
        name: investment.name || "Unknown",
        value,
        percentage: (value / totalValue) * 100,
      };
    })
    .sort((a, b) => b.value - a.value);

  return data;
});

const chartData = computed(() => {
  if (
    !allocations?.value ||
    !Array.isArray(allocations.value) ||
    allocations.value.length === 0
  ) {
    return {
      labels: ["No investments"],
      datasets: [
        {
          data: [100],
          backgroundColor: ["rgb(229, 231, 235)"],
          borderWidth: 0,
        },
      ],
    };
  }

  return {
    labels: allocations.value.map(
      (a) => `${a.symbol} (${a.percentage.toFixed(1)}%)`
    ),
    datasets: [
      {
        data: allocations.value.map((a) => a.percentage),
        backgroundColor: chartColors,
        borderWidth: 0,
      },
    ],
  };
});

const chartOptions = {
  responsive: true,
  maintainAspectRatio: false,
  plugins: {
    legend: {
      display: false,
    },
    tooltip: {
      backgroundColor: "rgba(17, 24, 39, 0.8)",
      titleColor: "#F3F4F6",
      bodyColor: "#F3F4F6",
      padding: 12,
      callbacks: {
        label: (context: any) => {
          return `${context.label}: ${context.raw.toFixed(2)}%`;
        },
      },
    },
  },
  cutout: "75%",
};

onMounted(() => {
  // Simular um pequeno delay para garantir que os dados estejam carregados
  setTimeout(() => {
    isLoading.value = false;
  }, 500);
});
</script>
