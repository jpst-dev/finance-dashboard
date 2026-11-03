// Tipos de domínio do modo real, derivados do esquema GERADO (schema.d.ts); nada de formato escrito à mão,
// exceto onde o OpenAPI não descreve o corpo (auth: csrf/me), indicado abaixo.
// Dinheiro é sempre string decimal ("-1234.56"): nunca converter para number fora do desenho de gráficos.
import type { components, operations } from "./schema";

type S = components["schemas"];

/**
 * Inteiros: o OpenAPI do .NET gera `number | string` para int32 porque o serializador ACEITA ler números em
 * texto (JsonSerializerDefaults.Web → AllowReadingFromString). Na ESCRITA o servidor sempre envia número JSON
 * (os testes do backend leem esses campos com JsonElement.GetInt32(), que falha em string). Este é o ÚNICO lugar
 * onde estreitamos esses campos para `number`; os campos de dinheiro (string) não são tocados.
 */
type IntFields<T, K extends keyof T> = Omit<T, K> & { [P in K]: number };

export type AccountType = S["AccountType"];
export type CategoryKind = S["CategoryKind"];
export type TransactionState = S["TransactionState"];
export type TransactionOrigin = S["TransactionOrigin"];
export type CheckpointOrigin = S["CheckpointOrigin"];
export type ImportSource = S["ImportSource"];
export type ImportBatchState = S["ImportBatchState"];

export type Account = S["AccountResponse"];
export type NewAccount = S["CreateAccountRequest"];
export type AccountUpdate = S["UpdateAccountRequest"];

export type Category = S["CategoryResponse"];

export type Checkpoint = S["CheckpointResponse"];
/** Derivado do esquema gerado; o OpenAPI marca os campos como anuláveis, mas o cliente sempre os envia preenchidos. */
export type NewCheckpoint = { [K in keyof S["CreateCheckpointRequest"]]-?: NonNullable<S["CreateCheckpointRequest"][K]> };

export type Transaction = S["TransactionResponse"];
export type NewTransaction = S["CreateTransactionRequest"];
export type TransactionUpdate = S["UpdateTransactionRequest"];

/** Filtros de GET /api/transactions; `page`/`size` estreitados para number (ver IntFields). */
export type TransactionQuery = Omit<NonNullable<operations["ListTransactions"]["parameters"]["query"]>, "page" | "size"> & {
  page?: number;
  size?: number;
};

/** Página genérica: forma de S["TransactionPage"] com os inteiros estreitados (ver IntFields). */
export type Page<T> = Omit<IntFields<S["TransactionPage"], "page" | "size" | "total">, "items"> & { items: T[] };

export type CategoryTotal = S["CategoryTotalResponse"];
export type PendingSummary = IntFields<S["PendingSummary"], "count">;
export type Summary = Omit<S["SummaryResponse"], "pending"> & { pending: PendingSummary };

export type ImportWarning = S["ImportWarning"];
export type ImportLine = S["ImportLineView"];
export type ImportCheckpoint = S["ImportCheckpointView"];
export type ImportLineOverride = S["ImportLineOverride"];
export type ImportBatch = IntFields<S["ImportBatchSummary"], "newCount" | "existingCount">;
export type ImportPreview = IntFields<S["ImportPreviewView"], "newCount" | "existingCount" | "editedCount">;
export type ConfirmBody = S["ConfirmImportRequest"];
export type ConfirmResult = IntFields<S["ConfirmResult"], "created" | "skipped">;
/** Corpo do 409 `blocking_warnings` do confirm. */
export type BlockingWarnings = S["BlockingWarningsResponse"];

// Auth: o OpenAPI não descreve estes corpos (objetos anônimos em backend/src/FinanceDashboard.Api/Auth/AuthEndpoints.cs).
/** `me()` só devolve este objeto com e-mail não vazio; resposta sem e-mail é tratada como resposta inválida. */
export type CurrentUser = { email: string };
export type CsrfTokenResponse = { token: string };
export type LoginBody = S["LoginRequest"];
