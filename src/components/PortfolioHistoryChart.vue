<template>
  <div class="bg-white dark:bg-gray-800 p-6 rounded-lg shadow-sm min-h-[105px]">
    <div
      class="flex flex-col sm:flex-row justify-between items-start sm:items-center gap-4 mb-6"
    >
      <div class="flex items-center gap-2">
        <h3 class="text-base font-medium text-gray-900 dark:text-gray-100">
          {{ t("portfolio.portfolioPerformance") }}
        </h3>
        <button
          @click="isCollapsed = !isCollapsed"
          class="p-1 text-gray-500 hover:text-gray-700 dark:text-gray-400 dark:hover:text-gray-200"
          :title="isCollapsed ? t('common.expand') : t('common.collapse')"
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
      <div class="flex flex-wrap gap-2">
        <button
          v-for="period in periods"
          :key="period.value"
          @click="selectedPeriod = period.value"
          :class="[
            'px-3 py-1.5 text-sm font-medium rounded-md transition-colors',
            selectedPeriod === period.value
              ? 'bg-blue-100 text-blue-700 dark:bg-blue-900 dark:text-blue-300'
              : 'text-gray-600 dark:text-gray-400 hover:bg-gray-100 dark:hover:bg-gray-700',
          ]"
        >
          {{ t(`portfolio.periods.${period.value}`) }}
        </button>
      </div>
    </div>
    <div v-show="!isCollapsed" class="h-[157px] transition-all duration-300">
      <Line :data="chartData" :options="chartOptions" />
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, computed } from "vue";
import {
  Chart as ChartJS,
  CategoryScale,
  LinearScale,
  PointElement,
  LineElement,
  Title,
  Tooltip,
  Legend,
} from "chart.js";
import { Line } from "vue-chartjs";
import { useI18n } from "vue-i18n";

const { t } = useI18n();

ChartJS.register(
  CategoryScale,
  LinearScale,
  PointElement,
  LineElement,
  Title,
  Tooltip,
  Legend
);

const periods = [
  { label: "24h", value: "24h" },
  { label: "7d", value: "7d" },
  { label: "30d", value: "30d" },
  { label: "90d", value: "90d" },
  { label: "all", value: "all" },
];

const selectedPeriod = ref("all");
const isCollapsed = ref(false);

const chartData = computed(() => {
  // Simulando dados históricos por enquanto
  const dates = generateDates();
  const portfolioValues = generatePortfolioValues(dates);
  const btcTrendValues = generateBTCTrendValues(dates);

  return {
    labels: dates.map((date) => date.toLocaleDateString()),
    datasets: [
      {
        label: "All-time profit",
        data: portfolioValues,
        borderColor: "rgb(59, 130, 246)",
        backgroundColor: "rgba(59, 130, 246, 0.5)",
        tension: 0.4,
      },
      {
        label: "BTC trend",
        data: btcTrendValues,
        borderColor: "rgb(249, 115, 22)",
        backgroundColor: "rgba(249, 115, 22, 0.5)",
        tension: 0.4,
      },
    ],
  };
});

const chartOptions = {
  responsive: true,
  maintainAspectRatio: false,
  interaction: {
    intersect: false,
    mode: "index" as const,
  },
  plugins: {
    legend: {
      position: "top" as const,
      align: "end" as const,
      labels: {
        boxWidth: 8,
        usePointStyle: true,
        pointStyle: "circle",
        padding: 20,
        color: "#6B7280",
      },
    },
    tooltip: {
      backgroundColor: "rgba(17, 24, 39, 0.8)",
      titleColor: "#F3F4F6",
      bodyColor: "#F3F4F6",
      padding: 12,
      borderColor: "rgba(75, 85, 99, 0.2)",
      borderWidth: 1,
      displayColors: false,
      callbacks: {
        label: (context: any) => {
          return new Intl.NumberFormat("en-US", {
            style: "currency",
            currency: "USD",
            minimumFractionDigits: 0,
            maximumFractionDigits: 0,
          }).format(context.raw);
        },
      },
    },
  },
  scales: {
    x: {
      grid: {
        display: false,
      },
      ticks: {
        color: "#6B7280",
        maxRotation: 0,
        autoSkipPadding: 20,
      },
    },
    y: {
      grid: {
        color: "rgba(107, 114, 128, 0.1)",
      },
      border: {
        display: false,
      },
      ticks: {
        color: "#6B7280",
        padding: 10,
        callback: function (this: any, tickValue: string | number) {
          return new Intl.NumberFormat("en-US", {
            style: "currency",
            currency: "USD",
            minimumFractionDigits: 0,
            maximumFractionDigits: 0,
          }).format(Number(tickValue));
        },
      },
    },
  },
};

// Funções auxiliares para gerar dados de exemplo
function generateDates() {
  const dates = [];
  const end = new Date();
  const start = new Date();
  start.setDate(start.getDate() - 90);

  for (let d = start; d <= end; d.setDate(d.getDate() + 1)) {
    dates.push(new Date(d));
  }
  return dates;
}

function generatePortfolioValues(dates: Date[]) {
  let value = 100000;
  return dates.map(() => {
    value = value * (1 + (Math.random() - 0.5) * 0.02);
    return value;
  });
}

function generateBTCTrendValues(dates: Date[]) {
  let value = 100000;
  return dates.map(() => {
    value = value * (1 + (Math.random() - 0.48) * 0.02);
    return value;
  });
}
</script>
