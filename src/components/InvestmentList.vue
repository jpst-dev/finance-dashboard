<template>
  <div class="bg-white dark:bg-gray-800 rounded-lg shadow p-6">
    <div v-if="isLoading" class="space-y-4">
      <div
        v-for="i in 3"
        :key="i"
        class="border dark:border-gray-700 rounded-lg p-4"
      >
        <div class="flex justify-between items-start">
          <div class="flex items-center space-x-3">
            <div
              class="w-10 h-10 rounded-full bg-gray-200 dark:bg-gray-700"
            ></div>
            <div>
              <div class="h-4 w-24 bg-gray-200 dark:bg-gray-700 rounded"></div>
              <div
                class="h-3 w-16 bg-gray-200 dark:bg-gray-700 rounded mt-2"
              ></div>
            </div>
          </div>
        </div>
        <div class="mt-4 grid grid-cols-2 md:grid-cols-4 gap-4">
          <div v-for="i in 4" :key="i">
            <div class="h-3 w-16 bg-gray-200 dark:bg-gray-700 rounded"></div>
            <div
              class="h-4 w-20 bg-gray-200 dark:bg-gray-700 rounded mt-2"
            ></div>
          </div>
        </div>
      </div>
    </div>

    <div v-else-if="!investmentList.length" class="text-center py-8">
      <p class="text-gray-500 dark:text-gray-400">
        No investments yet. Add your first investment!
      </p>
    </div>

    <div v-else class="space-y-4">
      <div
        v-for="investment in investmentList"
        :key="investment.id"
        class="border dark:border-gray-700 rounded-lg p-4 hover:bg-gray-50 dark:hover:bg-gray-700 transition-colors"
      >
        <div class="flex justify-between items-start">
          <div class="flex items-center space-x-3">
            <div class="flex items-center">
              <img
                v-if="investment.type === 'crypto'"
                :src="investment.image"
                :alt="investment.symbol"
                class="w-8 h-8 rounded-full mr-3"
              />
              <div
                v-else
                class="w-8 h-8 rounded-full bg-gray-200 flex items-center justify-center mr-3"
              >
                <svg
                  class="h-5 w-5 text-gray-500"
                  viewBox="0 0 24 24"
                  fill="none"
                  stroke="currentColor"
                >
                  <path
                    stroke-linecap="round"
                    stroke-linejoin="round"
                    stroke-width="2"
                    d="M12 8c-1.657 0-3 .895-3 2s1.343 2 3 2 3 .895 3 2-1.343 2-3 2m0-8c1.11 0 2.08.402 2.599 1M12 8V7m0 1v8m0 0v1m0-1c-1.11 0-2.08-.402-2.599-1M21 12a9 9 0 11-18 0 9 9 0 0118 0z"
                  />
                </svg>
              </div>
            </div>
            <div>
              <h3 class="font-medium text-gray-900 dark:text-white">
                {{ investment.name }}
              </h3>
              <p class="text-sm text-gray-500 dark:text-gray-400">
                {{ investment.symbol }}
              </p>
            </div>
          </div>
          <div class="flex items-center space-x-2">
            <button
              @click="showTransactions(investment)"
              class="p-1.5 text-gray-500 hover:text-gray-700 dark:text-gray-400 dark:hover:text-gray-200"
            >
              <svg
                class="w-5 h-5"
                fill="none"
                stroke="currentColor"
                viewBox="0 0 24 24"
              >
                <path
                  stroke-linecap="round"
                  stroke-linejoin="round"
                  stroke-width="2"
                  d="M9 5H7a2 2 0 00-2 2v12a2 2 0 002 2h10a2 2 0 002-2V7a2 2 0 00-2-2h-2M9 5a2 2 0 002 2h2a2 2 0 002-2M9 5a2 2 0 012-2h2a2 2 0 012 2"
                />
              </svg>
            </button>
            <button
              @click="confirmDelete(investment)"
              class="p-1.5 text-red-500 hover:text-red-700 dark:text-red-400 dark:hover:text-red-200"
            >
              <svg
                class="w-5 h-5"
                fill="none"
                stroke="currentColor"
                viewBox="0 0 24 24"
              >
                <path
                  stroke-linecap="round"
                  stroke-linejoin="round"
                  stroke-width="2"
                  d="M19 7l-.867 12.142A2 2 0 0116.138 21H7.862a2 2 0 01-1.995-1.858L5 7m5 4v6m4-6v6m1-10V4a1 1 0 00-1-1h-4a1 1 0 00-1 1v3M4 7h16"
                />
              </svg>
            </button>
          </div>
        </div>

        <div class="mt-4 grid grid-cols-2 md:grid-cols-4 gap-4">
          <div>
            <p class="text-sm text-gray-500 dark:text-gray-400">Holdings</p>
            <div class="flex items-baseline space-x-2">
              <p class="font-medium text-gray-900 dark:text-white">
                {{ investment.amount }} {{ investment.symbol }}
              </p>
              <p class="text-sm text-gray-500 dark:text-gray-400">
                ({{
                  formatCurrency(investment.amount * investment.currentPrice)
                }})
              </p>
            </div>
          </div>
          <div>
            <p class="text-sm text-gray-500 dark:text-gray-400">
              Avg Buy Price
            </p>
            <p class="font-medium text-gray-900 dark:text-white">
              {{ formatCurrency(investment.purchasePrice) }}
            </p>
          </div>
          <div>
            <p class="text-sm text-gray-500 dark:text-gray-400">
              Current Price
            </p>
            <p class="font-medium text-gray-900 dark:text-white">
              {{ formatCurrency(investment.currentPrice) }}
            </p>
          </div>
          <div>
            <p class="text-sm text-gray-500 dark:text-gray-400">24h Change</p>
            <p
              :class="[
                'font-medium',
                getPriceChange24h(investment.symbol) > 0
                  ? 'text-green-600 dark:text-green-400'
                  : getPriceChange24h(investment.symbol) < 0
                  ? 'text-red-600 dark:text-red-400'
                  : 'text-gray-900 dark:text-white',
              ]"
            >
              {{
                getPriceChange24h(investment.symbol) !== null
                  ? `${getPriceChange24h(investment.symbol).toFixed(2)}%`
                  : "N/A"
              }}
            </p>
          </div>
        </div>

        <div class="mt-4 pt-4 border-t dark:border-gray-700">
          <div class="grid grid-cols-2 gap-4">
            <div>
              <p class="text-sm text-gray-500 dark:text-gray-400">
                Total Invested
              </p>
              <p class="font-medium text-gray-900 dark:text-white">
                {{
                  formatCurrency(investment.amount * investment.purchasePrice)
                }}
              </p>
            </div>
            <div>
              <p class="text-sm text-gray-500 dark:text-gray-400">
                Profit/Loss
              </p>
              <div class="flex items-baseline space-x-2">
                <p
                  :class="[
                    'font-medium',
                    getProfitLoss(investment) > 0
                      ? 'text-green-600 dark:text-green-400'
                      : getProfitLoss(investment) < 0
                      ? 'text-red-600 dark:text-red-400'
                      : 'text-gray-900 dark:text-white',
                  ]"
                >
                  {{ formatCurrency(getProfitLoss(investment)) }}
                </p>
                <p
                  :class="[
                    'text-sm',
                    getProfitLossPercentage(investment) > 0
                      ? 'text-green-600 dark:text-green-400'
                      : getProfitLossPercentage(investment) < 0
                      ? 'text-red-600 dark:text-red-400'
                      : 'text-gray-900 dark:text-white',
                  ]"
                >
                  ({{ getProfitLossPercentage(investment).toFixed(2) }}%)
                </p>
              </div>
            </div>
          </div>
        </div>
      </div>
    </div>

    <CryptoTransactions
      v-if="selectedInvestment"
      :show="showTransactionModal"
      :symbol="selectedInvestment.symbol"
      :name="selectedInvestment.name"
      :transactions="getInvestmentTransactions(selectedInvestment.symbol)"
      :current-price="selectedInvestment.currentPrice"
      @close="closeTransactions"
    />

    <ConfirmationModal
      :show="showDeleteModal"
      title="Delete Investment"
      :message="`Are you sure you want to delete your ${investmentToDelete?.name} (${investmentToDelete?.symbol}) investment? This action cannot be undone.`"
      confirm-button-text="Delete"
      @close="closeDeleteModal"
      @confirm="handleDelete"
    />
  </div>
