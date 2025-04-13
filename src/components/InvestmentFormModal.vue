<template>
  <div
    v-if="show"
    class="fixed inset-0 bg-black bg-opacity-50 flex items-center justify-center z-50"
  >
    <div
      class="bg-white dark:bg-gray-800 rounded-lg shadow-xl max-w-md w-full mx-4 overflow-hidden"
    >
      <!-- Header -->
      <div
        class="bg-gray-50 dark:bg-gray-700 px-6 py-4 border-b dark:border-gray-600"
      >
        <div class="flex justify-between items-center">
          <h3 class="text-lg font-semibold text-gray-900 dark:text-white">
            {{
              form.transactionType === "buy"
                ? "Add New Investment"
                : "Sell Investment"
            }}
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
      </div>

      <!-- Form Content -->
      <div class="p-6 max-h-[80vh] overflow-y-auto">
        <form @submit.prevent="handleSubmit" class="space-y-6">
          <!-- Transaction Type Selection -->
          <div>
            <label
              class="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-2"
            >
              Transaction Type
            </label>
            <div class="grid grid-cols-2 gap-3">
              <button
                type="button"
                @click="form.transactionType = 'buy'"
                :class="[
                  'flex items-center justify-center gap-2 py-3 px-4 rounded-lg border transition-all',
                  form.transactionType === 'buy'
                    ? 'border-green-500 bg-green-50 dark:bg-green-900/20 text-green-700 dark:text-green-400'
                    : 'border-gray-200 dark:border-gray-600 hover:border-gray-300 dark:hover:border-gray-500 text-gray-700 dark:text-gray-300',
                ]"
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
                    d="M12 6v6m0 0v6m0-6h6m-6 0H6"
                  />
                </svg>
                <span>Buy</span>
              </button>
              <button
                type="button"
                @click="form.transactionType = 'sell'"
                :class="[
                  'flex items-center justify-center gap-2 py-3 px-4 rounded-lg border transition-all',
                  form.transactionType === 'sell'
                    ? 'border-red-500 bg-red-50 dark:bg-red-900/20 text-red-700 dark:text-red-400'
                    : 'border-gray-200 dark:border-gray-600 hover:border-gray-300 dark:hover:border-gray-500 text-gray-700 dark:text-gray-300',
                ]"
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
                    d="M20 12H4"
                  />
                </svg>
                <span>Sell</span>
              </button>
            </div>
          </div>

          <!-- Investment Type Selection -->
          <div>
            <label
              class="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-2"
            >
              Investment Type
            </label>
            <div class="grid grid-cols-2 gap-3">
              <button
                type="button"
                @click="form.type = 'crypto'"
                :class="[
                  'flex items-center justify-center gap-2 py-3 px-4 rounded-lg border transition-all',
                  form.type === 'crypto'
                    ? 'border-blue-500 bg-blue-50 dark:bg-blue-900/20 text-blue-700 dark:text-blue-400'
                    : 'border-gray-200 dark:border-gray-600 hover:border-gray-300 dark:hover:border-gray-500 text-gray-700 dark:text-gray-300',
                ]"
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
                    d="M12 8c-1.657 0-3 .895-3 2s1.343 2 3 2 3 .895 3 2-1.343 2-3 2m0-8c1.11 0 2.08.402 2.599 1M12 8V7m0 1v8m0 0v1m0-1c-1.11 0-2.08-.402-2.599-1M21 12a9 9 0 11-18 0 9 9 0 0118 0z"
                  />
                </svg>
                <span>Crypto</span>
              </button>
              <button
                type="button"
                @click="form.type = 'stock'"
                :class="[
                  'flex items-center justify-center gap-2 py-3 px-4 rounded-lg border transition-all',
                  form.type === 'stock'
                    ? 'border-blue-500 bg-blue-50 dark:bg-blue-900/20 text-blue-700 dark:text-blue-400'
                    : 'border-gray-200 dark:border-gray-600 hover:border-gray-300 dark:hover:border-gray-500 text-gray-700 dark:text-gray-300',
                ]"
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
                    d="M9 19v-6a2 2 0 00-2-2H5a2 2 0 00-2 2v6a2 2 0 002 2h2a2 2 0 002-2zm0 0V9a2 2 0 012-2h2a2 2 0 012 2v10m-6 0a2 2 0 002 2h2a2 2 0 002-2m0 0V5a2 2 0 012-2h2a2 2 0 012 2v14a2 2 0 01-2 2h-2a2 2 0 01-2-2z"
                  />
                </svg>
                <span>Stock</span>
              </button>
            </div>
          </div>

          <!-- Crypto Selection -->
          <div v-if="form.type === 'crypto'" class="space-y-2">
            <label
              class="block text-sm font-medium text-gray-700 dark:text-gray-300"
            >
              Cryptocurrency
            </label>
            <div class="relative">
              <select
                v-model="selectedCrypto"
                @change="updateCryptoDetails"
                class="w-full p-3 pl-12 border rounded-lg dark:bg-gray-700 appearance-none focus:ring-2 focus:ring-blue-500 focus:border-blue-500 dark:focus:ring-blue-400 dark:focus:border-blue-400"
                :class="[
                  { 'border-red-500': formErrors.crypto && !selectedCrypto },
                  { 'text-gray-400': !selectedCrypto },
                ]"
                required
              >
                <option value="" disabled selected>
                  {{
                    isLoading
                      ? "Loading cryptocurrencies..."
                      : "Choose a cryptocurrency"
                  }}
                </option>
                <option
                  v-for="crypto in cryptocurrencies"
                  :key="crypto.symbol"
                  :value="crypto"
                >
                  {{ crypto.symbol.toUpperCase() }} -
                  {{ formatCurrency(crypto.current_price) }}
                </option>
              </select>
              <div
                class="absolute inset-y-0 left-0 pl-3 flex items-center pointer-events-none"
              >
                <img
                  v-if="selectedCrypto"
                  :src="selectedCrypto.image"
                  :alt="selectedCrypto.symbol"
                  class="w-6 h-6 rounded-full"
                />
                <svg
                  v-else
                  class="h-6 w-6 text-gray-400"
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
              <div
                class="absolute inset-y-0 right-0 pr-3 flex items-center pointer-events-none"
              >
                <svg
                  class="h-5 w-5 text-gray-400"
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
              </div>
            </div>
            <p
              v-if="formErrors.crypto && !selectedCrypto"
              class="text-sm text-red-500"
            >
              Please select a cryptocurrency
            </p>
          </div>

          <!-- Stock Symbol Input -->
          <div v-if="form.type === 'stock'" class="space-y-2">
            <label
              class="block text-sm font-medium text-gray-700 dark:text-gray-300"
            >
              Stock Symbol
            </label>
            <input
              type="text"
              v-model="form.symbol"
              class="w-full p-3 border rounded-lg dark:bg-gray-700 focus:ring-2 focus:ring-blue-500 focus:border-blue-500 dark:focus:ring-blue-400 dark:focus:border-blue-400"
              :class="{ 'border-red-500': formErrors.symbol && !form.symbol }"
              placeholder="e.g., AAPL"
              required
            />
            <p
              v-if="formErrors.symbol && !form.symbol"
              class="text-sm text-red-500"
            >
              Stock symbol is required
            </p>
          </div>

          <!-- Amount Input -->
          <div class="space-y-2">
            <label
              class="block text-sm font-medium text-gray-700 dark:text-gray-300"
            >
              {{ form.type === "crypto" ? "Quantity" : "Amount" }}
            </label>
            <input
              type="number"
              v-model="form.amount"
              class="w-full p-3 border rounded-lg dark:bg-gray-700 focus:ring-2 focus:ring-blue-500 focus:border-blue-500 dark:focus:ring-blue-400 dark:focus:border-blue-400"
              :class="{ 'border-red-500': formErrors.amount && !form.amount }"
              :placeholder="form.type === 'crypto' ? '0.00000000' : '0.00'"
              :step="form.type === 'crypto' ? '0.000001' : '0.01'"
              :min="0"
              required
            />
            <p
              v-if="formErrors.amount && !form.amount"
              class="text-sm text-red-500"
            >
              Amount is required and must be greater than 0
            </p>
            <p
              v-if="form.type === 'crypto' && selectedCrypto && form.amount"
              class="text-sm text-gray-500 dark:text-gray-400 mt-1"
            >
              Total Value:
              {{
                formatCurrency(
                  Number(form.amount) * selectedCrypto.current_price
                )
              }}
            </p>
          </div>

          <!-- Purchase/Sell Price Input -->
          <div class="space-y-2">
            <label
              class="block text-sm font-medium text-gray-700 dark:text-gray-300"
            >
              {{ form.transactionType === "buy" ? "Purchase" : "Sell" }} Price
            </label>
            <input
              type="number"
              v-model="form.purchasePrice"
              class="w-full p-3 border rounded-lg dark:bg-gray-700 focus:ring-2 focus:ring-blue-500 focus:border-blue-500 dark:focus:ring-blue-400 dark:focus:border-blue-400"
              :class="{
                'border-red-500':
                  formErrors.purchasePrice && !form.purchasePrice,
              }"
              placeholder="0.00"
              step="0.01"
              :min="0"
              required
            />
            <p
              v-if="formErrors.purchasePrice && !form.purchasePrice"
              class="text-sm text-red-500"
            >
              {{ form.transactionType === "buy" ? "Purchase" : "Sell" }} price
              is required and must be greater than 0
            </p>
          </div>

          <!-- Name Input -->
          <div class="space-y-2">
            <label
              class="block text-sm font-medium text-gray-700 dark:text-gray-300"
            >
              Name
            </label>
            <input
              type="text"
              v-model="form.name"
              class="w-full p-3 border rounded-lg dark:bg-gray-700 focus:ring-2 focus:ring-blue-500 focus:border-blue-500 dark:focus:ring-blue-400 dark:focus:border-blue-400"
              :class="{ 'border-red-500': formErrors.name && !form.name }"
              :placeholder="form.type === 'crypto' ? 'Bitcoin' : 'Apple Inc.'"
              required
            />
            <p
              v-if="formErrors.name && !form.name"
              class="text-sm text-red-500"
            >
              Name is required
            </p>
          </div>

          <!-- Date Input -->
          <div class="space-y-2">
            <label
              class="block text-sm font-medium text-gray-700 dark:text-gray-300"
            >
              {{ form.transactionType === "buy" ? "Purchase" : "Sell" }} Date
            </label>
            <input
              type="date"
              v-model="form.purchaseDate"
              class="w-full p-3 border rounded-lg dark:bg-gray-700 focus:ring-2 focus:ring-blue-500 focus:border-blue-500 dark:focus:ring-blue-400 dark:focus:border-blue-400"
              :class="{
                'border-red-500': formErrors.purchaseDate && !form.purchaseDate,
              }"
              :max="today"
              required
            />
            <p
              v-if="formErrors.purchaseDate && !form.purchaseDate"
              class="text-sm text-red-500"
            >
              {{ form.transactionType === "buy" ? "Purchase" : "Sell" }} date is
              required
            </p>
          </div>

          <!-- Submit Button -->
          <div class="pt-4">
            <button
              type="submit"
              class="w-full py-3 px-4 rounded-lg transition-colors disabled:opacity-50 disabled:cursor-not-allowed focus:outline-none focus:ring-2 focus:ring-offset-2 dark:focus:ring-offset-gray-800"
              :class="[
                form.transactionType === 'buy'
                  ? 'bg-green-600 hover:bg-green-700 text-white focus:ring-green-500'
                  : 'bg-red-600 hover:bg-red-700 text-white focus:ring-red-500',
              ]"
              :disabled="isSubmitting"
            >
              <span
                v-if="isSubmitting"
                class="flex items-center justify-center"
              >
                <svg
                  class="animate-spin -ml-1 mr-2 h-5 w-5 text-white"
                  xmlns="http://www.w3.org/2000/svg"
                  fill="none"
                  viewBox="0 0 24 24"
                >
                  <circle
                    class="opacity-25"
                    cx="12"
                    cy="12"
                    r="10"
                    stroke="currentColor"
                    stroke-width="4"
                  ></circle>
                  <path
                    class="opacity-75"
                    fill="currentColor"
                    d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z"
                  ></path>
                </svg>
                {{
                  form.transactionType === "buy" ? "Adding..." : "Selling..."
                }}
              </span>
              <span v-else>{{
                form.transactionType === "buy"
                  ? "Add Investment"
                  : "Sell Investment"
              }}</span>
            </button>
          </div>
        </form>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, computed } from "vue";
