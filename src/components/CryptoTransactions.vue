<template>
  <div
    v-if="show"
    class="fixed inset-0 bg-black bg-opacity-50 flex items-center justify-center z-50"
  >
    <div
      class="bg-white dark:bg-gray-800 rounded-lg shadow-xl max-w-4xl w-full mx-4"
    >
      <div class="p-6">
        <div class="flex justify-between items-center mb-6">
          <div class="flex items-center space-x-3">
            <div
              class="w-10 h-10 rounded-full bg-purple-100 dark:bg-purple-900 flex items-center justify-center"
            >
              <span class="text-purple-600 dark:text-purple-300 font-medium">{{
                symbol
              }}</span>
            </div>
            <div>
              <h3 class="text-lg font-semibold text-gray-900 dark:text-white">
                {{ name }} Transactions
              </h3>
              <p class="text-sm text-gray-500">{{ symbol }}</p>
            </div>
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

        <div class="space-y-4">
          <div class="grid grid-cols-2 md:grid-cols-4 gap-4 mb-6">
            <div class="bg-gray-50 dark:bg-gray-700 p-4 rounded-lg">
              <p class="text-sm text-gray-500 dark:text-gray-400">
                Total Quantity
              </p>
              <p class="text-lg font-semibold">
                {{ totalQuantity }} {{ symbol }}
              </p>
            </div>
            <div class="bg-gray-50 dark:bg-gray-700 p-4 rounded-lg">
              <p class="text-sm text-gray-500 dark:text-gray-400">
                Avg Buy Price
              </p>
              <p class="text-lg font-semibold">
                {{ formatCurrency(averageBuyPrice) }}
              </p>
            </div>
            <div class="bg-gray-50 dark:bg-gray-700 p-4 rounded-lg">
              <p class="text-sm text-gray-500 dark:text-gray-400">
                Total Invested
              </p>
              <p class="text-lg font-semibold">
                {{ formatCurrency(totalInvested) }}
              </p>
            </div>
            <div class="bg-gray-50 dark:bg-gray-700 p-4 rounded-lg">
              <p class="text-sm text-gray-500 dark:text-gray-400">
                Current Value
              </p>
              <p class="text-lg font-semibold">
                {{ formatCurrency(currentValue) }}
              </p>
            </div>
          </div>

          <div class="overflow-x-auto">
            <table class="min-w-full">
              <thead class="bg-gray-50 dark:bg-gray-700">
                <tr>
                  <th
                    class="px-6 py-3 text-left text-xs font-medium text-gray-500 dark:text-gray-400 uppercase tracking-wider"
                  >
                    Type
                  </th>
                  <th
                    class="px-6 py-3 text-left text-xs font-medium text-gray-500 dark:text-gray-400 uppercase tracking-wider"
                  >
                    Date
                  </th>
                  <th
                    class="px-6 py-3 text-left text-xs font-medium text-gray-500 dark:text-gray-400 uppercase tracking-wider"
                  >
                    Quantity
                  </th>
                  <th
                    class="px-6 py-3 text-left text-xs font-medium text-gray-500 dark:text-gray-400 uppercase tracking-wider"
                  >
                    Price
                  </th>
                  <th
                    class="px-6 py-3 text-left text-xs font-medium text-gray-500 dark:text-gray-400 uppercase tracking-wider"
                  >
                    Total
                  </th>
                  <th
                    class="px-6 py-3 text-left text-xs font-medium text-gray-500 dark:text-gray-400 uppercase tracking-wider"
                  >
                    Actions
                  </th>
                </tr>
              </thead>
              <tbody class="divide-y divide-gray-200 dark:divide-gray-600">
                <tr
                  v-for="transaction in transactions"
                  :key="transaction.id"
                  class="hover:bg-gray-50 dark:hover:bg-gray-700"
                >
                  <td class="px-6 py-4">
                    <span
                      :class="[
                        'px-2 py-1 text-xs font-medium rounded-full',
                        transaction.type === 'buy'
                          ? 'bg-green-100 text-green-800 dark:bg-green-900 dark:text-green-200'
                          : 'bg-red-100 text-red-800 dark:bg-red-900 dark:text-red-200',
                      ]"
                    >
                      {{ transaction.type === "buy" ? "Buy" : "Sell" }}
                    </span>
                  </td>
                  <td
                    class="px-6 py-4 text-sm text-gray-500 dark:text-gray-400"
                  >
                    {{ formatDate(transaction.date) }}
                  </td>
                  <td class="px-6 py-4">
                    <span
                      :class="
                        transaction.type === 'buy'
                          ? 'text-green-600'
                          : 'text-red-600'
                      "
                    >
                      {{ transaction.type === "buy" ? "+" : "-"
                      }}{{ transaction.amount }}
                    </span>
                  </td>
                  <td class="px-6 py-4">
                    {{ formatCurrency(transaction.price) }}
                  </td>
                  <td class="px-6 py-4">
                    {{ formatCurrency(transaction.amount * transaction.price) }}
                  </td>
                  <td class="px-6 py-4">
                    <div class="flex items-center space-x-2">
                      <button
                        @click="editTransaction(transaction)"
                        class="text-blue-600 hover:text-blue-800 dark:text-blue-400 dark:hover:text-blue-300"
                        title="Edit transaction"
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
                            d="M11 5H6a2 2 0 00-2 2v11a2 2 0 002 2h11a2 2 0 002-2v-5m-1.414-9.414a2 2 0 112.828 2.828L11.828 15H9v-2.828l8.586-8.586z"
                          />
                        </svg>
                      </button>
                      <button
                        @click="deleteTransaction(transaction)"
                        class="text-red-600 hover:text-red-800 dark:text-red-400 dark:hover:text-red-300"
                        title="Delete transaction"
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
                  </td>
                </tr>
              </tbody>
            </table>
          </div>
        </div>
      </div>
    </div>
  </div>

  <EditTransactionModal
    v-if="selectedTransaction"
    :show="showEditModal"
    :transaction="selectedTransaction"
    @close="closeEditModal"
    @update="updateTransaction"
  />
