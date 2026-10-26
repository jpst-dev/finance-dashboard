<template>
  <div class="bg-white dark:bg-gray-800 p-6 rounded-lg shadow">
    <div class="flex justify-between items-center mb-4">
      <h3 class="text-lg font-semibold">
        {{ t("dashboard.charts.recentTransactions") }}
      </h3>
    </div>
    <div class="space-y-4">
      <div
        v-if="transactions.length === 0"
        class="text-gray-500 dark:text-gray-400"
      >
        {{ t("transactions.noTransactions") }}
      </div>
      <div
        v-for="transaction in displayedTransactions"
        :key="transaction.id"
        class="flex items-center justify-between gap-3 p-3 sm:p-4 border rounded-lg dark:border-gray-700"
      >
        <div class="flex min-w-0 flex-1 items-center gap-3">
          <div
            :class="[
              'w-9 h-9 sm:w-10 sm:h-10 shrink-0 rounded-full flex items-center justify-center',
              transaction.type === 'income'
                ? 'bg-green-100 text-green-600 dark:bg-green-900 dark:text-green-300'
                : 'bg-red-100 text-red-600 dark:bg-red-900 dark:text-red-300',
            ]"
          >
            {{ transaction.type === "income" ? "↑" : "↓" }}
          </div>
          <div class="min-w-0 flex-1">
            <p class="truncate font-medium">{{ transaction.description }}</p>
            <p class="truncate text-sm text-gray-500 dark:text-gray-400">
              {{
                t(
                  `transactions.categories.${transaction.category.toLowerCase()}`
                )
              }}
              •
              {{ formatDate(transaction.date) }}
            </p>
          </div>
        </div>
        <div class="flex shrink-0 flex-col items-end gap-0.5 sm:flex-row sm:items-center sm:gap-3">
          <p
            :class="[
              'whitespace-nowrap font-semibold',
              transaction.type === 'income'
                ? 'text-green-600 dark:text-green-400'
                : 'text-red-600 dark:text-red-400',
            ]"
          >
            {{ transaction.type === "income" ? "+" : "-" }}
            {{ formatarMoeda(transaction.amount) }}
          </p>
          <div class="-mr-1.5 flex items-center">
            <button
              @click="editTransaction(transaction)"
              class="p-1.5 text-blue-500 hover:text-blue-700 dark:text-blue-400 dark:hover:text-blue-300"
              :title="t('common.edit')"
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
              @click="confirmDelete(transaction)"
              class="p-1.5 text-red-500 hover:text-red-700 dark:text-red-400 dark:hover:text-red-300"
              :title="t('common.delete')"
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
      </div>
    </div>

    <!-- View More Button -->
    <div
      v-if="showViewMore && transactions.length > 3"
      class="mt-6 flex justify-center"
    >
      <button
        @click="showAll = !showAll"
        class="flex items-center gap-2 px-4 py-2 text-sm font-medium text-gray-700 dark:text-gray-300 bg-gray-100 dark:bg-gray-700 rounded-lg hover:bg-gray-200 dark:hover:bg-gray-600 transition-colors"
      >
        {{ showAll ? t("common.showLess") : t("common.viewMore") }}
        <svg
          class="w-4 h-4 transition-transform duration-300"
          :class="{ 'rotate-180': showAll }"
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

    <EditTransactionModal
      v-if="selectedTransaction"
      :show="showEditModal"
      :transaction="selectedTransaction"
      @close="showEditModal = false"
      @update="handleUpdate"
    />

    <ConfirmationModal
      :show="showDeleteModal"
      :title="t('transactions.delete')"
      :message="
        t('transactions.deleteConfirm', {
          type: transactionToDelete?.type
            ? t(`transactions.type.${transactionToDelete.type}`)
            : '',
          amount: transactionToDelete?.amount,
        })
      "
      :confirm-button-text="t('common.delete')"
      @close="showDeleteModal = false"
      @confirm="handleDelete"
    />
  </div>
</template>

<script setup lang="ts">
import { ref, computed } from "vue";
import { useTransactionStore } from "@/store/transactions";
import { storeToRefs } from "pinia";
import { useToast } from "vue-toastification";
import EditTransactionModal from "./EditTransactionModal.vue";
import ConfirmationModal from "./ConfirmationModal.vue";
import { formatarMoeda } from "@/utils/moeda";
import { useI18n } from "vue-i18n";

const { t, locale } = useI18n();

const transactionStore = useTransactionStore();
const { transactions } = storeToRefs(transactionStore);
const toast = useToast();

const showViewMore = ref(true);
const showAll = ref(false);

// Mais recentes primeiro.
const ordenadas = computed(() =>
  [...transactions.value].sort((a, b) => b.date.localeCompare(a.date))
);
const displayedTransactions = computed(() =>
  showAll.value ? ordenadas.value : ordenadas.value.slice(0, 3)
);

const showEditModal = ref(false);
const showDeleteModal = ref(false);
const selectedTransaction = ref<any>(null);
const transactionToDelete = ref<any>(null);

const formatDate = (dateString: string) => {
  // "2026-10-05" seria lido como UTC e cairia no dia anterior no Brasil.
  const data = dateString.length === 10 ? `${dateString}T12:00:00` : dateString;
  const d = new Date(data);
  // Ano só quando não é o atual: "24 de out." em vez de "24 de out. de 2026".
  return d.toLocaleDateString(locale.value, {
    day: "numeric",
    month: "short",
    ...(d.getFullYear() !== new Date().getFullYear() && { year: "numeric" }),
  });
};

const editTransaction = (transaction: any) => {
  selectedTransaction.value = transaction;
  showEditModal.value = true;
};

const confirmDelete = (transaction: any) => {
  transactionToDelete.value = transaction;
  showDeleteModal.value = true;
};

const handleDelete = async () => {
  if (transactionToDelete.value) {
    try {
      await transactionStore.removeTransaction(transactionToDelete.value.id);
      toast.success("Transaction deleted successfully!");
    } catch (error) {
      console.error("Error deleting transaction:", error);
      toast.error("Failed to delete transaction. Please try again.");
    } finally {
      showDeleteModal.value = false;
      transactionToDelete.value = null;
    }
  }
};

const handleUpdate = async (updatedTransaction: any) => {
  try {
    await transactionStore.updateTransaction(updatedTransaction);
    toast.success("Transaction updated successfully!");
    showEditModal.value = false;
    selectedTransaction.value = null;
  } catch (error) {
    console.error("Error updating transaction:", error);
    toast.error("Failed to update transaction. Please try again.");
  }
};
</script>