import { useInvestmentStore } from "@/store/investments";
import { getCryptoPrices, CRYPTO_IMAGES } from "@/services/cryptoService";
import type { CryptoPrice } from "@/services/cryptoService";
import { useToast } from "vue-toastification";

const props = defineProps<{
  show: boolean;
}>();

const emit = defineEmits(["close"]);
const investmentStore = useInvestmentStore();
const toast = useToast();

const form = ref({
  transactionType: "buy" as "buy" | "sell",
  type: "crypto" as "crypto" | "stock",
  symbol: "",
  name: "",
  amount: 0,
  purchasePrice: 0,
  currentPrice: 0,
  purchaseDate: new Date().toISOString().split("T")[0],
});

const selectedCrypto = ref<CryptoPrice | null>(null);
const cryptocurrencies = ref<CryptoPrice[]>([]);
const isLoading = ref(true);
const isSubmitting = ref(false);
const formErrors = ref({
  crypto: false,
  symbol: false,
  amount: false,
  purchasePrice: false,
  name: false,
  purchaseDate: false,
});

const today = computed(() => new Date().toISOString().split("T")[0]);

const fetchCryptocurrencies = async () => {
  isLoading.value = true;
  try {
    const cryptoList = await getCryptoPrices([
      "bitcoin",
      "ethereum",
      "binancecoin",
      "cardano",
      "solana",
    ]);
    // Ordenar por preço (maior para menor)
    cryptocurrencies.value = cryptoList.sort(
      (a, b) => b.current_price - a.current_price
    );
  } catch (error) {
    toast.error("Failed to load cryptocurrencies. Please try again.");
  } finally {
    isLoading.value = false;
  }
};

