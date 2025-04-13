import axios from "axios";

const API_URL = "https://corsproxy.io/?https://api.coingecko.com/api/v3";
const CACHE_DURATION = 60000; // 1 minuto
const RATE_LIMIT_DELAY = 1000; // 1 segundo entre requisições

interface PriceCache {
  data: CryptoPrice[];
  timestamp: number;
}

const priceCache: Record<string, PriceCache> = {};

export interface CryptoPrice {
  symbol: string;
  name: string;
  current_price: number;
  price_change_percentage_24h: number;
  image: string;
}

const CRYPTO_IDS: Record<string, string> = {
  bitcoin: "BTC",
  ethereum: "ETH",
  binancecoin: "BNB",
  cardano: "ADA",
  solana: "SOL",
};

export const CRYPTO_IMAGES: Record<string, string> = {
  bitcoin: "https://assets.coingecko.com/coins/images/1/large/bitcoin.png",
  ethereum: "https://assets.coingecko.com/coins/images/279/large/ethereum.png",
  binancecoin: "https://assets.coingecko.com/coins/images/825/large/bnb.png",
  cardano: "https://assets.coingecko.com/coins/images/975/large/cardano.png",
  solana: "https://assets.coingecko.com/coins/images/4128/large/solana.png",
};

export const SYMBOL_TO_ID: Record<string, string> = {
  BTC: "bitcoin",
  ETH: "ethereum",
  BNB: "binancecoin",
  ADA: "cardano",
  SOL: "solana",
};

const getCachedPrices = (ids: string[]): CryptoPrice[] | null => {
  const cacheKey = ids.sort().join(",");
  const cache = priceCache[cacheKey];

  if (cache && Date.now() - cache.timestamp < CACHE_DURATION) {
    return cache.data;
  }

  return null;
};

const setCachedPrices = (ids: string[], data: CryptoPrice[]) => {
  const cacheKey = ids.sort().join(",");
  priceCache[cacheKey] = {
    data,
    timestamp: Date.now(),
  };
};

export const getCryptoPrices = async (
  ids: string[]
): Promise<CryptoPrice[]> => {
  try {
    const cachedData = getCachedPrices(ids);
    if (cachedData) {
      console.log("Returning cached prices");
      return cachedData;
    }

    console.log("Fetching crypto prices for IDs:", ids);

    // Construct the query parameters
    const params = new URLSearchParams({
      vs_currency: "usd",
      ids: ids.join(","),
      order: "market_cap_desc",
      per_page: "100",
      page: "1",
      sparkline: "false",
      price_change_percentage: "24h",
    });

    // Construct the full URL with the CORS proxy
    const url = `${API_URL}/coins/markets?${params.toString()}`;
    console.log("Request URL:", url);

    const response = await axios.get(url, {
      headers: {
        Accept: "application/json",
      },
    });

    if (!response.data) {
      throw new Error("No data received from API");
    }

    console.log("API Response:", response.data);

    const prices = response.data.map((coin: any) => {
      const price: CryptoPrice = {
        symbol: coin.symbol.toUpperCase(),
        name: coin.name,
        current_price: coin.current_price,
        price_change_percentage_24h: coin.price_change_percentage_24h || 0,
        image: coin.image,
      };
      console.log("Processed coin data:", price);
      return price;
    });

    setCachedPrices(ids, prices);
    return prices;
  } catch (error) {
    console.error("Error fetching crypto prices:", error);
    const cachedData = getCachedPrices(ids);
    if (cachedData) {
      console.log("Returning stale cached data due to error");
      return cachedData;
    }
    throw error;
  }
};

export const startPriceUpdates = (
  ids: string[],
  callback: (prices: CryptoPrice[]) => void
) => {
  console.log("Starting price updates for IDs:", ids);

  let isUpdating = false;
  let lastUpdateTime = 0;

  const updatePrices = async () => {
    // Evitar múltiplas atualizações simultâneas
    if (isUpdating) {
      console.log("Update already in progress, skipping");
      return;
    }

    // Respeitar o rate limiting
    const now = Date.now();
    if (now - lastUpdateTime < RATE_LIMIT_DELAY) {
      console.log("Rate limit delay, skipping update");
      return;
    }

    isUpdating = true;
    lastUpdateTime = now;

    try {
      const prices = await getCryptoPrices(ids);
      console.log("New prices received:", prices);
      callback(prices);
    } catch (error) {
      console.error("Error in price update:", error);
    } finally {
      isUpdating = false;
    }
  };

  // Initial update
  updatePrices();

  // Set up interval for updates
  const intervalId = setInterval(updatePrices, 30000); // Update every 30 seconds

  // Return cleanup function
  return () => {
    console.log("Cleaning up price updates");
    clearInterval(intervalId);
  };
};
