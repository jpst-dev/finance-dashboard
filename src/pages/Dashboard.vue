<template>
  <div class="space-y-8">
    <!-- Summary Cards -->
    <div class="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-4 gap-4">
      <div class="bg-white dark:bg-gray-800 p-6 rounded-lg shadow">
        <h3 class="text-lg font-semibold text-gray-700 dark:text-gray-300">
          {{ t("dashboard.totalBalance") }}
        </h3>
        <p class="text-2xl font-bold">
          {{ formatCurrency(transactionStore.balance) }}
        </p>
        <div class="mt-2 flex items-center text-sm">
          <span
            :class="[
              'font-medium',
              transactionStore.balanceChange >= 0
                ? 'text-green-600'
                : 'text-red-600',
            ]"
          >
            {{ transactionStore.balanceChange >= 0 ? "↑" : "↓" }}
            {{ Math.abs(transactionStore.balanceChange) }}%
          </span>
          <span class="text-gray-500 dark:text-gray-400 ml-2">{{
            t("common.vs_last_month")
          }}</span>
        </div>
      </div>
      <div class="bg-white dark:bg-gray-800 p-6 rounded-lg shadow">
        <h3 class="text-lg font-semibold text-gray-700 dark:text-gray-300">
          {{ t("dashboard.monthlyIncome") }}
        </h3>
        <p class="text-2xl font-bold text-green-600">
          {{ formatCurrency(transactionStore.totalIncome) }}
        </p>
        <div class="mt-2 flex items-center text-sm">
          <span
            :class="[
              'font-medium',
              transactionStore.incomeChange >= 0
                ? 'text-green-600'
                : 'text-red-600',
            ]"
          >
            {{ transactionStore.incomeChange >= 0 ? "↑" : "↓" }}
            {{ Math.abs(transactionStore.incomeChange) }}%
          </span>
          <span class="text-gray-500 dark:text-gray-400 ml-2">{{
            t("common.vs_last_month")
          }}</span>
        </div>
      </div>
      <div class="bg-white dark:bg-gray-800 p-6 rounded-lg shadow">
        <h3 class="text-lg font-semibold text-gray-700 dark:text-gray-300">
          {{ t("dashboard.monthlyExpenses") }}
        </h3>
        <p class="text-2xl font-bold text-red-600">
          {{ formatCurrency(transactionStore.totalExpenses) }}
        </p>
        <div class="mt-2 flex items-center text-sm">
          <span
            :class="[
              'font-medium',
              transactionStore.expensesChange <= 0
                ? 'text-green-600'
                : 'text-red-600',
            ]"
          >
            {{ transactionStore.expensesChange <= 0 ? "↓" : "↑" }}
            {{ Math.abs(transactionStore.expensesChange) }}%
          </span>
          <span class="text-gray-500 dark:text-gray-400 ml-2">{{
            t("common.vs_last_month")
          }}</span>
        </div>
      </div>
      <div class="bg-white dark:bg-gray-800 p-6 rounded-lg shadow">
        <h3 class="text-lg font-semibold text-gray-700 dark:text-gray-300">
          {{ t("dashboard.investments") }}
        </h3>
        <p class="text-2xl font-bold">
          {{ formatCurrency(investmentStore.currentValue || 0) }}
        </p>
        <div class="mt-2 flex items-center text-sm">
          <span
            :class="[
              'font-medium',
              investmentStore.profitPercentage >= 0
                ? 'text-green-600'
                : 'text-red-600',
            ]"
          >
            {{ investmentStore.profitPercentage >= 0 ? "↑" : "↓" }}
            {{ Math.abs(investmentStore.profitPercentage).toFixed(2) }}%
          </span>
          <span class="text-gray-500 dark:text-gray-400 ml-2">{{
            t("common.all_time")
          }}</span>
        </div>
      </div>
    </div>

    <!-- Quick Actions -->
    <div class="grid grid-cols-1 md:grid-cols-4 gap-4">
      <button
        @click="showTransactionModal = true"
        class="bg-white dark:bg-gray-800 p-4 rounded-lg shadow hover:shadow-md transition-shadow flex items-center justify-center space-x-2"
      >
        <svg
          class="w-6 h-6 text-blue-600"
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
        <span class="font-medium">{{
          t("dashboard.quickActions.addTransaction")
        }}</span>
      </button>
      <button
        @click="$router.push('/portfolio')"
        class="bg-white dark:bg-gray-800 p-4 rounded-lg shadow hover:shadow-md transition-shadow flex items-center justify-center space-x-2"
      >
        <svg
          class="w-6 h-6 text-green-600"
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
        <span class="font-medium">{{
          t("dashboard.quickActions.addInvestment")
        }}</span>
      </button>
      <button
        class="bg-white dark:bg-gray-800 p-4 rounded-lg shadow hover:shadow-md transition-shadow flex items-center justify-center space-x-2"
        @click="handleSetGoalClick"
      >
        <svg
          class="w-6 h-6 text-purple-600"
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
        <span class="font-medium">{{
          t("dashboard.quickActions.setGoal")
        }}</span>
      </button>
      <button
        class="bg-white dark:bg-gray-800 p-4 rounded-lg shadow hover:shadow-md transition-shadow flex items-center justify-center space-x-2"
      >
        <svg
          class="w-6 h-6 text-gray-600"
          fill="none"
          stroke="currentColor"
          viewBox="0 0 24 24"
        >
          <path
            stroke-linecap="round"
            stroke-linejoin="round"
            stroke-width="2"
            d="M4 16v1a3 3 0 003 3h10a3 3 0 003-3v-1m-4-4l-4 4m0 0l-4-4m4 4V4"
          />
        </svg>
        <span class="font-medium">{{
          t("dashboard.quickActions.exportReport")
        }}</span>
      </button>
    </div>

    <!-- Charts Section -->
    <div class="grid grid-cols-1 lg:grid-cols-2 gap-8">
      <MonthlyChart />
      <ExpenseChart />
    </div>

    <!-- Recent Transactions and Alerts -->
    <div class="grid grid-cols-1 lg:grid-cols-2 gap-8">
      <TransactionList />
      <div class="bg-white dark:bg-gray-800 p-6 rounded-lg shadow">
        <h3 class="text-lg font-semibold mb-4">
          {{ t("dashboard.alerts.title") }}
        </h3>
        <div class="space-y-4">
          <div v-if="!hasAlerts" class="text-gray-500 dark:text-gray-400">
            {{ t("dashboard.alerts.noAlerts") }}
          </div>
          <div v-else class="space-y-4">
            <div
              v-for="alert in alerts"
              :key="alert.id"
              class="flex items-start space-x-3 p-4 rounded-lg"
              :class="[
                alert.type === 'warning'
                  ? 'bg-yellow-50 dark:bg-yellow-900/20'
                  : 'bg-red-50 dark:bg-red-900/20',
              ]"
            >
              <div
                :class="[
                  'flex-shrink-0 w-6 h-6 rounded-full flex items-center justify-center',
                  alert.type === 'warning'
                    ? 'bg-yellow-100 text-yellow-800 dark:bg-yellow-900 dark:text-yellow-300'
                    : 'bg-red-100 text-red-800 dark:bg-red-900 dark:text-red-300',
                ]"
              >
                <svg
                  class="w-4 h-4"
                  fill="none"
                  stroke="currentColor"
                  viewBox="0 0 24 24"
                >
                  <path
                    stroke-linecap="round"
                    stroke-linejoin="round"
                    stroke-width="2"
                    d="M12 9v2m0 4h.01m-6.938 4h13.856c1.54 0 2.502-1.667 1.732-3L13.732 4c-.77-1.333-2.694-1.333-3.464 0L3.34 16c-.77 1.333.192 3 1.732 3z"
                  />
                </svg>
              </div>
              <div>
                <p class="font-medium">{{ alert.title }}</p>
                <p class="text-sm text-gray-500 dark:text-gray-400">
                  {{ alert.message }}
                </p>
              </div>
            </div>
          </div>
        </div>
      </div>
    </div>

    <!-- Financial Goals Section -->
    <div
      ref="goalsSection"
      class="bg-white dark:bg-gray-800 rounded-xl shadow-sm p-6"
    >
      <div class="flex items-center justify-between mb-6">
        <h2 class="text-lg font-semibold text-gray-900 dark:text-white">
          {{ t("goals.title") }}
        </h2>
        <button
          @click="showGoalModal = true"
          class="inline-flex items-center gap-2 px-4 py-2 text-sm font-medium text-white bg-blue-600 rounded-lg hover:bg-blue-700 focus:outline-none focus:ring-2 focus:ring-blue-500 focus:ring-offset-2 dark:focus:ring-offset-gray-800"
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
          {{ t("goals.addGoal") }}
        </button>
      </div>

      <div v-if="goals.length === 0" class="text-center py-8">
        <svg
          class="mx-auto h-12 w-12 text-gray-400"
          fill="none"
          stroke="currentColor"
          viewBox="0 0 24 24"
        >
          <path
            stroke-linecap="round"
            stroke-linejoin="round"
            stroke-width="2"
            d="M9 12l2 2 4-4m6 2a9 9 0 11-18 0 9 9 0 0118 0z"
          />
        </svg>
        <h3 class="mt-2 text-sm font-medium text-gray-900 dark:text-white">
          {{ t("goals.noGoals") }}
        </h3>
        <p class="mt-1 text-sm text-gray-500 dark:text-gray-400">
          {{ t("goals.getStarted") }}
        </p>
        <div class="mt-6">
          <button
            @click="showGoalModal = true"
            class="inline-flex items-center px-4 py-2 border border-transparent shadow-sm text-sm font-medium rounded-md text-white bg-blue-600 hover:bg-blue-700 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-blue-500"
          >
            {{ t("goals.addGoal") }}
          </button>
        </div>
      </div>

      <div v-else class="space-y-4">
        <div
          v-for="goal in displayedGoals"
          :key="goal.id"
          class="bg-white dark:bg-gray-800 rounded-lg shadow-sm p-6 mb-4 border border-gray-200 dark:border-gray-700"
        >
          <div class="flex flex-col space-y-4">
            <!-- Header with Title and Progress -->
            <div class="flex justify-between items-start">
              <div>
                <h3 class="text-lg font-semibold text-gray-900 dark:text-white">
                  {{ goal.name }}
                </h3>
                <div class="mt-1 flex items-center space-x-2">
                  <span class="text-sm text-gray-500 dark:text-gray-400">
                    {{ t("goals.progress") }}:
                  </span>
                  <span
                    :class="[
                      'text-sm font-medium',
                      (goal.currentAmount / goal.targetAmount) * 100 >= 100
                        ? 'text-green-600 dark:text-green-400'
                        : 'text-blue-600 dark:text-blue-400',
                    ]"
                  >
                    {{
                      ((goal.currentAmount / goal.targetAmount) * 100).toFixed(
                        1
                      )
                    }}%
                  </span>
                </div>
              </div>
              <div class="flex items-center space-x-2">
                <button
                  @click="editGoal(goal)"
                  class="p-1.5 text-blue-500 hover:text-blue-700 dark:text-blue-400 dark:hover:text-blue-300"
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
                  @click="confirmDelete(goal)"
                  class="p-1.5 text-red-500 hover:text-red-700 dark:text-red-400 dark:hover:text-red-300"
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

            <!-- Progress Bar -->
            <div class="w-full bg-gray-200 dark:bg-gray-700 rounded-full h-2.5">
              <div
                class="h-2.5 rounded-full transition-all duration-500"
                :class="[
                  (goal.currentAmount / goal.targetAmount) * 100 >= 100
                    ? 'bg-green-600 dark:bg-green-500'
                    : 'bg-blue-600 dark:bg-blue-500',
                ]"
                :style="{
                  width: `${Math.min(
                    (goal.currentAmount / goal.targetAmount) * 100,
                    100
                  )}%`,
                }"
              ></div>
            </div>

            <!-- Financial Details -->
            <div class="grid grid-cols-2 gap-4">
              <div class="space-y-1">
                <p class="text-sm text-gray-500 dark:text-gray-400">
                  {{ t("goals.targetAmount") }}
                </p>
                <p class="text-lg font-semibold text-gray-900 dark:text-white">
                  {{ formatCurrency(goal.targetAmount) }}
                </p>
              </div>
              <div class="space-y-1">
                <p class="text-sm text-gray-500 dark:text-gray-400">
                  {{ t("goals.currentAmount") }}
                </p>
                <p class="text-lg font-semibold text-gray-900 dark:text-white">
                  {{ formatCurrency(goal.currentAmount) }}
                </p>
              </div>
            </div>

            <!-- Deadline and Notes -->
            <div class="flex flex-col space-y-2">
              <div
                class="flex items-center space-x-2 text-sm text-gray-500 dark:text-gray-400"
              >
                <svg
                  class="w-4 h-4"
                  fill="none"
                  stroke="currentColor"
                  viewBox="0 0 24 24"
                >
                  <path
                    stroke-linecap="round"
                    stroke-linejoin="round"
                    stroke-width="2"
                    d="M8 7V3m8 4V3m-9 8h10M5 21h14a2 2 0 002-2V7a2 2 0 00-2-2H5a2 2 0 00-2 2v12a2 2 0 002 2z"
                  />
                </svg>
                <span
                  >{{ t("goals.deadline") }}:
                  {{ new Date(goal.deadline).toLocaleDateString() }}</span
                >
              </div>
              <div
                class="flex items-center space-x-2 text-sm text-gray-500 dark:text-gray-400"
              >
                <svg
                  class="w-4 h-4"
                  fill="none"
                  stroke="currentColor"
                  viewBox="0 0 24 24"
                >
                  <path
                    stroke-linecap="round"
                    stroke-linejoin="round"
                    stroke-width="2"
                    d="M12 8v4l3 3m6-3a9 9 0 11-18 0 9 9 0 0118 0z"
                  />
                </svg>
                <span
                  >{{ getDaysRemaining(goal.deadline) }}
                  {{ t("goals.days_remaining") }}</span
                >
              </div>
              <div
                v-if="goal.notes"
                class="flex items-start space-x-2 text-sm text-gray-500 dark:text-gray-400"
              >
                <svg
                  class="w-4 h-4 mt-0.5"
                  fill="none"
                  stroke="currentColor"
                  viewBox="0 0 24 24"
                >
                  <path
                    stroke-linecap="round"
                    stroke-linejoin="round"
                    stroke-width="2"
                    d="M7 8h10M7 12h4m1 8l-4-4H5a2 2 0 01-2-2V6a2 2 0 012-2h14a2 2 0 012 2v8a2 2 0 01-2 2h-3l-4 4z"
                  />
                </svg>
                <span>{{ goal.notes }}</span>
              </div>
            </div>
          </div>
        </div>
        <div v-if="showViewMoreGoals" class="flex justify-center mt-4">
          <button
            @click="showAllGoals = !showAllGoals"
            class="flex items-center gap-2 px-4 py-2 text-sm font-medium text-gray-700 dark:text-gray-300 bg-gray-100 dark:bg-gray-700 rounded-lg hover:bg-gray-200 dark:hover:bg-gray-600 transition-colors"
          >
            {{ showAllGoals ? t("goals.showLess") : t("goals.viewMore") }}
            <svg
              class="w-4 h-4 ml-1 transition-transform"
              :class="{ 'rotate-180': showAllGoals }"
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
      </div>
    </div>

    <!-- Transaction Form Modal -->
    <TransactionFormModal
      :show="showTransactionModal"
      @close="showTransactionModal = false"
    />

    <!-- Goal Form Modal -->
    <GoalFormModal
      :show="showGoalModal"
      :goal="selectedGoal"
      @close="closeGoalModal"
      @save="handleSaveGoal"
      @update="handleUpdateGoal"
    />

    <ConfirmationModal
      :show="showDeleteGoalModal"
      :title="t('goals.deleteGoal.title')"
      :message="t('goals.deleteGoal.message')"
      :confirm-button-text="t('goals.deleteGoal.confirmButton')"
      @close="closeDeleteGoalModal"
      @confirm="handleDeleteGoal"
    />
  </div>
