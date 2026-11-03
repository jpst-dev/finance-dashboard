// Costura entre o app real e a origem dos dados: a implementação HTTP vive em http.ts;
// o demo estático (S1c) implementará a mesma interface.
import type {
  Account,
  AccountUpdate,
  Category,
  Checkpoint,
  ConfirmBody,
  ConfirmResult,
  CurrentUser,
  ImportBatch,
  ImportPreview,
  NewAccount,
  NewCheckpoint,
  NewTransaction,
  Page,
  Summary,
  Transaction,
  TransactionQuery,
  TransactionUpdate,
} from "./types";

export interface ApiClient {
  auth: {
    /** Usuário da sessão, ou `null` quando não há sessão (401). */
    me(): Promise<CurrentUser | null>;
    login(email: string, password: string): Promise<void>;
    logout(): Promise<void>;
  };
  accounts: {
    list(): Promise<Account[]>;
    create(b: NewAccount): Promise<Account>;
    update(id: string, b: AccountUpdate): Promise<Account>;
    checkpoints(id: string): Promise<Checkpoint[]>;
    addCheckpoint(id: string, b: NewCheckpoint): Promise<Checkpoint>;
  };
  categories: {
    list(): Promise<Category[]>;
  };
  transactions: {
    list(q: TransactionQuery): Promise<Page<Transaction>>;
    create(b: NewTransaction): Promise<Transaction>;
    update(id: string, b: TransactionUpdate): Promise<Transaction>;
    remove(id: string): Promise<void>;
  };
  summary: {
    /** `month` no formato `YYYY-MM`. */
    month(month: string, accountId?: string): Promise<Summary>;
  };
  imports: {
    preview(accountId: string, file: File): Promise<ImportPreview>;
    list(): Promise<ImportBatch[]>;
    /** GET /api/imports/{id} devolve o lote com a prévia (superconjunto de ImportBatch). */
    get(id: string): Promise<ImportPreview>;
    confirm(id: string, b: ConfirmBody): Promise<ConfirmResult>;
    undo(id: string): Promise<void>;
  };
}
