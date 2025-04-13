import { defineStore } from "pinia";
import { ref, computed } from "vue";
import {
  startPriceUpdates,
  getCryptoPrices,
  SYMBOL_TO_ID,
} from "@/services/cryptoService";

export interface Investment {
  id: string;
  symbol: string;
  name: string;
  type: "crypto" | "stock";
  amount: number;
  purchasePrice: number;
  currentPrice: number;
  purchaseDate: string;
  lastUpdated: string;
}

export interface Transaction {
  id: string;
  symbol: string;
  name: string;
  type: "buy" | "sell";
  amount: number;
  price: number;
  date: string;
  investmentType: "crypto" | "stock";
}

interface InvestmentState {
  investments: Investment[];
  cryptoPrices: Record<string, number>;
  priceChanges: Record<string, number>;
  transactions: Transaction[];
}

interface CryptoPrice {
  symbol: string;
  current_price: number;
  price_change_percentage_24h: number;
}

// Load initial state from localStorage
const loadState = (): InvestmentState => {
  const savedState = localStorage.getItem("investmentStore");
  if (savedState) {
    return JSON.parse(savedState);
  }
  return {
    investments: [],
    cryptoPrices: {},
    priceChanges: {},
    transactions: [],
  };
};

export const useInvestmentStore = defineStore("investments", () => {
  const state = ref<InvestmentState>(loadState());

  // Initialize state on store creation
  const initializeStore = async () => {
    try {
      const savedState = localStorage.getItem("investmentStore");
      if (savedState) {
        const parsedState = JSON.parse(savedState);
        state.value = {
          ...state.value,
          ...parsedState,
        };
      }
      return state.value;
    } catch (error) {
      state.value = {
        investments: [],
        cryptoPrices: {},
        priceChanges: {},
        transactions: [],
      };
      return state.value;
    }
  };

  // Call initializeStore when store is created
  initializeStore();

  const totalInvested = computed(() => {
    try {
      return state.value.investments.reduce((total, investment) => {
        return total + (investment.amount * investment.purchasePrice || 0);
      }, 0);
    } catch (error) {
      return 0;
    }
  });

  const currentValue = computed(() => {
    try {
      return state.value.investments.reduce((total, investment) => {
        return total + (investment.amount * investment.currentPrice || 0);
      }, 0);
    } catch (error) {
      return 0;
    }
  });

  const totalProfit = computed(() => {
    try {
      return currentValue.value - totalInvested.value;
    } catch (error) {
      return 0;
    }
  });

  const profitPercentage = computed(() => {
    try {
      if (totalInvested.value === 0) return 0;
      return (totalProfit.value / totalInvested.value) * 100;
    } catch (error) {
      return 0;
    }
  });

  const addTransaction = async (transaction: Transaction) => {
    const investment = state.value.investments.find(
      (i) => i.symbol === transaction.symbol
    );

    if (investment) {
      // Atualizar investimento existente
      const updatedInvestment = {
        ...investment,
        amount:
          transaction.type === "buy"
            ? investment.amount + transaction.amount
            : investment.amount - transaction.amount,
        purchasePrice:
          transaction.type === "buy"
            ? (investment.purchasePrice * investment.amount +
                transaction.price * transaction.amount) /
              (investment.amount + transaction.amount)
            : investment.purchasePrice,
        // Manter os dados da moeda
        symbol: transaction.symbol,
        name: transaction.name,
      };

      state.value.investments = state.value.investments.map((i) =>
        i.id === investment.id ? updatedInvestment : i
      );
    } else if (transaction.type === "buy") {
      // Criar novo investimento
      const newInvestment: Investment = {
        id: Date.now().toString(),
        symbol: transaction.symbol,
        name: transaction.name,
        amount: transaction.amount,
        purchasePrice: transaction.price,
        currentPrice: transaction.price,
        purchaseDate: transaction.date,
        type: transaction.investmentType,
        lastUpdated: new Date().toISOString(),
      };

      if (transaction.investmentType === "crypto") {
        const cryptoId = SYMBOL_TO_ID[transaction.symbol.toUpperCase()];
        if (cryptoId) {
          getCryptoPrices([cryptoId])
            .then((prices) => {
              const price = prices.find(
                (p) =>
                  p.symbol.toLowerCase() === transaction.symbol.toLowerCase()
              );
              if (price) {
                newInvestment.currentPrice = price.current_price;
                newInvestment.lastUpdated = new Date().toISOString();
                state.value.investments.push(newInvestment);
                saveState();
                initializePriceUpdates();
              }
            })
            .catch(() => {
              newInvestment.currentPrice = transaction.price;
              state.value.investments.push(newInvestment);
              saveState();
              initializePriceUpdates();
            });
        } else {
          newInvestment.currentPrice = transaction.price;
          state.value.investments.push(newInvestment);
          saveState();
          initializePriceUpdates();
        }
      } else {
        newInvestment.currentPrice = transaction.price;
        state.value.investments.push(newInvestment);
        saveState();
      }
    }

    state.value.transactions.push(transaction);
    saveState();
  };

  const removeInvestment = (id: string) => {
    const investment = state.value.investments.find((inv) => inv.id === id);
    if (investment) {
      // Remove the investment
      state.value.investments = state.value.investments.filter(
        (inv) => inv.id !== id
      );

      // Remove all transactions associated with this investment's symbol
      state.value.transactions = state.value.transactions.filter(
        (t) => t.symbol !== investment.symbol
      );

      saveState();
    }
  };

  const updateInvestmentPrice = (symbol: string, newPrice: number) => {
    const investment = state.value.investments.find(
      (inv) => inv.symbol === symbol
    );
    if (investment) {
      investment.currentPrice = newPrice;
      investment.lastUpdated = new Date().toISOString();
    }
  };

  const getInvestmentHistory = (symbol: string) => {
    return state.value.transactions
      .filter((t) => t.symbol === symbol)
      .sort((a, b) => new Date(a.date).getTime() - new Date(b.date).getTime());
  };

  const getPortfolioHistory = () => {
    const history: { date: string; value: number }[] = [];
    const dates = new Set<string>();

    // Collect all unique dates from transactions
    state.value.transactions.forEach((t) => dates.add(t.date));

    // Sort dates chronologically
    const sortedDates = Array.from(dates).sort(
      (a, b) => new Date(a).getTime() - new Date(b).getTime()
    );

    // Calculate portfolio value for each date
    sortedDates.forEach((date) => {
      const transactionsUpToDate = state.value.transactions.filter(
        (t) => new Date(t.date).getTime() <= new Date(date).getTime()
      );

      const investmentsAtDate = new Map<
        string,
        { amount: number; price: number }
      >();

      transactionsUpToDate.forEach((t) => {
        const existing = investmentsAtDate.get(t.symbol);
        if (t.type === "buy") {
          if (existing) {
            existing.amount += t.amount;
            existing.price = t.price;
          } else {
            investmentsAtDate.set(t.symbol, {
              amount: t.amount,
              price: t.price,
            });
          }
        } else {
          if (existing) {
            existing.amount -= t.amount;
          }
        }
      });

      const portfolioValue = Array.from(investmentsAtDate.entries()).reduce(
        (total, [symbol, { amount, price }]) => {
          return total + amount * price;
        },
        0
      );

      history.push({ date, value: portfolioValue });
    });

    return history;
  };

  const saveState = () => {
    localStorage.setItem("investmentStore", JSON.stringify(state.value));
  };

  const updatePrices = (prices: CryptoPrice[]) => {
    try {
      const newPriceChanges: Record<string, number> = {};
      const newCryptoPrices: Record<string, number> = {};

      prices.forEach((price) => {
        const upperSymbol = price.symbol.toUpperCase();
        if (typeof price.price_change_percentage_24h === "number") {
          newPriceChanges[upperSymbol] = price.price_change_percentage_24h;
        }
        if (typeof price.current_price === "number") {
          newCryptoPrices[upperSymbol] = price.current_price;
        }
      });

      state.value.investments.forEach((investment) => {
        const upperSymbol = investment.symbol.toUpperCase();
        if (
          investment.type === "crypto" &&
          newCryptoPrices[upperSymbol] !== undefined
        ) {
          investment.currentPrice = newCryptoPrices[upperSymbol];
          investment.lastUpdated = new Date().toISOString();
        }
      });

      state.value.priceChanges = {
        ...state.value.priceChanges,
        ...newPriceChanges,
      };
      state.value.cryptoPrices = {
        ...state.value.cryptoPrices,
        ...newCryptoPrices,
      };

      saveState();
    } catch (error) {
      // Silently fail
    }
  };

  const initializePriceUpdates = () => {
    try {
      const cryptoInvestments = state.value.investments.filter(
        (inv) => inv.type === "crypto"
      );

      if (cryptoInvestments.length === 0) {
        return;
      }

      const ids = cryptoInvestments
        .map((inv) => SYMBOL_TO_ID[inv.symbol.toUpperCase()])
        .filter(Boolean);

      return startPriceUpdates(ids, (prices: CryptoPrice[]) => {
        updatePrices(prices);
      });
    } catch (error) {
      // Silently fail
    }
  };

  const updateTransaction = async (updatedTransaction: Transaction) => {
    try {
      const index = state.value.transactions.findIndex(
        (t) => t.id === updatedTransaction.id
      );
      if (index === -1) {
        throw new Error("Transaction not found");
      }

      state.value.transactions[index] = updatedTransaction;
      saveState();

      // Update investment if needed
      const investment = state.value.investments.find(
        (inv) => inv.symbol === updatedTransaction.symbol
      );
      if (investment) {
        const transactions = state.value.transactions.filter(
          (t) => t.symbol === updatedTransaction.symbol
        );
        const totalAmount = transactions.reduce(
          (sum, t) => sum + (t.type === "buy" ? t.amount : -t.amount),
          0
        );
        const totalInvested = transactions.reduce(
          (sum, t) => sum + (t.type === "buy" ? t.amount * t.price : 0),
          0
        );

        investment.amount = totalAmount;
        investment.purchasePrice =
          totalAmount > 0 ? totalInvested / totalAmount : 0;
        investment.lastUpdated = new Date().toISOString();
      }

      return state.value;
    } catch (error) {
      throw error;
    }
  };

  const deleteTransaction = async (id: string) => {
    try {
      const transaction = state.value.transactions.find((t) => t.id === id);
      if (!transaction) {
        throw new Error("Transaction not found");
      }

      state.value.transactions = state.value.transactions.filter(
        (t) => t.id !== id
      );
      saveState();

      // Update investment if needed
      const investment = state.value.investments.find(
        (inv) => inv.symbol === transaction.symbol
      );
      if (investment) {
        const transactions = state.value.transactions.filter(
          (t) => t.symbol === transaction.symbol
        );
        const totalAmount = transactions.reduce(
          (sum, t) => sum + (t.type === "buy" ? t.amount : -t.amount),
          0
        );
        const totalInvested = transactions.reduce(
          (sum, t) => sum + (t.type === "buy" ? t.amount * t.price : 0),
          0
        );

        if (totalAmount === 0) {
          state.value.investments = state.value.investments.filter(
            (inv) => inv.symbol !== transaction.symbol
          );
        } else {
          investment.amount = totalAmount;
          investment.purchasePrice = totalInvested / totalAmount;
          investment.lastUpdated = new Date().toISOString();
        }
      }

      return state.value;
    } catch (error) {
      throw error;
    }
  };

  return {
    state,
    investments: computed(() => state.value.investments),
    transactions: computed(() => state.value.transactions),
    cryptoPrices: computed(() => state.value.cryptoPrices),
    priceChanges: computed(() => state.value.priceChanges),
    totalInvested,
    currentValue,
    totalProfit,
    profitPercentage,
    addTransaction,
    removeInvestment,
    updateInvestmentPrice,
    getInvestmentHistory,
    getPortfolioHistory,
    initializePriceUpdates,
    initializeStore,
    updateTransaction,
    deleteTransaction,
  };
});