</template>

<script setup lang="ts">
import { ref, computed } from "vue";
import { useI18n } from "vue-i18n";
import { useTransactionStore } from "@/store/transactions";
import { useInvestmentStore } from "@/store/investments";
import TransactionList from "@/components/TransactionList.vue";
import MonthlyChart from "@/components/MonthlyChart.vue";
import ExpenseChart from "@/components/ExpenseChart.vue";
import TransactionFormModal from "@/components/TransactionFormModal.vue";
import { useGoalStore } from "@/store/goals";
import GoalFormModal from "@/components/GoalFormModal.vue";
import type { Goal } from "@/store/goals";
import ConfirmationModal from "@/components/ConfirmationModal.vue";
import { useToast } from "vue-toastification";

const { t } = useI18n();
const transactionStore = useTransactionStore();
const investmentStore = useInvestmentStore();
const goalStore = useGoalStore();
const toast = useToast();
const showTransactionModal = ref(false);
const showGoalModal = ref(false);
const showDeleteGoalModal = ref(false);
const goalToDelete = ref<Goal | null>(null);
const selectedGoal = ref<Goal | null>(null);
const showAllGoals = ref(false);
const goalsSection = ref<HTMLElement | null>(null);

const goals = computed(() => goalStore.goals);
const showViewMoreGoals = computed(() => goals.value.length > 2);
const displayedGoals = computed(() =>
  showAllGoals.value ? goals.value : goals.value.slice(0, 2)
);

