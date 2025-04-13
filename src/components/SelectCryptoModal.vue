<template>
  <div
    v-if="show"
    class="fixed inset-0 bg-black bg-opacity-50 flex items-center justify-center z-50"
  >
    <div
      class="bg-white dark:bg-gray-800 rounded-lg shadow-xl max-w-md w-full mx-4"
    >
      <div class="p-6">
        <div class="flex justify-between items-center mb-6">
          <h3 class="text-lg font-semibold text-gray-900 dark:text-white">
            Selecionar Moeda
          </h3>
          <button
            @click="close"
            class="text-gray-500 hover:text-gray-700 dark:text-gray-400 dark:hover:text-gray-200"
          >
            <svg
              class="w-6 h-6"
              fill="none"
              stroke="currentColor"
              viewBox="0 0 24 24"
            >
              <path
                stroke-linecap="round"
                stroke-linejoin="round"
                stroke-width="2"
                d="M6 18L18 6M6 6l12 12"
              />
            </svg>
          </button>
        </div>

        <!-- Search Input -->
        <div class="relative mb-6">
          <input
            type="text"
            v-model="searchQuery"
            placeholder="Pesquisar moeda (ex: Bitcoin, ETH)"
            class="w-full p-3 pl-10 bg-gray-50 dark:bg-gray-700 border border-gray-200 dark:border-gray-600 rounded-lg focus:ring-2 focus:ring-blue-500 focus:border-blue-500"
          />
          <svg
            class="w-5 h-5 text-gray-400 absolute left-3 top-1/2 transform -translate-y-1/2"
            :class="{ 'animate-spin': isLoading }"
            fill="none"
            stroke="currentColor"
            viewBox="0 0 24 24"
          >
            <path
              v-if="!isLoading"
              stroke-linecap="round"
              stroke-linejoin="round"
              stroke-width="2"
              d="M21 21l-6-6m2-5a7 7 0 11-14 0 7 7 0 0114 0z"
            />
            <path
              v-else
              stroke-linecap="round"
              stroke-linejoin="round"
              stroke-width="2"
              d="M4 4v5h.582m15.356 2A8.001 8.001 0 004.582 9m0 0H9m11 11v-5h-.581m0 0a8.003 8.003 0 01-15.357-2m15.357 2H15"
            />
          </svg>
        </div>

        <!-- Cryptocurrency List -->
        <div class="space-y-2 max-h-[400px] overflow-y-auto custom-scrollbar">
          <div v-if="isLoading" class="text-center py-8">
            <div
              class="animate-spin rounded-full h-8 w-8 border-b-2 border-blue-500 mx-auto"
            ></div>
            <p class="mt-2 text-gray-500 dark:text-gray-400">
              Buscando moedas...
            </p>
          </div>

          <div
            v-else-if="filteredCryptos.length === 0"
            class="text-center py-8"
          >
            <p class="text-gray-500 dark:text-gray-400">
              Nenhuma moeda encontrada
            </p>
          </div>

          <button
            v-else
            v-for="crypto in filteredCryptos"
            :key="crypto.symbol"
            @click="handleSelect(crypto)"
            class="w-full flex items-center justify-between p-3 rounded-lg hover:bg-gray-50 dark:hover:bg-gray-700 transition-colors"
          >
            <div class="flex items-center space-x-3">
              <img
                :src="crypto.image"
                :alt="crypto.symbol"
                class="w-8 h-8 rounded-full"
              />
              <div class="text-left">
                <p class="font-medium text-gray-900 dark:text-white">
                  {{ crypto.symbol.toUpperCase() }}
                </p>
                <p class="text-sm text-gray-500 dark:text-gray-400">
                  {{ crypto.name }}
                </p>
              </div>
            </div>
            <div class="text-right">
              <p class="font-medium text-gray-900 dark:text-white">
                {{ formatCurrency(crypto.current_price) }}
              </p>
              <p
                :class="[
                  'text-sm',
                  crypto.price_change_percentage_24h >= 0
                    ? 'text-green-600 dark:text-green-400'
                    : 'text-red-600 dark:text-red-400',
                ]"
              >
                {{ crypto.price_change_percentage_24h.toFixed(2) }}%
              </p>
            </div>
          </button>
        </div>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, computed, watch } from "vue";
import type { CryptoPrice } from "@/services/cryptoService";
import { getCryptoPrices, SUPPORTED_CRYPTOS } from "@/services/cryptoService";
import { useToast } from "vue-toastification";
import debounce from "lodash/debounce";

const props = defineProps<{
  show: boolean;
}>();

const emit = defineEmits<{
  (e: "close"): void;
  (e: "select", crypto: CryptoPrice): void;
}>();

const toast = useToast();
const searchQuery = ref("");
const isLoading = ref(false);
const error = ref<string | null>(null);

const availableCryptos = ref<CryptoPrice[]>([]);

const filteredCryptos = computed(() => {
  if (!searchQuery.value) return availableCryptos.value;

  const query = searchQuery.value.toLowerCase();
  return availableCryptos.value.filter(
    (crypto) =>
      crypto.name.toLowerCase().includes(query) ||
      crypto.symbol.toLowerCase().includes(query)
  );
});

const handleSelect = (crypto: CryptoPrice) => {
  emit("select", crypto);
  emit("close");
};

const close = () => {
  searchQuery.value = "";
  error.value = null;
  emit("close");
};

const formatCurrency = (value: number) => {
  return new Intl.NumberFormat("en-US", {
    style: "currency",
    currency: "USD",
  }).format(value);
};

// Fetch available cryptocurrencies when component is mounted
const fetchAvailableCryptos = async () => {
  try {
    const cryptos = await getCryptoPrices([
      "bitcoin",
      "ethereum",
      "binancecoin",
      "cardano",
      "solana",
      "ripple",
      "polkadot",
      "dogecoin",
      "avalanche-2",
      "chainlink",
      "polygon",
      "litecoin",
      "uniswap",
      "monero",
      "stellar",
    ]);
    availableCryptos.value = cryptos;
  } catch (error) {
    console.error("Error fetching cryptocurrencies:", error);
  }
};

// Watch for show prop changes to fetch cryptos when modal opens
watch(
  () => props.show,
  (newValue) => {
    if (newValue) {
      fetchAvailableCryptos();
    }
  },
  { immediate: true }
);
</script>

<style scoped>
.custom-scrollbar {
  scrollbar-width: thin;
  scrollbar-color: rgba(156, 163, 175, 0.5) transparent;
}

.custom-scrollbar::-webkit-scrollbar {
  width: 6px;
}

.custom-scrollbar::-webkit-scrollbar-track {
  background: transparent;
}

.custom-scrollbar::-webkit-scrollbar-thumb {
  background-color: rgba(156, 163, 175, 0.5);
  border-radius: 20px;
}

.custom-scrollbar::-webkit-scrollbar-thumb:hover {
  background-color: rgba(156, 163, 175, 0.7);
}
</style>