const updateCryptoDetails = () => {
  if (selectedCrypto.value) {
    form.value.symbol = selectedCrypto.value.symbol;
    form.value.purchasePrice = selectedCrypto.value.current_price;
    form.value.currentPrice = selectedCrypto.value.current_price;
    form.value.name = selectedCrypto.value.name;
  }
};

const formatCurrency = (value: number) => {
  return new Intl.NumberFormat("en-US", {
    style: "currency",
    currency: "USD",
  }).format(value);
};

const validateForm = () => {
  let isValid = true;
  formErrors.value = {
    crypto: false,
    symbol: false,
    amount: false,
    purchasePrice: false,
    name: false,
    purchaseDate: false,
  };

  if (form.value.type === "crypto" && !selectedCrypto.value) {
    formErrors.value.crypto = true;
    isValid = false;
  }

  if (form.value.type === "stock" && !form.value.symbol) {
    formErrors.value.symbol = true;
    isValid = false;
  }

  if (!form.value.amount || form.value.amount <= 0) {
    formErrors.value.amount = true;
    isValid = false;
  }

  if (!form.value.purchasePrice || form.value.purchasePrice <= 0) {
    formErrors.value.purchasePrice = true;
    isValid = false;
  }

  if (!form.value.name) {
    formErrors.value.name = true;
    isValid = false;
  }

  if (!form.value.purchaseDate) {
    formErrors.value.purchaseDate = true;
    isValid = false;
  }

  return isValid;
};

