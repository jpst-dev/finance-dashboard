import axios from "axios";

const API_URL = "https://api.coingecko.com/api/v3";
const PROXY_URL = "https://corsproxy.io/?";
const CACHE_DURATION = 300000; // 5 minutos
const RATE_LIMIT_DELAY = 10000; // 10 segundos entre requisições
const MAX_RETRIES = 3;
const RETRY_DELAY = 2000; // 2 segundos

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

// Lista de criptomoedas suportadas
export const SUPPORTED_CRYPTOS = [
  {
    id: "bitcoin",
    symbol: "BTC",
    name: "Bitcoin",
    image: "https://assets.coingecko.com/coins/images/1/large/bitcoin.png",
  },
  {
    id: "ethereum",
    symbol: "ETH",
    name: "Ethereum",
    image: "https://assets.coingecko.com/coins/images/279/large/ethereum.png",
  },
  {
    id: "binancecoin",
    symbol: "BNB",
    name: "Binance Coin",
    image: "https://assets.coingecko.com/coins/images/825/large/bnb.png",
  },
  {
    id: "cardano",
    symbol: "ADA",
    name: "Cardano",
    image: "https://assets.coingecko.com/coins/images/975/large/cardano.png",
  },
  {
    id: "solana",
    symbol: "SOL",
    name: "Solana",
    image: "https://assets.coingecko.com/coins/images/4128/large/solana.png",
  },
  {
    id: "ripple",
    symbol: "XRP",
    name: "XRP",
    image:
      "https://assets.coingecko.com/coins/images/44/large/xrp-symbol-white-128.png",
  },
  {
    id: "polkadot",
    symbol: "DOT",
    name: "Polkadot",
    image: "https://assets.coingecko.com/coins/images/12171/large/polkadot.png",
  },
  {
    id: "dogecoin",
    symbol: "DOGE",
    name: "Dogecoin",
    image: "https://assets.coingecko.com/coins/images/5/large/dogecoin.png",
  },
  {
    id: "avalanche-2",
    symbol: "AVAX",
    name: "Avalanche",
    image:
      "https://assets.coingecko.com/coins/images/12559/large/Avalanche_Circle_RedWhite_Trans.png",
  },
  {
    id: "chainlink",
    symbol: "LINK",
    name: "Chainlink",
    image:
      "https://assets.coingecko.com/coins/images/877/large/chainlink-new-logo.png",
  },
  {
    id: "polygon",
    symbol: "MATIC",
    name: "Polygon",
    image:
      "https://assets.coingecko.com/coins/images/4713/large/matic-token-icon.png",
  },
  {
    id: "litecoin",
    symbol: "LTC",
    name: "Litecoin",
    image: "https://assets.coingecko.com/coins/images/2/large/litecoin.png",
  },
  {
    id: "uniswap",
    symbol: "UNI",
    name: "Uniswap",
    image:
      "https://assets.coingecko.com/coins/images/12504/large/uniswap-uni.png",
  },
  {
    id: "monero",
    symbol: "XMR",
    name: "Monero",
    image: "https://assets.coingecko.com/coins/images/69/large/monero_logo.png",
  },
  {
    id: "stellar",
    symbol: "XLM",
    name: "Stellar",
    image:
      "https://assets.coingecko.com/coins/images/100/large/Stellar_symbol_black_RGB.png",
  },
];

export const CRYPTO_IMAGES: Record<string, string> = {
  bitcoin: "https://assets.coingecko.com/coins/images/1/large/bitcoin.png",
  ethereum: "https://assets.coingecko.com/coins/images/279/large/ethereum.png",
  binancecoin: "https://assets.coingecko.com/coins/images/825/large/bnb.png",
  cardano: "https://assets.coingecko.com/coins/images/975/large/cardano.png",
  solana: "https://assets.coingecko.com/coins/images/4128/large/solana.png",
  ripple:
    "https://assets.coingecko.com/coins/images/44/large/xrp-symbol-white-128.png",
  polkadot:
    "https://assets.coingecko.com/coins/images/12171/large/polkadot.png",
  dogecoin: "https://assets.coingecko.com/coins/images/5/large/dogecoin.png",
  avalanche:
    "https://assets.coingecko.com/coins/images/12559/large/Avalanche_Circle_RedWhite_Trans.png",
  chainlink:
    "https://assets.coingecko.com/coins/images/877/large/chainlink-new-logo.png",
  polygon:
    "https://assets.coingecko.com/coins/images/4713/large/matic-token-icon.png",
  litecoin: "https://assets.coingecko.com/coins/images/2/large/litecoin.png",
  uniswap:
    "https://assets.coingecko.com/coins/images/12504/large/uniswap-uni.png",
  monero: "https://assets.coingecko.com/coins/images/69/large/monero_logo.png",
  stellar:
    "https://assets.coingecko.com/coins/images/100/large/Stellar_symbol_black_RGB.png",
};