</template>

<script setup lang="ts">
import { ref, computed, onMounted, watch } from "vue";
import { useInvestmentStore } from "@/store/investments";
import { storeToRefs } from "pinia";
import CryptoTransactions from "./CryptoTransactions.vue";
import ConfirmationModal from "./ConfirmationModal.vue";
import { CRYPTO_IMAGES } from "@/services/cryptoService";
import type { Investment } from "@/store/investments";
import { useToast } from "vue-toastification";

const investmentStore = useInvestmentStore();
const { investments, transactions, priceChanges } =
  storeToRefs(investmentStore);
const toast = useToast();

const isLoading = ref(false);
const showTransactionModal = ref(false);
const selectedInvestment = ref<Investment | null>(null);
const showDeleteModal = ref(false);
const investmentToDelete = ref<Investment | null>(null);

const investmentList = computed(() => {
  if (!investments.value || !Array.isArray(investments.value)) return [];

  return investments.value
    .filter((investment) => investment && investment.amount > 0)
    .map((investment) => ({
      ...investment,
      image:
        investment.type === "crypto" && investment.name
          ? CRYPTO_IMAGES[investment.name.toLowerCase()]
          : undefined,
      totalValue: investment.amount * (investment.currentPrice || 0),
    }))
    .sort((a, b) => {
      const valueA = a.totalValue || 0;
      const valueB = b.totalValue || 0;
      return valueB - valueA;
    });
});

