<template>
  <div
    v-if="show"
    class="fixed inset-0 bg-black bg-opacity-50 flex items-center justify-center z-50"
  >
    <div
      class="bg-white dark:bg-gray-800 rounded-lg shadow-xl max-w-md w-full mx-4"
    >
      <!-- Header -->
      <div
        class="bg-gray-50 dark:bg-gray-700 px-6 py-4 border-b dark:border-gray-600"
      >
        <div class="flex justify-between items-center">
          <div class="flex items-center space-x-3">
            <img
              v-if="selectedCrypto"
              :src="selectedCrypto.image"
              :alt="selectedCrypto.symbol"
              class="w-8 h-8 rounded-full"
            />
            <h3 class="text-lg font-semibold text-gray-900 dark:text-white">
              {{
                selectedCrypto
                  ? `${
                      selectedCrypto.name
                    } (${selectedCrypto.symbol.toUpperCase()})`
                  : "Add Investment"
              }}
            </h3>
          </div>
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
      <div class="p-6">
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

          <!-- Amount Input -->
          <div class="space-y-2">
            <label
              class="block text-sm font-medium text-gray-700 dark:text-gray-300"
            >
              Quantity
            </label>
            <input
              type="number"
              v-model="form.amount"
              class="w-full p-3 border rounded-lg dark:bg-gray-700 focus:ring-2 focus:ring-blue-500 focus:border-blue-500"
              :class="{ 'border-red-500': formErrors.amount && !form.amount }"
              placeholder="0.00000000"
              step="0.000001"
              min="0"
              required
            />
            <p
              v-if="formErrors.amount && !form.amount"
              class="text-sm text-red-500"
            >
              Amount is required and must be greater than 0
            </p>
            <p
              v-if="selectedCrypto && form.amount"
              class="text-sm text-gray-500 dark:text-gray-400"
            >
              Total Value:
              {{
                formatCurrency(
                  Number(form.amount) * selectedCrypto.current_price
                )
              }}
            </p>
          </div>

          <!-- Price Input -->
          <div class="space-y-2">
            <label
              class="block text-sm font-medium text-gray-700 dark:text-gray-300"
            >
              {{ form.transactionType === "buy" ? "Purchase" : "Sell" }} Price
            </label>
            <input
              type="number"
              v-model="form.purchasePrice"
              class="w-full p-3 border rounded-lg dark:bg-gray-700 focus:ring-2 focus:ring-blue-500 focus:border-blue-500"
              :class="{
                'border-red-500':
                  formErrors.purchasePrice && !form.purchasePrice,
              }"
              placeholder="0.00"
              step="0.01"
              min="0"
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
              class="w-full p-3 border rounded-lg dark:bg-gray-700 focus:ring-2 focus:ring-blue-500 focus:border-blue-500"
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
            >
              {{ form.transactionType === "buy" ? "Buy" : "Sell" }}
              {{ selectedCrypto?.symbol.toUpperCase() }}
            </button>
          </div>
        </form>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, computed, watch } from "vue";
import { useInvestmentStore } from "@/store/investments";
import type { Transaction } from "@/store/investments";
import type { CryptoPrice } from "@/services/cryptoService";
import { useToast } from "vue-toastification";

const props = defineProps<{
  show: boolean;
  selectedCrypto?: CryptoPrice | null;
}>();

const emit = defineEmits<{
  (e: "close"): void;
  (e: "submit", transaction: Transaction): void;
}>();

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

// Watch for selectedCrypto changes and update form
watch(
  () => props.selectedCrypto,
  (newCrypto) => {
    if (newCrypto) {
      form.value = {
        transactionType: "buy",
        type: "crypto",
        symbol: newCrypto.symbol,
        name: newCrypto.name,
        amount: 0,
        purchasePrice: newCrypto.current_price,
        currentPrice: newCrypto.current_price,
        purchaseDate: new Date().toISOString().split("T")[0],
      };
    }
  },
  { immediate: true }
);

// Watch for show prop changes to update form when modal is reopened
watch(
  () => props.show,
  (isShowing) => {
    if (isShowing && props.selectedCrypto) {
      form.value.purchasePrice = props.selectedCrypto.current_price;
      form.value.currentPrice = props.selectedCrypto.current_price;
    }
  }
);

const formErrors = ref({
  amount: false,
  purchasePrice: false,
  purchaseDate: false,
});

const today = computed(() => new Date().toISOString().split("T")[0]);

const formatCurrency = (value: number) => {
  return new Intl.NumberFormat("en-US", {
    style: "currency",
    currency: "USD",
  }).format(value);
};

const validateForm = () => {
  let isValid = true;
  formErrors.value = {
    amount: false,
    purchasePrice: false,
    purchaseDate: false,
  };

  if (!form.value.amount || form.value.amount <= 0) {
    formErrors.value.amount = true;
    isValid = false;
  }

  if (!form.value.purchasePrice || form.value.purchasePrice <= 0) {
    formErrors.value.purchasePrice = true;
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
    return;
  }

  try {
    // Garantir que estamos usando os dados da moeda selecionada
    const cryptoData = props.selectedCrypto || {
      symbol: form.value.symbol,
      name: form.value.name,
      current_price: form.value.purchasePrice,
    };

    const transaction = {
      id: Date.now().toString(),
      symbol: cryptoData.symbol,
      name: cryptoData.name,
      type: form.value.transactionType,
      amount: Number(form.value.amount),
      price: Number(form.value.purchasePrice),
      date: form.value.purchaseDate,
      investmentType: form.value.type,
    };

    if (!investmentStore) {
      throw new Error("Investment store not initialized");
    }

    await investmentStore.addTransaction(transaction);

    toast.success("Investment created successfully!");
    resetForm();
    emit("close");
  } catch (error) {
    console.error("Error creating investment:", error);
    toast.error("Failed to create investment. Please try again.");
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
  formErrors.value = {
    amount: false,
    purchasePrice: false,
    purchaseDate: false,
  };
};

const close = () => {
  resetForm();
  emit("close");
};
</script>
