// Dados de exemplo para quem abre o app pela primeira vez (vitrine do portfólio).
import type { Transaction } from "@/store/transactions";
import type { Goal } from "@/store/goals";
import type { Investment, Transaction as Operacao } from "@/store/investments";

export const CHAVES = {
  transacoes: "finance_dashboard_transactions",
  metas: "finance_dashboard_goals",
  investimentos: "investmentStore",
  limpo: "finance_dashboard_cleared",
} as const;

type Lancamento = [dia: number, tipo: Transaction["type"], valor: number, descricao: string, categoria: string];

// Um mês típico; os valores variam um pouco de mês para mês (fator abaixo).
const MES: Lancamento[] = [
  [3, "income", 5200, "Salário", "Salary"],
  [5, "expense", 1450, "Aluguel", "Housing"],
  [8, "expense", 182.4, "Conta de luz e internet", "Utilities"],
  [10, "expense", 386.9, "Supermercado", "Food"],
  [14, "income", 950, "Projeto freelance", "Freelance"],
  [16, "expense", 64.5, "Combustível", "Transportation"],
  [19, "expense", 129.9, "Farmácia e consulta", "Healthcare"],
  [21, "expense", 74.3, "Cinema e jantar", "Entertainment"],
  [24, "expense", 298.6, "Supermercado", "Food"],
  [27, "expense", 215, "Roupas", "Shopping"],
];
const FATOR = [0.94, 1.03, 0.97, 1.06, 0.99, 1.02];

const iso = (d: Date) =>
  `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, "0")}-${String(d.getDate()).padStart(2, "0")}`;
const centavos = (v: number) => Math.round(v * 100) / 100;

const CRIPTO = [
  // Preços aproximados de out. 2026; ao abrir, a CoinGecko substitui pelos atuais.
  { symbol: "BTC", name: "Bitcoin", compras: [[0.035, 61200], [0.02, 72400]], atual: 80360 },
  { symbol: "ETH", name: "Ethereum", compras: [[0.6, 2610], [0.4, 2280]], atual: 2327 },
  { symbol: "SOL", name: "Solana", compras: [[6, 118], [4, 92]], atual: 101.5 },
  { symbol: "ADA", name: "Cardano", compras: [[900, 0.29]], atual: 0.21 },
] as const;

export const gerarExemplo = (hoje: Date) => {
  const transactions: Transaction[] = [];
  for (let i = 5; i >= 0; i--) {
    const fator = FATOR[5 - i];
    for (const [dia, type, valor, description, category] of MES) {
      const data = new Date(hoje.getFullYear(), hoje.getMonth() - i, dia);
      if (data > hoje) continue;
      // Salário não varia; o resto acompanha o fator do mês.
      const amount = centavos(category === "Salary" ? valor : valor * fator);
      transactions.push({ id: `exemplo-${iso(data)}-${dia}-${category}`, type, amount, description, date: iso(data), category });
    }
  }

  const prazo = (meses: number) => iso(new Date(hoje.getFullYear(), hoje.getMonth() + meses, 15));
  const criada = iso(new Date(hoje.getFullYear(), hoje.getMonth() - 5, 2));
  const goals: Goal[] = [
    { id: "exemplo-meta-1", name: "Reserva de emergência", type: "savings", targetAmount: 12000, currentAmount: 9150, deadline: prazo(6), priority: "high", notes: "Seis meses de despesas fixas.", createdAt: criada },
    { id: "exemplo-meta-2", name: "Viagem para o Chile", type: "savings", targetAmount: 4000, currentAmount: 1560, deadline: prazo(9), priority: "medium", notes: "", createdAt: criada },
    { id: "exemplo-meta-3", name: "Carteira de cripto", type: "investment", targetAmount: 8000, currentAmount: 6100, deadline: prazo(12), priority: "low", notes: "Aportes mensais em BTC e ETH.", createdAt: criada },
  ];

  const operacoes: Operacao[] = [];
  const investments: Investment[] = [];
  const cryptoPrices: Record<string, number> = {};
  const priceChanges: Record<string, number> = {};
  CRIPTO.forEach((c, n) => {
    let qtd = 0;
    let custo = 0;
    c.compras.forEach(([amount, price], k) => {
      const data = iso(new Date(hoje.getFullYear(), hoje.getMonth() - 5 + k * 2 + n, 6 + n));
      operacoes.push({ id: `exemplo-op-${c.symbol}-${k}`, symbol: c.symbol, name: c.name, type: "buy", amount, price, date: data, investmentType: "crypto" });
      qtd += amount;
      custo += amount * price;
    });
    investments.push({
      id: `exemplo-inv-${c.symbol}`,
      symbol: c.symbol,
      name: c.name,
      type: "crypto",
      amount: qtd,
      purchasePrice: centavos(custo / qtd),
      currentPrice: c.atual,
      purchaseDate: operacoes.find((o) => o.symbol === c.symbol)!.date,
      lastUpdated: iso(hoje),
    });
    cryptoPrices[c.symbol] = c.atual;
    priceChanges[c.symbol] = [1.8, 2.4, -1.2, -0.6][n];
  });
  operacoes.sort((a, b) => a.date.localeCompare(b.date));

  return { transactions, goals, investments: { investments, cryptoPrices, priceChanges, transactions: operacoes } };
};

const gravar = (storage: Storage, hoje: Date) => {
  const { transactions, goals, investments } = gerarExemplo(hoje);
  storage.setItem(CHAVES.transacoes, JSON.stringify(transactions));
  storage.setItem(CHAVES.metas, JSON.stringify(goals));
  storage.setItem(CHAVES.investimentos, JSON.stringify(investments));
};

// Só semeia um navegador sem nenhum dado e que não pediu para limpar.
export const semearSeVazio = (storage: Storage, hoje = new Date()): boolean => {
  const temAlgo = [CHAVES.transacoes, CHAVES.metas, CHAVES.investimentos, CHAVES.limpo].some((k) => storage.getItem(k) !== null);
  if (temAlgo) return false;
  gravar(storage, hoje);
  return true;
};

export const limparDados = (storage: Storage) => {
  storage.removeItem(CHAVES.transacoes);
  storage.removeItem(CHAVES.metas);
  storage.removeItem(CHAVES.investimentos);
  storage.setItem(CHAVES.limpo, "1");
};

export const restaurarExemplo = (storage: Storage, hoje = new Date()) => {
  storage.removeItem(CHAVES.limpo);
  gravar(storage, hoje);
};
