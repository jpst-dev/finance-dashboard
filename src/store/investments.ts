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
      console.log("Store initialized successfully:", state.value);
      return state.value;
    } catch (error) {
      console.error("Error initializing store:", error);
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
      const total = state.value.investments.reduce((total, investment) => {
        const value = investment.amount * investment.purchasePrice;
        console.log("Investment value:", {
          symbol: investment.symbol,
          amount: investment.amount,
          purchasePrice: investment.purchasePrice,
          value,
        });
        return total + (value || 0);
      }, 0);
      console.log("Total invested:", total);
      return total;
    } catch (error) {
      console.error("Error calculating total invested:", error);
      return 0;
    }
  });

  const currentValue = computed(() => {
    try {
      const total = state.value.investments.reduce((total, investment) => {
        const value = investment.amount * investment.currentPrice;
        console.log("Current value for", investment.symbol, ":", {
          amount: investment.amount,
          currentPrice: investment.currentPrice,
          value,
        });
        return total + (value || 0);
      }, 0);
      console.log("Total current value:", total);
      return total;
    } catch (error) {
      console.error("Error calculating current value:", error);
      return 0;
    }
  });

  const totalProfit = computed(() => {
    try {
      const profit = currentValue.value - totalInvested.value;
      console.log("Total profit calculation:", {
        currentValue: currentValue.value,
        totalInvested: totalInvested.value,
        profit,
      });
      return profit;
    } catch (error) {
      console.error("Error calculating total profit:", error);
      return 0;
    }
  });

  const profitPercentage = computed(() => {
    try {
      if (totalInvested.value === 0) return 0;
      const percentage = (totalProfit.value / totalInvested.value) * 100;
      console.log("Profit percentage calculation:", {
        totalProfit: totalProfit.value,
        totalInvested: totalInvested.value,
        percentage,
      });
      return percentage;
    } catch (error) {
      console.error("Error calculating profit percentage:", error);
      return 0;
    }
  });

  const addTransaction = (transaction: Transaction) => {
    try {
      const investment = state.value.investments.find(
        (inv) => inv.symbol === transaction.symbol
      );

      if (investment) {
        // Update existing investment
        if (transaction.type === "buy") {
          const totalAmount = investment.amount + transaction.amount;
          const totalValue =
            investment.amount * investment.purchasePrice +
            transaction.amount * transaction.price;
          investment.purchasePrice = totalValue / totalAmount;
          investment.amount = totalAmount;
        } else {
          investment.amount -= transaction.amount;
        }
      } else if (transaction.type === "buy") {
        // Create new investment
        const newInvestment: Investment = {
          id: Date.now().toString(),
          symbol: transaction.symbol,
          name: transaction.name,
          type: transaction.investmentType,
          amount: transaction.amount,
          purchasePrice: transaction.price,
          currentPrice: transaction.price,
          purchaseDate: transaction.date,
          lastUpdated: new Date().toISOString(),
        };

        // If it's a crypto investment, fetch the current price
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
                  // Initialize price updates after adding the investment
                  initializePriceUpdates();
                }
              })
              .catch((error) => {
                console.error("Error fetching current price:", error);
                // Add investment with purchase price as current price if fetch fails
                newInvestment.currentPrice = transaction.price;
                state.value.investments.push(newInvestment);
                saveState();
                // Initialize price updates even if fetch fails
                initializePriceUpdates();
              });
          } else {
            // Add investment with purchase price as current price if no crypto ID found
            newInvestment.currentPrice = transaction.price;
            state.value.investments.push(newInvestment);
            saveState();
            // Initialize price updates even if no crypto ID found
            initializePriceUpdates();
          }
        } else {
          // For non-crypto investments, use purchase price as current price
          newInvestment.currentPrice = transaction.price;
          state.value.investments.push(newInvestment);
          saveState();
        }
      }

      state.value.transactions.push(transaction);
      saveState();
    } catch (error) {
      console.error("Error adding transaction:", error);
    }
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
      console.log("Updating investment price:", symbol, newPrice);
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
      console.log("Updating prices with data:", prices);

      // Create a new object to store the updates
      const newPriceChanges: Record<string, number> = {};
      const newCryptoPrices: Record<string, number> = {};

      // First, process all prices and store them in our update objects
      prices.forEach((price) => {
        const upperSymbol = price.symbol.toUpperCase();
        console.log("Processing price update for:", upperSymbol, price);

        // Store price changes and current prices
        if (typeof price.price_change_percentage_24h === "number") {
          newPriceChanges[upperSymbol] = price.price_change_percentage_24h;
          console.log(
            "Storing price change for",
            upperSymbol,
            ":",
            price.price_change_percentage_24h
          );
        }

        if (typeof price.current_price === "number") {
          newCryptoPrices[upperSymbol] = price.current_price;
          console.log(
            "Storing current price for",
            upperSymbol,
            ":",
            price.current_price
          );
        }
      });

      // Then update all investments with new prices
      state.value.investments.forEach((investment) => {
        const upperSymbol = investment.symbol.toUpperCase();
        if (
          investment.type === "crypto" &&
          newCryptoPrices[upperSymbol] !== undefined
        ) {
          const oldPrice = investment.currentPrice;
          investment.currentPrice = newCryptoPrices[upperSymbol];
          investment.lastUpdated = new Date().toISOString();
          console.log("Updated investment price:", {
            symbol: investment.symbol,
            oldPrice,
            newPrice: investment.currentPrice,
            priceChange: newPriceChanges[upperSymbol],
          });
        }
      });

      // Finally, update the state with all new prices and changes
      state.value.priceChanges = {
        ...state.value.priceChanges,
        ...newPriceChanges,
      };
      state.value.cryptoPrices = {
        ...state.value.cryptoPrices,
        ...newCryptoPrices,
      };

      console.log("Final state after updates:", {
        priceChanges: state.value.priceChanges,
        cryptoPrices: state.value.cryptoPrices,
        investments: state.value.investments.map((inv) => ({
          symbol: inv.symbol,
          currentPrice: inv.currentPrice,
          lastUpdated: inv.lastUpdated,
        })),
      });

      saveState();
    } catch (error) {
      console.error("Error updating prices:", error);
    }
  };

  const initializePriceUpdates = () => {
    try {
      const cryptoInvestments = state.value.investments.filter(
        (inv) => inv.type === "crypto"
      );
      console.log("Found crypto investments:", cryptoInvestments);

      if (cryptoInvestments.length === 0) {
        console.log("No crypto investments to update");
        return;
      }

      const ids = cryptoInvestments
        .map((inv) => {
          const id = SYMBOL_TO_ID[inv.symbol.toUpperCase()];
          console.log(`Converting symbol ${inv.symbol} to ID:`, id);
          return id;
        })
        .filter(Boolean); // Remove any undefined IDs

      console.log("Starting price updates for IDs:", ids);

      return startPriceUpdates(ids, (prices: CryptoPrice[]) => {
        console.log("Received price updates from API:", prices);
        updatePrices(prices);
      });
    } catch (error) {
      console.error("Error initializing price updates:", error);
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
      console.error("Error updating transaction:", error);
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
      console.error("Error deleting transaction:", error);
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
