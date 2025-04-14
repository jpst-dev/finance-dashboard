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
          <h3 class="text-lg font-semibold text-gray-900 dark:text-white">
            Add Transaction
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
                @click="form.type = 'income'"
                :class="[
                  'flex items-center justify-center gap-2 py-3 px-4 rounded-lg border transition-all',
                  form.type === 'income'
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
                <span>Income</span>
              </button>
              <button
                type="button"
                @click="form.type = 'expense'"
                :class="[
                  'flex items-center justify-center gap-2 py-3 px-4 rounded-lg border transition-all',
                  form.type === 'expense'
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
                <span>Expense</span>
              </button>
            </div>
          </div>

          <!-- Amount Input -->
          <div class="space-y-2">
            <label
              class="block text-sm font-medium text-gray-700 dark:text-gray-300"
            >
              Amount
            </label>
            <input
              type="number"
              v-model="form.amount"
              class="w-full p-3 border rounded-lg dark:bg-gray-700 focus:ring-2 focus:ring-blue-500 focus:border-blue-500"
              :class="{ 'border-red-500': formErrors.amount && !form.amount }"
              placeholder="0.00"
              step="0.01"
              min="0"
              required
            />
            <p
              v-if="formErrors.amount && !form.amount"
              class="text-sm text-red-500"
            >
              Amount is required and must be greater than 0
            </p>
          </div>

          <!-- Description Input -->
          <div class="space-y-2">
            <label
              class="block text-sm font-medium text-gray-700 dark:text-gray-300"
            >
              Description
            </label>
            <input
              type="text"
              v-model="form.description"
              class="w-full p-3 border rounded-lg dark:bg-gray-700 focus:ring-2 focus:ring-blue-500 focus:border-blue-500"
              :class="{
                'border-red-500': formErrors.description && !form.description,
              }"
              placeholder="What's this for?"
              required
            />
            <p
              v-if="formErrors.description && !form.description"
              class="text-sm text-red-500"
            >
              Description is required
            </p>
          </div>

          <!-- Category Selection -->
          <div class="space-y-2">
            <label
              class="block text-sm font-medium text-gray-700 dark:text-gray-300"
            >
              Category
            </label>
            <select
              v-model="form.category"
              class="w-full p-3 border rounded-lg dark:bg-gray-700 focus:ring-2 focus:ring-blue-500 focus:border-blue-500"
              :class="{
                'border-red-500': formErrors.category && !form.category,
              }"
              required
            >
              <option value="" disabled>Select a category</option>
              <option
                v-for="category in categories"
                :key="category"
                :value="category"
              >
                {{ category }}
              </option>
            </select>
            <p
              v-if="formErrors.category && !form.category"
              class="text-sm text-red-500"
            >
              Category is required
            </p>
          </div>

          <!-- Date Input -->
          <div class="space-y-2">
            <label
              class="block text-sm font-medium text-gray-700 dark:text-gray-300"
            >
              Date
            </label>
            <input
              type="date"
              v-model="form.date"
              class="w-full p-3 border rounded-lg dark:bg-gray-700 focus:ring-2 focus:ring-blue-500 focus:border-blue-500"
              :class="{ 'border-red-500': formErrors.date && !form.date }"
              :max="today"
              required
            />
            <p
              v-if="formErrors.date && !form.date"
              class="text-sm text-red-500"
            >
              Date is required
            </p>
          </div>

          <!-- Submit Button -->
          <div class="pt-4">
            <button
              type="submit"
              class="w-full py-3 px-4 rounded-lg transition-colors disabled:opacity-50 disabled:cursor-not-allowed focus:outline-none focus:ring-2 focus:ring-offset-2 dark:focus:ring-offset-gray-800"
              :class="[
                form.type === 'income'
                  ? 'bg-green-600 hover:bg-green-700 text-white focus:ring-green-500'
                  : 'bg-red-600 hover:bg-red-700 text-white focus:ring-red-500',
              ]"
            >
              Add {{ form.type === "income" ? "Income" : "Expense" }}
            </button>
          </div>
        </form>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, computed } from "vue";
import { useTransactionStore } from "@/store/transactions";
import { useToast } from "vue-toastification";

const props = defineProps<{
  show: boolean;
}>();

const emit = defineEmits<{
  (e: "close"): void;
}>();

const transactionStore = useTransactionStore();
const toast = useToast();

const form = ref({
  type: "income" as "income" | "expense",
  amount: 0,
  description: "",
  category: "",
  date: new Date().toISOString().split("T")[0],
});

const formErrors = ref({
  amount: false,
  description: false,
  category: false,
  date: false,
});

const categories = [
  "Salary",
  "Freelance",
  "Investment",
  "Gift",
  "Housing",
  "Transportation",
  "Food",
  "Utilities",
  "Healthcare",
  "Entertainment",
  "Shopping",
  "Other",
];

const today = computed(() => new Date().toISOString().split("T")[0]);

const validateForm = () => {
  let isValid = true;
  formErrors.value = {
    amount: false,
    description: false,
    category: false,
    date: false,
  };

  if (!form.value.amount || form.value.amount <= 0) {
    formErrors.value.amount = true;
    isValid = false;
  }

  if (!form.value.description) {
    formErrors.value.description = true;
    isValid = false;
  }

  if (!form.value.category) {
    formErrors.value.category = true;
    isValid = false;
  }

  if (!form.value.date) {
    formErrors.value.date = true;
    isValid = false;
  }

  return isValid;
};

const handleSubmit = async () => {
  if (!validateForm()) {
    return;
  }

  try {
    await transactionStore.addTransaction({
      type: form.value.type,
      amount: Number(form.value.amount),
      description: form.value.description,
      category: form.value.category,
      date: form.value.date,
    });

    toast.success(
      `${
        form.value.type === "income" ? "Income" : "Expense"
      } added successfully!`
    );
    resetForm();
    emit("close");
  } catch (error) {
    console.error("Error adding transaction:", error);
    toast.error("Failed to add transaction. Please try again.");
  }
};

const resetForm = () => {
  form.value = {
    type: "income",
    amount: 0,
    description: "",
    category: "",
    date: new Date().toISOString().split("T")[0],
  };
  formErrors.value = {
    amount: false,
    description: false,
    category: false,
    date: false,
  };
};

const close = () => {
  resetForm();
  emit("close");
};
</script>
