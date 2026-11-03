// Fixtures sintéticas do demo estático (geradas pelo backend em backend/tests/FinanceDashboard.Tests/DemoFixtures/; ver a seção "Fixtures do demo" do README).
// Este módulo é o único ponto que importa os JSON; o cast abaixo é coberto por fixtures.test.ts.
import type {
  Account,
  Category,
  CategoryRule,
  Checkpoint,
  CurrentUser,
  ImportBatch,
  ImportPreview,
  LinkResponse,
  Summary,
  Transaction,
} from "../real/api/types";
import accounts from "./fixtures/accounts.json";
import categories from "./fixtures/categories.json";
import checkpoints from "./fixtures/checkpoints.json";
import importDetails from "./fixtures/import-details.json";
import imports from "./fixtures/imports.json";
import links from "./fixtures/links.json";
import meta from "./fixtures/meta.json";
import rules from "./fixtures/rules.json";
import summaries from "./fixtures/summaries.json";
import emptySummary from "./fixtures/summary-empty.json";
import transactions from "./fixtures/transactions.json";

/** Marcador que o script de bundles procura para saber se as fixtures estão (ou não) em um bundle. */
export const FIXTURES_MARKER = "finance-demo-fixtures";

export interface DemoFixtures {
  meta: { generator: string; referenceDate: string; months: string[]; previewBatchId: string; user: CurrentUser };
  accounts: Account[];
  checkpoints: Record<string, Checkpoint[]>;
  categories: Category[];
  transactions: Transaction[];
  summaries: Record<string, Summary>;
  emptySummary: Summary;
  imports: ImportBatch[];
  importDetails: Record<string, ImportPreview>;
  /** Vínculos gerados pelo pareador real, por estado (sem envelope de página: o adaptador pagina). */
  links: { confirmed: LinkResponse[]; suggested: LinkResponse[] };
  /** Regras de categoria geradas pelo serviço real (com `matches`). */
  rules: CategoryRule[];
}

export const DEMO_FIXTURES = {
  meta,
  accounts,
  checkpoints,
  categories,
  transactions,
  summaries,
  emptySummary,
  imports,
  importDetails,
  links,
  rules,
} as unknown as DemoFixtures;
