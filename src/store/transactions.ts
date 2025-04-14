import { defineStore } from "pinia";
import { ref, computed } from "vue";

interface Transaction {
  id: string;
  type: "income" | "expense";
  amount: number;
  description: string;
  date: string;
  category: string;
}

const STORAGE_KEY = "finance_dashboard_transactions";

export const useTransactionStore = defineStore("transactions", () => {
  // Load transactions from localStorage
  const loadTransactions = () => {
    const saved = localStorage.getItem(STORAGE_KEY);
    return saved ? JSON.parse(saved) : [];
  };

  const transactions = ref<Transaction[]>(loadTransactions());

  // Save transactions to localStorage whenever they change
  const saveTransactions = () => {
    localStorage.setItem(STORAGE_KEY, JSON.stringify(transactions.value));
  };

  const addTransaction = (transaction: Omit<Transaction, "id">) => {
    const newTransaction = {
      ...transaction,
      id: Date.now().toString(),
    };
    transactions.value.push(newTransaction);
    saveTransactions();
  };

  const removeTransaction = (id: string) => {
    transactions.value = transactions.value.filter((t) => t.id !== id);
    saveTransactions();
  };

  const updateTransaction = (updatedTransaction: Transaction) => {
    const index = transactions.value.findIndex(
      (t) => t.id === updatedTransaction.id
    );
    if (index !== -1) {
      transactions.value[index] = updatedTransaction;
      saveTransactions();
    }
  };

  // Computed properties
  const totalIncome = computed(() => {
    return transactions.value
      .filter((t) => t.type === "income")
      .reduce((sum, t) => sum + t.amount, 0);
  });

  const totalExpenses = computed(() => {
    return transactions.value
      .filter((t) => t.type === "expense")
      .reduce((sum, t) => sum + t.amount, 0);
  });

  const balance = computed(() => {
    return totalIncome.value - totalExpenses.value;
  });

  const recentTransactions = computed(() => {
    return [...transactions.value]
      .sort((a, b) => new Date(b.date).getTime() - new Date(a.date).getTime())
      .slice(0, 5);
  });

  const balanceChange = computed(() => {
    const currentMonth = new Date().getMonth();
    const lastMonth = currentMonth === 0 ? 11 : currentMonth - 1;

    const currentMonthBalance = transactions.value
      .filter((t) => new Date(t.date).getMonth() === currentMonth)
      .reduce(
        (sum, t) => (t.type === "income" ? sum + t.amount : sum - t.amount),
        0
      );

    const lastMonthBalance = transactions.value
      .filter((t) => new Date(t.date).getMonth() === lastMonth)
      .reduce(
        (sum, t) => (t.type === "income" ? sum + t.amount : sum - t.amount),
        0
      );

    if (lastMonthBalance === 0) return 0;
    return ((currentMonthBalance - lastMonthBalance) / lastMonthBalance) * 100;
  });

  const incomeChange = computed(() => {
    const currentMonth = new Date().getMonth();
    const lastMonth = currentMonth === 0 ? 11 : currentMonth - 1;

    const currentMonthIncome = transactions.value
      .filter(
        (t) =>
          t.type === "income" && new Date(t.date).getMonth() === currentMonth
      )
      .reduce((sum, t) => sum + t.amount, 0);

    const lastMonthIncome = transactions.value
      .filter(
        (t) => t.type === "income" && new Date(t.date).getMonth() === lastMonth
      )
      .reduce((sum, t) => sum + t.amount, 0);

    if (lastMonthIncome === 0) return 0;
    return ((currentMonthIncome - lastMonthIncome) / lastMonthIncome) * 100;
  });

  const expensesChange = computed(() => {
    const currentMonth = new Date().getMonth();
    const lastMonth = currentMonth === 0 ? 11 : currentMonth - 1;

    const currentMonthExpenses = transactions.value
      .filter(
        (t) =>
          t.type === "expense" && new Date(t.date).getMonth() === currentMonth
      )
      .reduce((sum, t) => sum + t.amount, 0);

    const lastMonthExpenses = transactions.value
      .filter(
        (t) => t.type === "expense" && new Date(t.date).getMonth() === lastMonth
      )
      .reduce((sum, t) => sum + t.amount, 0);

    if (lastMonthExpenses === 0) return 0;
    return (
      ((currentMonthExpenses - lastMonthExpenses) / lastMonthExpenses) * 100
    );
  });

  return {
    transactions,
    addTransaction,
    removeTransaction,
    updateTransaction,
    totalIncome,
    totalExpenses,
    balance,
    recentTransactions,
    balanceChange,
    incomeChange,
    expensesChange,
  };
});
