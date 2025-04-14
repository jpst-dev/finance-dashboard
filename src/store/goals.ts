import { defineStore } from "pinia";
import { ref, computed } from "vue";

export type Goal = {
  id: string;
  name: string;
  type: "savings" | "investment";
  targetAmount: number;
  currentAmount: number;
  deadline: string;
  priority: "low" | "medium" | "high";
  notes: string;
  createdAt: string;
};

const STORAGE_KEY = "finance_dashboard_goals";

export const useGoalStore = defineStore("goals", () => {
  // Load goals from localStorage
  const loadGoals = () => {
    const saved = localStorage.getItem(STORAGE_KEY);
    return saved ? JSON.parse(saved) : [];
  };

  const goals = ref<Goal[]>(loadGoals());

  // Save goals to localStorage whenever they change
  const saveGoals = () => {
    localStorage.setItem(STORAGE_KEY, JSON.stringify(goals.value));
  };

  const addGoal = (goal: Goal) => {
    goals.value.push(goal);
    saveGoals();
  };

  const updateGoal = (updatedGoal: Goal) => {
    const index = goals.value.findIndex((g) => g.id === updatedGoal.id);
    if (index !== -1) {
      goals.value[index] = updatedGoal;
      saveGoals();
    }
  };

  const removeGoal = (id: string) => {
    goals.value = goals.value.filter((g) => g.id !== id);
    saveGoals();
  };

  // Computed properties
  const totalGoals = computed(() => goals.value.length);

  const savingsGoals = computed(() =>
    goals.value.filter((g) => g.type === "savings")
  );

  const investmentGoals = computed(() =>
    goals.value.filter((g) => g.type === "investment")
  );

  const totalTargetAmount = computed(() =>
    goals.value.reduce((sum, g) => sum + g.targetAmount, 0)
  );

  const totalCurrentAmount = computed(() =>
    goals.value.reduce((sum, g) => sum + g.currentAmount, 0)
  );

  const progressPercentage = computed(() => {
    if (totalTargetAmount.value === 0) return 0;
    return (totalCurrentAmount.value / totalTargetAmount.value) * 100;
  });

  const upcomingDeadlines = computed(() => {
    const now = new Date();
    return goals.value
      .filter((g) => new Date(g.deadline) > now)
      .sort(
        (a, b) =>
          new Date(a.deadline).getTime() - new Date(b.deadline).getTime()
      )
      .slice(0, 3);
  });

  return {
    goals,
    addGoal,
    updateGoal,
    removeGoal,
    totalGoals,
    savingsGoals,
    investmentGoals,
    totalTargetAmount,
    totalCurrentAmount,
    progressPercentage,
    upcomingDeadlines,
  };
});
