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
            Set Financial Goal
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
          <!-- Goal Type Selection -->
          <div>
            <label
              class="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-2"
            >
              Goal Type
            </label>
            <div class="grid grid-cols-2 gap-3">
              <button
                type="button"
                @click="form.type = 'savings'"
                :class="[
                  'flex items-center justify-center gap-2 py-3 px-4 rounded-lg border transition-all',
                  form.type === 'savings'
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
                    d="M3 10h18M7 15h1m4 0h1m-7 4h12a3 3 0 003-3V8a3 3 0 00-3-3H6a3 3 0 00-3 3v8a3 3 0 003 3z"
                  />
                </svg>
                <span>Savings</span>
              </button>
              <button
                type="button"
                @click="form.type = 'investment'"
                :class="[
                  'flex items-center justify-center gap-2 py-3 px-4 rounded-lg border transition-all',
                  form.type === 'investment'
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
                    d="M13 7h8m0 0v8m0-8l-8 8-4-4-6 6"
                  />
                </svg>
                <span>Investment</span>
              </button>
            </div>
          </div>

          <!-- Goal Name -->
          <div>
            <label
              class="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-2"
            >
              Goal Name
            </label>
            <input
              type="text"
              v-model="form.name"
              class="w-full p-3 border rounded-lg dark:bg-gray-700 focus:ring-2 focus:ring-blue-500 focus:border-blue-500"
              :class="{ 'border-red-500': formErrors.name && !form.name }"
              placeholder="e.g., New Car, House Down Payment"
              required
            />
            <p
              v-if="formErrors.name && !form.name"
              class="text-sm text-red-500"
            >
              Goal name is required
            </p>
          </div>

          <!-- Target Amount -->
          <div>
            <label
              class="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-2"
            >
              Target Amount
            </label>
            <div class="relative">
              <span
                class="absolute left-3 top-1/2 -translate-y-1/2 text-gray-500 dark:text-gray-400"
                >$</span
              >
              <input
                type="number"
                v-model="form.targetAmount"
                class="w-full pl-8 p-3 border rounded-lg dark:bg-gray-700 focus:ring-2 focus:ring-blue-500 focus:border-blue-500"
                :class="{
                  'border-red-500':
                    formErrors.targetAmount && !form.targetAmount,
                }"
                placeholder="0.00"
                step="0.01"
                min="0"
                required
              />
            </div>
            <p
              v-if="formErrors.targetAmount && !form.targetAmount"
              class="text-sm text-red-500"
            >
              Target amount is required and must be greater than 0
            </p>
          </div>

          <!-- Current Amount -->
          <div>
            <label
              class="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-2"
            >
              Current Amount
            </label>
            <div class="relative">
              <span
                class="absolute left-3 top-1/2 -translate-y-1/2 text-gray-500 dark:text-gray-400"
                >$</span
              >
              <input
                type="number"
                v-model="form.currentAmount"
                class="w-full pl-8 p-3 border rounded-lg dark:bg-gray-700 focus:ring-2 focus:ring-blue-500 focus:border-blue-500"
                :class="{
                  'border-red-500':
                    formErrors.currentAmount && !form.currentAmount,
                }"
                placeholder="0.00"
                step="0.01"
                min="0"
                required
              />
            </div>
            <p
              v-if="formErrors.currentAmount && !form.currentAmount"
              class="text-sm text-red-500"
            >
              Current amount is required and must be greater than or equal to 0
            </p>
          </div>

          <!-- Deadline -->
          <div>
            <label
              class="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-2"
            >
              Target Date
            </label>
            <input
              type="date"
              v-model="form.deadline"
              class="w-full p-3 border rounded-lg dark:bg-gray-700 focus:ring-2 focus:ring-blue-500 focus:border-blue-500"
              :class="{
                'border-red-500': formErrors.deadline && !form.deadline,
              }"
              :min="today"
              required
            />
            <p
              v-if="formErrors.deadline && !form.deadline"
              class="text-sm text-red-500"
            >
              Target date is required
            </p>
          </div>

          <!-- Priority Selection -->
          <div>
            <label
              class="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-2"
            >
              Priority
            </label>
            <div class="grid grid-cols-3 gap-3">
              <button
                type="button"
                @click="form.priority = 'low'"
                :class="[
                  'flex items-center justify-center gap-2 py-3 px-4 rounded-lg border transition-all',
                  form.priority === 'low'
                    ? 'border-gray-500 bg-gray-50 dark:bg-gray-900/20 text-gray-700 dark:text-gray-400'
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
                    d="M5 12h14"
                  />
                </svg>
                <span>Low</span>
              </button>
              <button
                type="button"
                @click="form.priority = 'medium'"
                :class="[
                  'flex items-center justify-center gap-2 py-3 px-4 rounded-lg border transition-all',
                  form.priority === 'medium'
                    ? 'border-yellow-500 bg-yellow-50 dark:bg-yellow-900/20 text-yellow-700 dark:text-yellow-400'
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
                    d="M5 12h14M12 5v14"
                  />
                </svg>
                <span>Medium</span>
              </button>
              <button
                type="button"
                @click="form.priority = 'high'"
                :class="[
                  'flex items-center justify-center gap-2 py-3 px-4 rounded-lg border transition-all',
                  form.priority === 'high'
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
                    d="M5 12h14M12 5v14"
                  />
                </svg>
                <span>High</span>
              </button>
            </div>
          </div>

          <!-- Notes -->
          <div>
            <label
              class="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-2"
            >
              Notes
            </label>
            <textarea
              v-model="form.notes"
              class="w-full p-3 border rounded-lg dark:bg-gray-700 focus:ring-2 focus:ring-blue-500 focus:border-blue-500"
              rows="3"
              placeholder="Add any additional notes about your goal..."
            ></textarea>
          </div>

          <!-- Submit Button -->
          <div class="pt-4">
            <button
              type="submit"
              class="w-full py-3 px-4 bg-blue-600 hover:bg-blue-700 text-white rounded-lg transition-colors focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-blue-500"
            >
              Set Goal
            </button>
          </div>
        </form>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, computed, watch } from "vue";