</template>

<script setup lang="ts">
import { computed, ref } from "vue";
import EditTransactionModal from "./EditTransactionModal.vue";
import { useInvestmentStore } from "@/store/investments";

const investmentStore = useInvestmentStore();

const props = defineProps<{
  show: boolean;
  symbol: string;
  name: string;
  transactions: Array<{
    id: string;
    type: "buy" | "sell";
    amount: number;
    price: number;
    date: string;
  }>;
  currentPrice: number;
}>();

const emit = defineEmits(["close", "update"]);

const totalQuantity = computed(() => {
  if (!props.transactions?.length) return 0;
  return props.transactions.reduce((sum, t) => {
    return sum + (t.type === "buy" ? t.amount : -t.amount);
  }, 0);
});

const totalInvested = computed(() => {
  if (!props.transactions?.length) return 0;
  return props.transactions.reduce((sum, t) => {
    return sum + (t.type === "buy" ? t.amount * t.price : 0);
  }, 0);
});

const averageBuyPrice = computed(() => {
  if (!props.transactions?.length) return 0;
  const buyTransactions = props.transactions.filter((t) => t.type === "buy");
  if (!buyTransactions.length) return 0;
  return (
    totalInvested.value / buyTransactions.reduce((sum, t) => sum + t.amount, 0)
  );
});

const currentValue = computed(() => {
  return totalQuantity.value * (props.currentPrice || 0);
});

const formatCurrency = (value: number) => {
  return new Intl.NumberFormat("en-US", {
    style: "currency",
    currency: "USD",
  }).format(value);
};

const formatDate = (date: string) => {
  return new Date(date).toLocaleDateString("en-US", {
    year: "numeric",
    month: "short",
    day: "numeric",
  });
};

const selectedTransaction = ref(null);
const showEditModal = ref(false);

const editTransaction = (transaction: any) => {
  selectedTransaction.value = {
    ...transaction,
    symbol: props.symbol,
    name: props.name,
  };
  showEditModal.value = true;
};

const closeEditModal = () => {
  showEditModal.value = false;
  selectedTransaction.value = null;
};

const updateTransaction = async (updatedTransaction: any) => {
  try {
    await investmentStore.updateTransaction(updatedTransaction);
    emit("update");
  } catch (error) {
    console.error("Error updating transaction:", error);
  }
};

const deleteTransaction = async (transaction: any) => {
  if (confirm("Are you sure you want to delete this transaction?")) {
    try {
      await investmentStore.deleteTransaction(transaction.id);
      emit("update");
    } catch (error) {
      console.error("Error deleting transaction:", error);
    }
  }
};

const close = () => {
  emit("close");
};
</script>