const handleSubmit = async () => {
  if (!validateForm()) {
    toast.error("Please fill in all required fields correctly.");
    return;
  }

  isSubmitting.value = true;

  try {
    const transaction = {
      id: Date.now().toString(),
      symbol: form.value.symbol,
      name: form.value.name,
      type: form.value.transactionType,
      amount: Number(form.value.amount),
      price: Number(form.value.purchasePrice),
      date: form.value.purchaseDate,
      investmentType: form.value.type,
    };

    console.log("Submitting transaction:", transaction);

    // Ensure store is initialized
    if (!investmentStore) {
      throw new Error("Investment store not initialized");
    }

    // Await the addTransaction call
    const updatedState = await investmentStore.addTransaction(transaction);
    console.log("Store state after adding transaction:", updatedState);

    toast.success(
      form.value.transactionType === "buy"
        ? "Investment added successfully!"
        : "Investment sold successfully!"
    );
    resetForm();
    emit("close");
  } catch (error) {
    console.error("Error submitting transaction:", error);
    toast.error(
      form.value.transactionType === "buy"
        ? "Failed to add investment. Please try again."
        : "Failed to sell investment. Please try again."
    );
  } finally {
    isSubmitting.value = false;
  }
};

const resetForm = () => {
  form.value = {
    transactionType: "buy",
    type: "crypto",
    symbol: "",
    name: "",
    amount: 0,
    purchasePrice: 0,
    currentPrice: 0,
    purchaseDate: new Date().toISOString().split("T")[0],
  };
  selectedCrypto.value = null;
  formErrors.value = {
    crypto: false,
    symbol: false,
    amount: false,
    purchasePrice: false,
    name: false,
    purchaseDate: false,
  };
};

const close = () => {
  if (!isSubmitting.value) {
    resetForm();
    emit("close");
  }
};

// Fetch cryptocurrencies when the modal is shown
fetchCryptocurrencies();
</script>
