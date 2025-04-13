<template>
  <div class="max-w-7xl mx-auto space-y-6">
    <!-- Summary Cards -->
    <div class="grid grid-cols-1 md:grid-cols-3 gap-6">
      <div class="bg-white dark:bg-gray-800 p-6 rounded-lg shadow-sm">
        <h3 class="text-sm font-medium text-gray-500 dark:text-gray-400">
          Total Portfolio Value
        </h3>
        <p class="mt-2 text-3xl font-semibold text-gray-900 dark:text-white">
          {{ formatCurrency(investmentStore.currentValue || 0) }}
        </p>
      </div>
      <div class="bg-white dark:bg-gray-800 p-6 rounded-lg shadow-sm">
        <h3 class="text-sm font-medium text-gray-500 dark:text-gray-400">
          Total Profit/Loss
        </h3>
        <p
          :class="[
            'mt-2 text-3xl font-semibold',
            (investmentStore.totalProfit || 0) >= 0
              ? 'text-green-600 dark:text-green-400'
              : 'text-red-600 dark:text-red-400',
          ]"
        >
          {{ formatCurrency(investmentStore.totalProfit || 0) }}
        </p>
      </div>
      <div class="bg-white dark:bg-gray-800 p-6 rounded-lg shadow-sm">
        <h3 class="text-sm font-medium text-gray-500 dark:text-gray-400">
          Profit Percentage
        </h3>
        <p
          :class="[
            'mt-2 text-3xl font-semibold',
            (investmentStore.profitPercentage || 0) >= 0
              ? 'text-green-600 dark:text-green-400'
              : 'text-red-600 dark:text-red-400',
          ]"
        >
          {{ (investmentStore.profitPercentage || 0).toFixed(2) }}%
        </p>
      </div>
    </div>

    <!-- Charts Section -->
    <div class="grid grid-cols-1 lg:grid-cols-3 gap-6">
      <div class="lg:col-span-2">
        <PortfolioHistoryChart />
      </div>
      <div>
        <PortfolioAllocationChart />
      </div>
    </div>

    <!-- Investments Section -->
    <div class="bg-white dark:bg-gray-800 rounded-lg shadow-sm">
      <div class="p-6 flex justify-between items-center">
        <div class="flex justify-between items-center mb-6">
          <h2 class="text-xl font-semibold text-gray-900 dark:text-white">
            Your Investments
          </h2>
        </div>
        <button
          @click="showAddInvestment = true"
          class="inline-flex items-center px-4 py-2 border border-transparent rounded-md shadow-sm text-sm font-medium text-white bg-blue-600 hover:bg-blue-700 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-blue-500"
        >
          <svg
            class="-ml-1 mr-2 h-5 w-5"
            xmlns="http://www.w3.org/2000/svg"
            fill="none"
            viewBox="0 0 24 24"
            stroke="currentColor"
          >
            <path
              stroke-linecap="round"
              stroke-linejoin="round"
              stroke-width="2"
              d="M12 6v6m0 0v6m0-6h6m-6 0H6"
            />
          </svg>
          Add Investment
        </button>
      </div>
      <InvestmentList />
    </div>

    <!-- Investment Form Modal -->
    <InvestmentFormModal
      :show="showAddInvestment"
      @close="showAddInvestment = false"
    />
  </div>
</template>

<script setup lang="ts">
import { ref } from "vue";
import { useInvestmentStore } from "@/store/investments";
import InvestmentList from "@/components/InvestmentList.vue";
import InvestmentFormModal from "@/components/InvestmentFormModal.vue";
import PortfolioHistoryChart from "@/components/PortfolioHistoryChart.vue";
import PortfolioAllocationChart from "@/components/PortfolioAllocationChart.vue";

const investmentStore = useInvestmentStore();
const showAddInvestment = ref(false);

const formatCurrency = (amount: number) => {
  return new Intl.NumberFormat("en-US", {
    style: "currency",
    currency: "USD",
  }).format(amount);
};
</script>
