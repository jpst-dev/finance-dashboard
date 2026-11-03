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
export type CardKind = S["CardKind"];
export type ImportBatchState = S["ImportBatchState"];

export type Account = S["AccountResponse"];
export type NewAccount = S["CreateAccountRequest"];
export type AccountUpdate = S["UpdateAccountRequest"];

export type Category = S["CategoryResponse"];

export type Checkpoint = S["CheckpointResponse"];
/** Derivado do esquema gerado; o OpenAPI marca os campos como anuláveis, mas o cliente sempre os envia preenchidos. */
export type NewCheckpoint = { [K in keyof S["CreateCheckpointRequest"]]-?: NonNullable<S["CreateCheckpointRequest"][K]> };

export type LinkKind = S["LinkKind"];
export type LinkState = S["LinkState"];
export type LinkOrigin = S["LinkOrigin"];
export type LinkLine = S["LinkLine"];
export type LinkResponse = S["LinkResponse"];
/** Selo de vínculo em TransactionResponse.link (ausente/null quando não há vínculo). */
export type TransactionLink = S["TransactionLink"];
export type LinkPage = Omit<IntFields<S["LinkPage"], "page" | "size" | "total">, "items"> & { items: LinkResponse[] };
export type LinkQuery = { state?: LinkState; page?: number; size?: number };
export type ReconcileResult = IntFields<S["ReconcileResult"], "linked" | "suggested">;

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
/** Linha da prévia: `ordinal` e parcela estreitados para number (ver IntFields), preservando o `null` da parcela. */
export type ImportLine = Omit<S["ImportLineView"], "ordinal" | "installmentNumber" | "installmentTotal"> & {
  ordinal: number;
  installmentNumber: number | null;
  installmentTotal: number | null;
};
export type ImportCheckpoint = S["ImportCheckpointView"];
export type ImportInvoice = S["ImportInvoiceView"];
/**
 * `ordinal` tem padrão 0 no servidor; o openapi-typescript torna obrigatório todo campo com `default`, então aqui ele
 * volta a ser opcional (o corpo do Nubank, sempre ordinal 0, continua sem o campo) e estreitado para number.
 */
export type ImportLineOverride = Omit<S["ImportLineOverride"], "ordinal"> & { ordinal?: number };
export type ImportBatch = IntFields<S["ImportBatchSummary"], "newCount" | "existingCount">;
export type ImportPreview = Omit<IntFields<S["ImportPreviewView"], "newCount" | "existingCount" | "editedCount">, "lines"> & {
  lines: ImportLine[];
};
/** Corpo do confirm com `overrides` no formato de ImportLineOverride acima (`ordinal` opcional). */
export type ConfirmBody = Omit<S["ConfirmImportRequest"], "overrides"> & { overrides?: ImportLineOverride[] | null };
export type ConfirmResult = IntFields<S["ConfirmResult"], "created" | "skipped" | "linked" | "suggested">;
/** Corpo do 409 `blocking_warnings` do confirm. */
export type BlockingWarnings = S["BlockingWarningsResponse"];

// Auth: o OpenAPI não descreve estes corpos (objetos anônimos em backend/src/FinanceDashboard.Api/Auth/AuthEndpoints.cs).
/** `me()` só devolve este objeto com e-mail não vazio; resposta sem e-mail é tratada como resposta inválida. */
export type CurrentUser = { email: string };
export type CsrfTokenResponse = { token: string };
export type LoginBody = S["LoginRequest"];