const formatCurrency = (value: number) => {
  if (isNaN(value)) return "$0.00";
  return new Intl.NumberFormat("en-US", {
    style: "currency",
    currency: "USD",
  }).format(value);
};

const getPriceChange24h = (symbol: string) => {
  if (!priceChanges.value || !symbol) return 0;
  return priceChanges.value[symbol.toUpperCase()] || 0;
};

const getInvestmentTransactions = (symbol: string) => {
  if (!transactions.value || !symbol) return [];
  return transactions.value.filter((t) => t.symbol === symbol);
};

const getProfitLoss = (investment: Investment) => {
  if (!investment) return 0;
  const totalInvested =
    (investment.amount || 0) * (investment.purchasePrice || 0);
  const currentValue =
    (investment.amount || 0) * (investment.currentPrice || 0);
  return currentValue - totalInvested;
};

const getProfitLossPercentage = (investment: Investment) => {
  if (!investment) return 0;
  const totalInvested =
    (investment.amount || 0) * (investment.purchasePrice || 0);
  if (totalInvested === 0) return 0;
  return (getProfitLoss(investment) / totalInvested) * 100;
};

const showTransactions = (investment: Investment) => {
  selectedInvestment.value = investment;
  showTransactionModal.value = true;
};

const closeTransactions = () => {
  showTransactionModal.value = false;
  selectedInvestment.value = null;
};

const confirmDelete = (investment: Investment) => {
  investmentToDelete.value = investment;
  showDeleteModal.value = true;
};

const handleDelete = async () => {
  if (!investmentToDelete.value) return;

  try {
    await investmentStore.removeInvestment(investmentToDelete.value.id);
    toast.success("Investment deleted successfully!");
  } catch (error) {
    console.error("Error deleting investment:", error);
    toast.error("Failed to delete investment. Please try again.");
  } finally {
    closeDeleteModal();
  }
};

const closeDeleteModal = () => {
  showDeleteModal.value = false;
  investmentToDelete.value = null;
};

onMounted(() => {
  investmentStore.initializePriceUpdates();
});

watch(
  priceChanges,
  () => {
    // Price changes updated, no need to log
  },
  { deep: true }
);
</script>
