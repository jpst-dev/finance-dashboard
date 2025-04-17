<template>
  <div
    v-if="show"
    class="fixed inset-0 bg-black bg-opacity-50 flex items-center justify-center z-50"
  >
    <div
      class="bg-white dark:bg-gray-800 rounded-lg shadow-xl max-w-md w-full mx-4"
    >
      <div class="p-6">
        <div class="flex justify-between items-center mb-4">
          <h3 class="text-lg font-semibold text-gray-900 dark:text-white">
            Edit Transaction
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

        <div v-if="error" class="mb-4 p-2 bg-red-100 text-red-700 rounded">
          {{ error }}
        </div>

        <form @submit.prevent="handleSubmit" class="space-y-4">
          <div>
            <label class="block text-sm font-medium mb-1">Type</label>
            <select
              v-model="form.type"
              class="w-full p-2 border rounded dark:bg-gray-700"
            >
              <option value="buy">Buy</option>
              <option value="sell">Sell</option>
            </select>
          </div>

          <div>
            <label class="block text-sm font-medium mb-1">Amount</label>
            <input
              type="number"
              v-model="form.amount"
              class="w-full p-2 border rounded dark:bg-gray-700"
              step="0.000001"
              required
            />
          </div>

          <div>
            <label class="block text-sm font-medium mb-1">Price</label>
            <input
              type="number"
              v-model="form.price"
              class="w-full p-2 border rounded dark:bg-gray-700"
              step="0.01"
              required
            />
          </div>

          <div>
            <label class="block text-sm font-medium mb-1">Date</label>
            <input
              type="datetime-local"
              v-model="form.date"
              class="w-full p-2 border rounded dark:bg-gray-700"
              required
            />
          </div>

          <div class="flex space-x-3">
            <button
              type="submit"
              class="flex-1 bg-blue-600 text-white py-2 px-4 rounded hover:bg-blue-700 transition-colors"
            >
              Save Changes
            </button>
            <button
              type="button"
              @click="close"
              class="flex-1 border border-gray-300 dark:border-gray-600 text-gray-700 dark:text-gray-300 py-2 px-4 rounded hover:bg-gray-50 dark:hover:bg-gray-700 transition-colors"
            >
              Cancel
            </button>
          </div>
        </form>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, watch } from "vue";
import { useToast } from "vue-toastification";
import type { Transaction } from "@/store/investments";

const props = defineProps<{
  show: boolean;
  transaction: Transaction;
}>();

const emit = defineEmits<{
  (e: "close"): void;
  (e: "update", transaction: Transaction): void;
}>();

const toast = useToast();

const form = ref({
  type: "buy" as "buy" | "sell",
  amount: 0,
  price: 0,
  date: "",
});

const error = ref("");

watch(
  () => props.transaction,
  (newTransaction) => {
    if (newTransaction) {
      form.value = {
        type: newTransaction.type,
        amount: newTransaction.amount,
        price: newTransaction.price,
        date: new Date(newTransaction.date).toISOString().slice(0, 16),
      };
      error.value = "";
    }
  },
  { immediate: true }
);

const validateForm = () => {
  if (form.value.amount <= 0) {
    error.value = "Amount must be greater than 0";
    return false;
  }
  if (form.value.price <= 0) {
    error.value = "Price must be greater than 0";
    return false;
  }
  if (!form.value.date) {
    error.value = "Date is required";
    return false;
  }
  return true;
};

const close = () => {
  emit("close");
};

const handleSubmit = async () => {
  if (!validateForm()) {
    return;
  }

  try {
    const updatedTransaction = {
      ...props.transaction,
      type: form.value.type,
      amount: Number(form.value.amount),
      price: Number(form.value.price),
      date: new Date(form.value.date).toISOString(),
    };

    emit("update", updatedTransaction);
    toast.success("Transaction updated successfully!");
    close();
  } catch (error) {
    console.error("Error updating transaction:", error);
    toast.error("Failed to update transaction. Please try again.");
  }
};
</script>
