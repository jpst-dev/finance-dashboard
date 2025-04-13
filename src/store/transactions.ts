import { defineStore } from "pinia";

interface Transaction {
  id: string;
  type: "income" | "expense";
  amount: number;
  description: string;
  date: string;
  category: string;
}

interface TransactionState {
  transactions: Transaction[];
}

export const useTransactionStore = defineStore("transactions", {
  state: (): TransactionState => ({
    transactions: [
      {
        id: "1",
        type: "income",
        amount: 5000,
        description: "Salary",
        date: "2024-03-01",
        category: "Salary",
      },
      {
        id: "2",
        type: "expense",
        amount: 1500,
        description: "Rent",
        date: "2024-03-01",
        category: "Housing",
      },
    ],
  }),

  getters: {
    totalIncome: (state) =>
      state.transactions
        .filter((t) => t.type === "income")
        .reduce((sum, t) => sum + t.amount, 0),

    totalExpenses: (state) =>
      state.transactions
        .filter((t) => t.type === "expense")
        .reduce((sum, t) => sum + t.amount, 0),

    balance: (state) =>
      state.transactions.reduce(
        (sum, t) => (t.type === "income" ? sum + t.amount : sum - t.amount),
        0
      ),

    recentTransactions: (state) =>
      [...state.transactions]
        .sort((a, b) => new Date(b.date).getTime() - new Date(a.date).getTime())
        .slice(0, 5),
  },

  actions: {
    addTransaction(transaction: Omit<Transaction, "id">) {
      this.transactions.push({
        ...transaction,
        id: Date.now().toString(),
      });
    },

    removeTransaction(id: string) {
      this.transactions = this.transactions.filter((t) => t.id !== id);
    },
  },
});