import { useToast } from "vue-toastification";
import type { Goal } from "@/store/goals";

const props = defineProps<{
  show: boolean;
  goal?: Goal | null;
}>();

const emit = defineEmits<{
  (e: "close"): void;
  (e: "save", goal: Goal): void;
  (e: "update", goal: Goal): void;
}>();

const toast = useToast();

const form = ref({
  type: "savings" as "savings" | "investment",
  name: "",
  targetAmount: 0,
  currentAmount: 0,
  deadline: "",
  priority: "medium" as "low" | "medium" | "high",
  notes: "",
});

const formErrors = ref({
  name: false,
  targetAmount: false,
  currentAmount: false,
  deadline: false,
  priority: false,
});

const today = computed(() => new Date().toISOString().split("T")[0]);

// Watch for goal prop changes to update form
watch(
  () => props.goal,
  (newGoal) => {
    if (newGoal) {
      form.value = {
        type: newGoal.type,
        name: newGoal.name,
        targetAmount: newGoal.targetAmount,
        currentAmount: newGoal.currentAmount,
        deadline: newGoal.deadline,
        priority: newGoal.priority,
        notes: newGoal.notes,
      };
    }
  },
  { immediate: true }
);

const validateForm = () => {
  let isValid = true;
  formErrors.value = {
    name: false,
    targetAmount: false,
    currentAmount: false,
    deadline: false,
    priority: false,
  };

  if (!form.value.name) {
    formErrors.value.name = true;
    isValid = false;
  }

  if (!form.value.targetAmount || form.value.targetAmount <= 0) {
    formErrors.value.targetAmount = true;
    isValid = false;
  }

  if (form.value.currentAmount < 0) {
    formErrors.value.currentAmount = true;
    isValid = false;
  }

  if (!form.value.deadline) {
    formErrors.value.deadline = true;
    isValid = false;
  }

  if (!form.value.priority) {
    formErrors.value.priority = true;
    isValid = false;
  }

  return isValid;
};

const handleSubmit = () => {
  if (!validateForm()) {
    return;
  }

  const goal: Goal = {
    id: props.goal?.id || Date.now().toString(),
    name: form.value.name,
    type: form.value.type,
    targetAmount: Number(form.value.targetAmount),
    currentAmount: Number(form.value.currentAmount),
    deadline: form.value.deadline,
    priority: form.value.priority,
    notes: form.value.notes,
    createdAt: props.goal?.createdAt || new Date().toISOString(),
  };

  if (props.goal) {
    emit("update", goal);
    toast.success("Goal updated successfully!");
  } else {
    emit("save", goal);
    toast.success("Goal set successfully!");
  }

  resetForm();
  emit("close");
};

const resetForm = () => {
  form.value = {
    type: "savings",
    name: "",
    targetAmount: 0,
    currentAmount: 0,
    deadline: "",
    priority: "medium",
    notes: "",
  };
  formErrors.value = {
    name: false,
    targetAmount: false,
    currentAmount: false,
    deadline: false,
    priority: false,
  };
};

const close = () => {
  resetForm();
  emit("close");
};
</script>