const alerts = ref([
  {
    id: 1,
    type: "highSpending",
    title: t("dashboard.alerts.highSpending.title"),
    message: t("dashboard.alerts.highSpending.message"),
  },
  {
    id: 2,
    type: "investmentOpportunity",
    title: t("dashboard.alerts.investmentOpportunity.title"),
    message: t("dashboard.alerts.investmentOpportunity.message"),
  },
]);

const hasAlerts = computed(() => alerts.value.length > 0);

const formatCurrency = (value: number) => {
  return new Intl.NumberFormat("en-US", {
    style: "currency",
    currency: "USD",
  }).format(value);
};

const editGoal = (goal: Goal) => {
  selectedGoal.value = goal;
  showGoalModal.value = true;
};

const confirmDelete = (goal: Goal) => {
  goalToDelete.value = goal;
  showDeleteGoalModal.value = true;
};

const handleSaveGoal = (goal: Goal) => {
  goalStore.addGoal(goal);
  toast.success(t("goals.success.created"));
  closeGoalModal();
};

const handleUpdateGoal = (goal: Goal) => {
  goalStore.updateGoal(goal);
  toast.success(t("goals.success.updated"));
  closeGoalModal();
};

const handleDeleteGoal = () => {
  if (goalToDelete.value) {
    goalStore.removeGoal(goalToDelete.value.id);
    toast.success(t("goals.success.deleted"));
    closeDeleteGoalModal();
  }
};

const closeDeleteGoalModal = () => {
  showDeleteGoalModal.value = false;
  goalToDelete.value = null;
};

const closeGoalModal = () => {
  showGoalModal.value = false;
  selectedGoal.value = null;
};

const getDaysRemaining = (deadline: string) => {
  const today = new Date();
  const deadlineDate = new Date(deadline);
  const diffTime = deadlineDate.getTime() - today.getTime();
  return Math.ceil(diffTime / (1000 * 60 * 60 * 24));
};

const handleSetGoalClick = () => {
  showGoalModal.value = true;
  setTimeout(() => {
    goalsSection.value?.scrollIntoView({ behavior: "smooth", block: "start" });
  }, 100);
};
</script>