export const SYMBOL_TO_ID: Record<string, string> = {
  BTC: "bitcoin",
  ETH: "ethereum",
  BNB: "binancecoin",
  ADA: "cardano",
  SOL: "solana",
  XRP: "ripple",
  DOT: "polkadot",
  DOGE: "dogecoin",
  AVAX: "avalanche-2",
  LINK: "chainlink",
  MATIC: "polygon",
  LTC: "litecoin",
  UNI: "uniswap",
  XMR: "monero",
  XLM: "stellar",
};

const sleep = (ms: number) => new Promise((resolve) => setTimeout(resolve, ms));

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

const fetchWithRetry = async (
  url: string,
  retries = MAX_RETRIES
): Promise<any> => {
  try {
    const response = await axios.get(url, {
      headers: {
        Accept: "application/json",
      },
    });
    return response.data;
  } catch (error: any) {
    if (retries > 0 && error.response?.status === 429) {
      await sleep(RETRY_DELAY);
      return fetchWithRetry(url, retries - 1);
    }
    throw error;
  }
};

export const getCryptoPrices = async (
  ids: string[]
): Promise<CryptoPrice[]> => {
  try {
    // Primeiro, tenta obter do cache
    const cachedData = getCachedPrices(ids);
    if (cachedData) {
      return cachedData;
    }

    // Filtra IDs inválidos ou vazios
    const validIds = ids.filter((id) => id && id.trim().length > 0);
    if (validIds.length === 0) {
      return [];
    }

    const params = new URLSearchParams({
      vs_currency: "usd",
      ids: validIds.join(","),
      order: "market_cap_desc",
      per_page: "100",
      page: "1",
      sparkline: "false",
      price_change_percentage: "24h",
    });

    const url = `${PROXY_URL}${encodeURIComponent(
      `${API_URL}/coins/markets?${params.toString()}`
    )}`;
    const data = await fetchWithRetry(url);

    if (!data || !Array.isArray(data)) {
      throw new Error("Invalid data received from API");
    }

    const prices = data.map((coin: any) => ({
      symbol: coin.symbol.toUpperCase(),
      name: coin.name,
      current_price: coin.current_price,
      price_change_percentage_24h: coin.price_change_percentage_24h || 0,
      image: coin.image,
    }));

    setCachedPrices(validIds, prices);
    return prices;
  } catch (error) {
    // Se houver erro, retorna as moedas pré-definidas com valores padrão
    console.warn("Error fetching crypto prices, using default values:", error);
    return SUPPORTED_CRYPTOS.map((crypto) => ({
      symbol: crypto.symbol,
      name: crypto.name,
      current_price: 0,
      price_change_percentage_24h: 0,
      image: crypto.image,
    }));
  }
};

export const startPriceUpdates = (
  ids: string[],
  callback: (prices: CryptoPrice[]) => void
) => {
  let isUpdating = false;
  let lastUpdateTime = 0;

  const updatePrices = async () => {
    if (isUpdating || Date.now() - lastUpdateTime < RATE_LIMIT_DELAY) {
      return;
    }

    isUpdating = true;
    lastUpdateTime = Date.now();

    try {
      const prices = await getCryptoPrices(ids);
      if (prices.length > 0) {
        callback(prices);
      }
    } catch (error) {
      console.error("Error updating prices:", error);
    } finally {
      isUpdating = false;
    }
  };

  // Primeira atualização
  updatePrices();

  // Atualizações subsequentes
  const intervalId = setInterval(updatePrices, RATE_LIMIT_DELAY);

  return () => clearInterval(intervalId);
};
