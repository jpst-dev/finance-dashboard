# Finance v2 — S1a: núcleo + importador Nubank Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Servidor com contas, categorias, lançamentos, checkpoints, saldo, resumo mensal e importador Nubank OFX (prévia, confirmar, desfazer), isolado por usuário, testável pelo Scalar/curl.

**Architecture:** Camadas simples por módulo (abordagem A). `FinanceDashboard.Domain` (novo): entidades e regras puras (saldo, resumo). `Infrastructure`: EF Core/Npgsql, filtro global por usuário, leitor OFX e serviço de importação. `Api`: endpoints minimal API por módulo (Accounts, Categories, Transactions, Summary, Imports) atrás de cookie + antiforgery.

**Tech Stack:** .NET 10, EF Core 10 + Npgsql, xUnit v2, Testcontainers (`postgres:17`), `WebApplicationFactory` (já existentes na S0).

**Spec:** `docs/superpowers/specs/2026-11-02-finance-v2-s1a-nucleo-nubank-design.md` (autoridade). Pai: `docs/superpowers/specs/2026-11-02-finance-v2-backend-design.md`.

## Global Constraints
- Identificadores de código em **inglês** (como a S0); textos de usuário/UI ficam para a S1b. Glossário: Conta=`Account`, Categoria=`Category`, Lançamento=`Transaction`, Checkpoint=`BalanceCheckpoint`, Lote=`ImportBatch`, Pendente de classificação=`TransactionState.PendingClassification`.
- Dinheiro na API: **string decimal** (`"1234.56"`, ponto, no máx. 2 casas; entrada com vírgula, milhar ou número JSON → `400`); no banco `numeric(18,2)`; nunca `double`/`float`. Datas: `DateOnly` (coluna `date`), data literal do OFX sem converter fuso.
- Toda entidade de dados tem `UserId`; filtro global de consulta por usuário; recurso de outro usuário responde `404`. Rotas sob `/api/...`, JSON, `RequireAuthorization()`; todo POST/PUT/DELETE valida antiforgery (`X-XSRF-TOKEN`) como `/api/auth/*`.
- Enums gravados como **string** no banco. Exclusão lógica (`IsDeleted`) **não** entra no filtro global (a importação precisa enxergar apagados); consultas de leitura usam o helper `Visible()`.
- Nunca logar descrição, valores, nomes, contraparte ou corpo de requisição; erros de parser citam tipo e número da linha, nunca conteúdo. Arquivo enviado não é guardado.
- Upload: 1 arquivo, `5 MB`, validação por conteúdo, XML/SGML sem DTD/entidades externas, `422` para formato inválido.
- Fixtures sintéticas geradas em código (nenhum dado de `finance-samples/` em testes, commits, logs ou mensagens de falha). `TreatWarningsAsErrors` permanece ligado.
- Comandos a partir de `backend/`. Testes: `dotnet test FinanceDashboard.sln` (Docker precisa estar rodando).

## Review Focus
1. Dinheiro mal formatado (`"1.234,56"`, `1234.56` numérico, 3 casas, `"abc"`) em qualquer endpoint → `400`, nunca gravado (Task 3/4).
2. OFX com BOM UTF-8, e com `CHARSET:1252` e acentos: descrições chegam intactas (Task 6).
3. `DTPOSTED` `20261031000000[-3:BRT]` vira `2026-10-31` e não desloca para outro dia (Task 6).
4. Arquivo vazio, gigante, não-OFX, ou com `<!DOCTYPE`/entidade: `422`/`413` sem repetir conteúdo na resposta nem nos logs (Task 6/7/9).
5. Duas confirmações simultâneas (mesmo lote, ou mesmo arquivo em dois lotes) não duplicam lançamentos (Task 8).
Também: usuário B nunca lê/altera/confirma/desfaz dados do usuário A (Tasks 2–8).

---

### Task 1: Projeto Domain, entidades e regras puras

**Files:**
- Create: `backend/src/FinanceDashboard.Domain/FinanceDashboard.Domain.csproj` (sem pacotes), `Enums.cs`, `Account.cs`, `Category.cs`, `Transaction.cs`, `BalanceCheckpoint.cs`, `ImportBatch.cs`, `BalanceCalculator.cs`, `MonthlySummaryCalculator.cs`
- Modify: `backend/FinanceDashboard.sln` (adicionar projeto), `FinanceDashboard.Infrastructure.csproj` (referência ao Domain)
- Test: `backend/tests/FinanceDashboard.Tests/Domain/BalanceCalculatorTests.cs`, `MonthlySummaryCalculatorTests.cs`

**Interfaces:**
- Produces (namespace `FinanceDashboard.Domain`):
  - Enums: `AccountType {Checking, CreditCard, Wallet}`, `CategoryKind {Expense, Income}`, `TransactionState {Normal, PendingClassification}`, `TransactionOrigin {Manual, Imported}`, `CheckpointOrigin {LedgerBal, Manual}`, `ImportSource {NubankChecking, NubankCard}`, `ImportBatchState {Preview, Confirmed, Undone}`.
  - Entidades (propriedades públicas `get; set;`, `Guid Id`, `string UserId` em todas): `Account {Name, Type, Institution, ExternalIdentifier?, IsArchived}` com `bool IsLiability => Type == CreditCard`; `Category {Name, Kind, IsDefault}`; `Transaction {AccountId, Date, PurchaseDate?, StatementDate?, Amount, OriginalDescription, NormalizedDescription, CategoryId?, State, Origin, BatchId?, SourceKey?, Ordinal, CounterpartyName?, CounterpartyDocument?, IsDeleted, EditedByUser}`; `BalanceCheckpoint {AccountId, Date, Amount, Origin, BatchId?, CreatedAt (DateTimeOffset)}`; `ImportBatch {AccountId, Source, FileHash, PeriodStart?, PeriodEnd?, State, CreatedAt, ExpiresAt?, PreviewJson?, NewCount, ExistingCount, WarningsJson?}`.
  - `record LedgerLine(DateOnly Date, decimal Amount)`; `static class BalanceCalculator { BalanceCheckpoint? Latest(IEnumerable<BalanceCheckpoint>); decimal Compute(BalanceCheckpoint? latest, IEnumerable<LedgerLine> lines) }`.
  - `record SummaryLine(decimal Amount, AccountType AccountType, TransactionState State, Guid? CategoryId)`; `record CategoryTotal(Guid? CategoryId, decimal Total)`; `record SummaryResult(decimal Income, decimal Expenses, decimal Net, IReadOnlyList<CategoryTotal> ByCategory, int PendingCount, decimal PendingTotal)`; `static class MonthlySummaryCalculator { SummaryResult Compute(IEnumerable<SummaryLine>) }`.

- [ ] **Step 1: Write failing tests** — `BalanceCalculatorTests`: `No_checkpoint_sums_all_lines`; `Lines_on_or_before_checkpoint_date_are_excluded` (checkpoint 2026-10-31 = 100, linha 10-31 = 50 ignorada, linha 11-01 = −20 ⇒ 80); `Latest_is_greatest_date_then_most_recently_created`; `Empty_is_zero`. `MonthlySummaryCalculatorTests`: `Income_counts_positive_normal_lines_on_non_card_accounts`; `Expenses_sum_negative_normal_lines_on_any_account` (Expenses é número **negativo**); `Card_refund_positive_reduces_expenses_and_is_not_income`; `Pending_lines_are_excluded_and_reported_with_count_and_total`; `Net_is_income_plus_expenses`; `By_category_groups_signed_totals_with_null_for_uncategorized`; `Month_with_only_pending_returns_zeros_and_pending_count`.
- [ ] **Step 2:** `dotnet test FinanceDashboard.sln --filter "FullyQualifiedName~Domain"` → falha de compilação (tipos inexistentes).
- [ ] **Step 3:** Criar o projeto `net10.0` herdando `Directory.Build.props`, as entidades/enums acima e as duas calculadoras puras (sem EF, sem I/O). Adicionar ao `.sln`; Infrastructure referencia Domain.
- [ ] **Step 4:** Mesmo comando → PASS; `dotnet build FinanceDashboard.sln` sem warnings.
- [ ] **Step 5: Commit** `feat(domain): entidades e regras de saldo e resumo`.

### Task 2: Persistência, filtro por usuário, migration e categorias padrão

**Files:**
- Create: `Infrastructure/ICurrentUser.cs`, `Infrastructure/IUserOwned.cs` (interface `string UserId`; implementada por Account, Category, Transaction, BalanceCheckpoint, ImportBatch via `partial`/herança — escolha do construtor), `Infrastructure/QueryExtensions.cs` (`IQueryable<Transaction> Visible(this IQueryable<Transaction>)` ⇒ `!IsDeleted`), `Infrastructure/DefaultCategories.cs`, `Api/Security/HttpCurrentUser.cs`, migration `S1aCore` (nome livre)
- Modify: `AppDbContext.cs`, `InfrastructureServiceCollectionExtensions.cs`, `Api/Program.cs`, `Domain` entidades (marcar `IUserOwned`), `tests/.../MigrationTests.cs` se necessário
- Test: `tests/.../Persistence/UserIsolationTests.cs`, `ConstraintTests.cs`, `DefaultCategoriesTests.cs`

**Interfaces:**
- Produces: `interface ICurrentUser { string? UserId { get; } }`; `AppDbContext(DbContextOptions<AppDbContext>, ICurrentUser)` com `DbSet<Account> Accounts`, `DbSet<Category> Categories`, `DbSet<Transaction> Transactions`, `DbSet<BalanceCheckpoint> Checkpoints`, `DbSet<ImportBatch> ImportBatches`; filtro global `e.UserId == CurrentUserId` em todas; `SaveChanges*` preenche `UserId` das entidades `Added` que o tenham vazio a partir de `ICurrentUser` (falha se não houver usuário); `static class DefaultCategories { Task EnsureForUserAsync(AppDbContext db, string userId, CancellationToken ct = default); Task EnsureForAllUsersAsync(IServiceProvider sp, CancellationToken ct = default) }` (idempotente; usa `IgnoreQueryFilters`; despesas: Alimentação, Moradia, Transporte, Saúde, Lazer, Educação, Assinaturas, Compras, Impostos e taxas, Outros; receitas: Salário, Rendimentos, Outras receitas).
- Mapeamento: enums como string; `Amount`/checkpoint `numeric(18,2)`; índice único filtrado `(AccountId, SourceKey, Ordinal)` onde `SourceKey IS NOT NULL`; índice único parcial `(UserId, FileHash)` onde `State = 'Confirmed'`; FK de `Transaction.CategoryId` com `OnDelete SetNull`; índice `(AccountId, Date)`; `ImportBatch.PreviewJson`/`WarningsJson` como `jsonb`.
- `Api`: `HttpCurrentUser` lê `ClaimTypes.NameIdentifier` via `IHttpContextAccessor` (registrar `AddHttpContextAccessor` e `ICurrentUser` escopado); `Program.cs` chama `DefaultCategories.EnsureForAllUsersAsync` após o seed do usuário.

- [ ] **Step 1: Write failing tests** — `UserIsolationTests.Query_filter_hides_other_users_rows_for_every_entity` (dois `ICurrentUser` falsos, mesma base); `Saving_without_current_user_throws`; `IgnoreQueryFilters_sees_all`. `ConstraintTests.Duplicate_source_key_and_ordinal_in_same_account_is_rejected`; `Same_source_key_in_different_accounts_is_allowed`; `Confirmed_hash_is_unique_per_user_but_reusable_after_undone`; `Deleting_category_nulls_transaction_category`; `Amount_roundtrips_with_two_decimals`. `DefaultCategoriesTests.Ensure_is_idempotent_and_covers_existing_users`. Atualizar/garantir `MigrationTests` (modelo sem mudanças pendentes).
- [ ] **Step 2:** `dotnet test FinanceDashboard.sln --filter "FullyQualifiedName~Persistence"` → FAIL.
- [ ] **Step 3:** Implementar mapeamento, `SaveChanges` override, extensão `Visible()`, `DefaultCategories`, `HttpCurrentUser`, DI e hook no `Program.cs`. Gerar a migration: `dotnet tool run dotnet-ef migrations add S1aCore -p src/FinanceDashboard.Infrastructure -s src/FinanceDashboard.Api`; revisar o SQL gerado (índices parciais, jsonb).
- [ ] **Step 4:** `dotnet test FinanceDashboard.sln` completo → PASS (testes da S0 continuam verdes).
- [ ] **Step 5: Commit** `feat(infra): modelo de dados da S1a, filtro por usuário e categorias padrão`.

### Task 3: Contas e categorias (API) + base de endpoints

**Files:**
- Create: `Api/Security/AntiforgeryFilter.cs` (extrair de `AuthEndpoints.ValidateAntiforgeryAsync` para `public static class AntiforgeryFilter { ValueTask<object?> ValidateAsync(EndpointFilterInvocationContext, EndpointFilterDelegate) }`, e `AuthEndpoints` passa a usá-lo), `Api/Json/DecimalStringConverter.cs` (grava `"12.50"` invariante; lê **só string** com regex `^-?\d{1,16}(\.\d{1,2})?$`; senão `JsonException`→400), `Api/Accounts/AccountEndpoints.cs`, `Api/Categories/CategoryEndpoints.cs`
- Modify: `Program.cs` (registrar conversor nas `JsonOptions` HTTP e `MapAccounts()/MapCategories()`)
- Test: `tests/.../Support/AuthedClient.cs` (helper), `Accounts/AccountTests.cs`, `Categories/CategoryTests.cs`

**Interfaces:**
- Consumes: Task 2 (`AppDbContext`, entidades).
- Produces: `AuthedClient` (teste): `static Task<AuthedClient> CreateAsync(ApiFactory f, string email, string password)` e métodos `Task<HttpResponseMessage> GetAsync/PostAsync/PutAsync/DeleteAsync(string url, object? body = null)` que buscam token antiforgery após o login e anexam `X-XSRF-TOKEN`; `PostFileAsync(string url, byte[] content, string fileName, IDictionary<string,string> fields)` (multipart, para a Task 7).
- Rotas: `GET/POST /api/accounts`, `PUT /api/accounts/{id}` (nome, instituição, `externalIdentifier`, `isArchived`; sem DELETE); `GET/POST /api/categories`, `PUT/DELETE /api/categories/{id}`. Corpo/Resposta de conta: `{id, name, type, institution, externalIdentifier, isArchived}` (+`balance` na Task 4). Nome 1–80 chars; tipo imutável após criar; ids de outro usuário → `404`; mutação sem antiforgery → `400`; sem login → `401`.

- [ ] **Step 1: Write failing tests** — `AccountTests`: `Create_list_update_archive_account`; `Name_is_required_and_trimmed`; `Type_cannot_change_on_update`; `Other_users_account_is_404_on_get_and_put`; `Mutation_without_antiforgery_is_rejected`; `Anonymous_is_401`. `CategoryTests`: `New_user_has_default_categories`; `Create_rename_delete_category`; `Other_users_category_is_404`. Teste do conversor: `Amount_as_json_number_or_comma_string_is_400` (via endpoint de conta não aplicável — cobrir na Task 4; aqui, teste unitário `DecimalStringConverterTests` com `"1.234,56"`, `"12.345"`, `12.5` (número), `"abc"` rejeitados e `"-12.50"`, `"0"` aceitos).
- [ ] **Step 2:** `dotnet test FinanceDashboard.sln --filter "FullyQualifiedName~Accounts|FullyQualifiedName~Categories|FullyQualifiedName~DecimalString"` → FAIL.
- [ ] **Step 3:** Implementar conforme Interfaces; endpoints com `.WithTags`, `.WithName`, `.Produces(...)` para o OpenAPI.
- [ ] **Step 4:** Suíte completa → PASS (incluir `AuthTests` inalterados).
- [ ] **Step 5: Commit** `feat(api): contas e categorias, conversor de dinheiro e filtro antiforgery compartilhado`.

### Task 4: Lançamentos, checkpoints e saldo

**Files:**
- Create: `Api/Transactions/TransactionEndpoints.cs`, `Api/Transactions/CheckpointEndpoints.cs`, DTOs junto
- Modify: `AccountEndpoints.cs` (incluir `balance` string em GET), `Program.cs`
- Test: `tests/.../Transactions/TransactionTests.cs`, `CheckpointAndBalanceTests.cs`

**Interfaces:**
- Consumes: Task 1 (`BalanceCalculator`), Task 2 (`Visible()`), Task 3 (`AuthedClient`, `DecimalStringConverter`).
- Produces rotas: `GET /api/transactions?accountId&month=2026-09&categoryId&state&page=1&size=50` (ordem `date` desc, `id` desc; `size` máx. 200; resposta `{items:[...], page, size, total}`; só `Visible()`); `POST /api/transactions` `{accountId, date, amount, description, categoryId?, state?}` (origem `Manual`, estado padrão `Normal`); `PUT /api/transactions/{id}` (data, valor, descrição, categoria, estado; **mesma regra para importados**, que ganham `EditedByUser = true`; `SourceKey/Ordinal/BatchId/Origin` imutáveis); `DELETE /api/transactions/{id}` (manual ⇒ remoção física; importado ⇒ `IsDeleted = true`, chave mantida); `GET /api/accounts/{id}/checkpoints`, `POST /api/accounts/{id}/checkpoints` `{date, amount}` (origem `Manual`). Saldo da conta = `BalanceCalculator.Compute(Latest(checkpoints), linhas visíveis)`. Valor ≠ 0, no máx. 2 casas, |valor| < 10^16; descrição 1–300 chars; conta arquivada recusa novos lançamentos (`409`); categoria/conta de outro usuário ⇒ `404`/`400`.

- [ ] **Step 1: Write failing tests** — `TransactionTests`: `Create_list_filters_by_month_account_category_state`; `List_is_ordered_date_desc_then_id_desc_and_paginated`; `Update_changes_state_and_category_of_imported_line` (semear via contexto com `Origin=Imported`); `Update_cannot_change_source_key`; `Delete_manual_removes_row`; `Delete_imported_hides_row_but_keeps_key` (linha some da lista e do saldo, continua no banco com `IsDeleted`); `Amount_validation_rejects_comma_number_zero_and_three_decimals` (400); `Archived_account_rejects_new_transactions`; `Other_users_transaction_is_404_for_get_put_delete`. `CheckpointAndBalanceTests`: `Balance_without_checkpoint_sums_visible_lines`; `Balance_uses_latest_checkpoint_and_only_later_lines`; `Deleted_lines_do_not_affect_balance`; `Card_balance_is_negative_debt`; `Other_users_checkpoints_are_404`.
- [ ] **Step 2:** `dotnet test FinanceDashboard.sln --filter "FullyQualifiedName~Transactions"` → FAIL.
- [ ] **Step 3:** Implementar endpoints e saldo (consulta no banco, regra via Domain).
- [ ] **Step 4:** Suíte completa → PASS.
- [ ] **Step 5: Commit** `feat(api): lançamentos, checkpoints e saldo por conta`.

### Task 5: Resumo mensal

**Files:**
- Create: `Api/Summary/SummaryEndpoints.cs`
- Test: `tests/.../Summary/SummaryTests.cs`

**Interfaces:**
- Consumes: `MonthlySummaryCalculator`, `Visible()`, `AuthedClient`.
- Produces: `GET /api/summary?month=2026-09[&accountId=...]` → `{month, income, expenses, net, byCategory:[{categoryId, categoryName, total}], pending:{count, total}}` (strings decimais; `categoryName` `null` ⇒ cliente exibe "Sem categoria"); `month` obrigatório e inválido ⇒ `400`; recorte por `Date` dentro do mês; só linhas `Visible()`.

- [ ] **Step 1: Write failing tests** — `Summary_for_month_matches_calculator_rules`; `Pending_are_excluded_but_counted`; `Deleted_lines_are_excluded`; `Other_month_lines_are_excluded_boundaries` (31/10 e 01/11); `Invalid_month_is_400`; `Only_pending_month_returns_zeros_with_count`; `Other_users_data_never_counted`.
- [ ] **Step 2:** `dotnet test FinanceDashboard.sln --filter "FullyQualifiedName~SummaryTests"` → FAIL.
- [ ] **Step 3:** Implementar endpoint carregando as linhas do mês e delegando ao Domain.
- [ ] **Step 4:** Suíte completa → PASS.
- [ ] **Step 5: Commit** `feat(api): resumo mensal calculado no servidor`.

### Task 6: Leitor OFX do Nubank e regras de texto

**Files:**
- Create: `Infrastructure/Importing/NubankOfxReader.cs`, `ParsedStatement.cs`, `ImportFormatException.cs`, `NubankRules.cs`
- Test: `tests/.../Importing/OfxTestFile.cs` (construtor de arquivos sintéticos em código: conta/cartão, `CHARSET`, BOM, linhas, LEDGERBAL opcional), `NubankOfxReaderTests.cs`, `NubankRulesTests.cs`

**Interfaces:**
- Produces (namespace `FinanceDashboard.Infrastructure.Importing`):
  - `record ParsedLine(string FitId, DateOnly Date, decimal Amount, string Memo)`; `record ParsedBalance(decimal Amount, DateOnly AsOf)`; `record ParsedStatement(ImportSource Source, string? AccountExternalId, DateOnly? PeriodStart, DateOnly? PeriodEnd, IReadOnlyList<ParsedLine> Lines, ParsedBalance? LedgerBalance)`.
  - `static class NubankOfxReader { ParsedStatement Parse(ReadOnlySpan<byte> content) }`; `sealed class ImportFormatException(string kind, int? line) : Exception` com `Kind` ∈ {`not_ofx`, `empty`, `too_large`, `unsupported_multiple_statements`, `malformed`, `forbidden_dtd`} e mensagem só com tipo e linha.
  - `static class NubankRules { TransactionState Classify(ImportSource source, string memo); (string? Name, string? MaskedDocument) ExtractCounterparty(string memo) }`.
- Regras do leitor: tolerante (não usa parser XML estrito; varre tags `<TAG>valor` com ou sem fechamento); decodifica por `CHARSET`/`ENCODING` do cabeçalho (1252 via `CodePagesEncodingProvider`, UTF-8; BOM tolerado; `CHARSET:NONE` ⇒ UTF-8); reconhece conta por `BANKMSGSRSV1/STMTRS` e cartão por `CREDITCARDMSGSRSV1/CCSTMTRS`; `<!DOCTYPE`/`<!ENTITY` ⇒ `forbidden_dtd`; >1 `STMTRS`/`CCSTMTRS` ⇒ `unsupported_multiple_statements`; limite de 5 MB e de tamanho de campo (ex.: 2 000 chars); `AccountExternalId` = `ACCTID`; `DTPOSTED`/`DTSTART`/`DTEND`/`DTASOF` ⇒ **primeiros 8 dígitos** como `DateOnly`; `TRNAMT` com ponto decimal ⇒ `decimal` invariante, sem converter sinal (conta: entrada +; cartão: cobrança −, pagamento +); `MEMO` ⇒ `Memo`.
- `Classify`: sem diferenciar maiúsculas/acentos, `PendingClassification` se o memo começa com `Pagamento de fatura`, `Pagamento recebido`, `Transferência enviada`, `Transferência recebida` ou `Pix no Crédito`; senão `Normal`. `ExtractCounterparty`: em memos `Transferência (enviada|recebida) pelo Pix - NOME - DOCUMENTO - BANCO ...` devolve nome e documento mascarado; senão `(null, null)`.

- [ ] **Step 1: Write failing tests** — `NubankOfxReaderTests`: `Parses_checking_statement_lines_period_and_ledger_balance`; `Parses_card_statement_keeping_sign_charge_negative`; `Decodes_cp1252_header_accents_intact`; `Decodes_utf8_with_bom`; `Charset_none_is_utf8`; `Posted_date_is_literal_not_shifted_by_offset` (`20261031000000[-3:BRT]` ⇒ 2026-10-31; também `20261101000000[0:GMT]` ⇒ 2026-11-01); `Missing_ledger_balance_is_null`; `Empty_period_is_valid`; `Rejects_empty_file`, `Rejects_non_ofx_bytes`, `Rejects_doctype_and_entity`, `Rejects_multiple_statements`, `Rejects_oversized_input`; `Exception_message_has_kind_and_line_but_no_content` (memo marcador não aparece em `Message`). `NubankRulesTests`: `Classifies_prefixes_ignoring_case_and_accents` (inclui "pagamento de fatura", "TRANSFERENCIA ENVIADA", "Pix no Credito"); `Regular_purchase_is_normal`; `Extracts_counterparty_and_masked_document_from_pix_memo`; `Non_pix_memo_has_no_counterparty`.
- [ ] **Step 2:** `dotnet test FinanceDashboard.sln --filter "FullyQualifiedName~Importing"` → FAIL.
- [ ] **Step 3:** Implementar leitor e regras (registrar `CodePagesEncodingProvider` uma vez).
- [ ] **Step 4:** Mesmo comando → PASS.
- [ ] **Step 5: Commit** `feat(import): leitor OFX do Nubank e regras de classificação`.

### Task 7: Importação — upload e prévia

**Files:**
- Create: `Infrastructure/Importing/ImportService.cs` (`CreatePreviewAsync`, `GetAsync`, `ListAsync`), `ImportWarning.cs`, `Api/Imports/ImportEndpoints.cs`
- Modify: `Program.cs` (DI + `MapImports()`)
- Test: `tests/.../Importing/ImportPreviewTests.cs`

**Interfaces:**
- Consumes: Task 6 (reader/regras), Task 2/4 (entidades, `Visible()`), `AuthedClient.PostFileAsync`.
- Produces: `sealed record ImportWarning(string Code, string Severity /* "warning"|"blocking" */, string Message /* sem conteúdo do arquivo */)`; códigos: `AlreadyImported` (warning), `AccountIdentifierMismatch` (blocking), `FitidInOtherAccount` (blocking), `DateOutsidePeriod`, `BalanceMismatch`, `PeriodGap`, `PeriodOverlap`. Rotas: `POST /api/imports` (multipart: `file`, `accountId`) ⇒ `200` `{batchId, source, periodStart, periodEnd, newCount, existingCount, warnings, checkpoint:{date, amount}|null, lines:[{date, amount, description, state, existing}]}`; `GET /api/imports`, `GET /api/imports/{id}`.
- Comportamento: tipo de conta × fonte (cartão só `CreditCard`, conta só `Checking` ⇒ `422`); arquivo inválido ⇒ `422 {error: kind}`; >5 MB ⇒ `413`; cria `ImportBatch` em `Preview` com `PreviewJson`, `FileHash` (SHA-256), `ExpiresAt = agora + 24h` (usar `TimeProvider`); descarta previews expiradas do usuário ao enviar/listar; reenviar o mesmo hash com preview aberta substitui essa preview; hash já `Confirmed` ⇒ `AlreadyImported` com `newCount = 0`; "existente" = existe `Transaction` na conta com mesma `SourceKey` (inclusive `IsDeleted`); checagens: `ExternalIdentifier` da conta ≠ `AccountExternalId` do arquivo ⇒ `AccountIdentifierMismatch`; FITIDs já presentes em outra conta do usuário ⇒ `FitidInOtherAccount`; datas fora de `[PeriodStart, PeriodEnd]` ⇒ `DateOutsidePeriod`; `LedgerBalance` ≠ (checkpoint anterior + linhas posteriores incl. novas) ⇒ `BalanceMismatch` (só se existir checkpoint anterior); período sem encontro/sobreposto a lotes `Confirmed` da conta ⇒ `PeriodGap`/`PeriodOverlap`. **Nenhum lançamento/checkpoint é gravado** na prévia. Validação do multipart sem DTD; antiforgery explícito (`DisableAntiforgery()` no endpoint se o antiforgery embutido duplicar a checagem); `RequestSizeLimit` 5 MB.

- [ ] **Step 1: Write failing tests** — `Preview_of_checking_file_counts_new_and_writes_no_transactions`; `Preview_of_card_file_into_checking_account_is_422`; `Invalid_file_is_422_with_kind_and_without_content`; `Oversized_file_is_413`; `Existing_fitids_are_reported_existing_including_soft_deleted`; `Same_hash_confirmed_reports_already_imported_with_zero_new` (semear lote Confirmed); `Same_hash_with_open_preview_replaces_it`; `Expired_previews_are_discarded_on_next_upload` (FakeTimeProvider/`TimeProvider` injetado); `Account_identifier_mismatch_is_blocking_warning`; `Fitid_in_other_account_is_blocking_warning`; `Date_outside_period_and_balance_mismatch_warn`; `Other_users_account_id_is_404`; `Other_users_batch_is_404`; `Anonymous_is_401`; `Upload_without_antiforgery_is_rejected`.
- [ ] **Step 2:** `dotnet test FinanceDashboard.sln --filter "FullyQualifiedName~ImportPreview"` → FAIL.
- [ ] **Step 3:** Implementar serviço e endpoints (injetar `TimeProvider.System` em produção; ajustar `ApiFactory` do teste para substituir por `FakeTimeProvider` quando fornecido — pacote `Microsoft.Extensions.TimeProvider.Testing`).
- [ ] **Step 4:** Suíte completa → PASS.
- [ ] **Step 5: Commit** `feat(import): upload e prévia do OFX do Nubank`.

### Task 8: Importação — confirmar, desfazer e concorrência

**Files:**
- Modify: `ImportService.cs` (`ConfirmAsync`, `UndoAsync`), `ImportEndpoints.cs`
- Test: `tests/.../Importing/ImportConfirmTests.cs`, `ImportUndoTests.cs`, `ImportConcurrencyTests.cs`, `ImportIdempotencyTests.cs`

**Interfaces:**
- Produces: `POST /api/imports/{id}/confirm` `{acknowledgeBlockingWarnings: bool}` ⇒ `200 {batchId, created, skipped}`; `POST /api/imports/{id}/undo` ⇒ `204`.
- Comportamento de confirmar: só lote `Preview` não expirado do usuário (senão `404`/`410`); revalida contra o estado atual (recalcula existentes); avisos `blocking` exigem `acknowledgeBlockingWarnings=true` (senão `409` com os avisos); numa transação: insere somente linhas cuja `(conta, SourceKey, ordinal 0)` não existe — estado via `NubankRules.Classify`, contraparte via `ExtractCounterparty`, `Origin=Imported`, `BatchId`, `SourceKey=FITID`, `Ordinal=0`, `NormalizedDescription` = trim + espaços colapsados, `PurchaseDate=Date`; cria checkpoint `LedgerBal` (data `AsOf`, valor do `LEDGERBAL`) se houver, sem duplicar se já existir checkpoint na mesma conta/data/valor; lote ⇒ `Confirmed`, descarta `PreviewJson`; violação do índice único por corrida ⇒ trata como linha já existente (não aborta) e, se o hash `Confirmed` colidir, responde `409 already_imported`. Linhas existentes nunca são alteradas (edições do usuário preservadas).
- Desfazer: lote `Confirmed` do usuário ⇒ remove fisicamente seus lançamentos (inclusive `IsDeleted`) e seu checkpoint, lote ⇒ `Undone` (libera o hash); lote `Preview` ⇒ remove o lote. Lançamentos editados pelo usuário dentro do lote (`EditedByUser`) **também** são removidos; `GET /api/imports/{id}` expõe `editedCount` para o cliente avisar antes do desfazer.

- [ ] **Step 1: Write failing tests** — Confirm: `Confirm_creates_transactions_with_state_counterparty_and_checkpoint`; `Confirm_does_not_touch_existing_edited_lines`; `Confirm_with_blocking_warning_requires_acknowledgement`; `Confirm_expired_preview_is_410`; `Other_users_batch_confirm_is_404`; `Confirm_twice_second_is_409_or_noop`. Undo: `Undo_removes_lines_checkpoint_and_frees_hash_allowing_reimport`; `Undo_removes_soft_deleted_lines_of_the_batch`; `Undo_reports_edited_count_beforehand`; `Other_users_batch_undo_is_404`. Concorrência: `Two_simultaneous_confirms_of_same_batch_create_lines_once`; `Same_file_in_two_previews_confirmed_simultaneously_creates_lines_once` (Task.WhenAll com 8 chamadas). Idempotência (estilo propriedade, `Random` com semente fixa, 25 execuções): para sequências aleatórias de {preview, confirm, undo, reimport} sobre 2–3 arquivos sintéticos sobrepostos, o conjunto final de `(conta, SourceKey)` nunca tem duplicatas e reimportar o mesmo arquivo confirmado não altera o total.
- [ ] **Step 2:** `dotnet test FinanceDashboard.sln --filter "FullyQualifiedName~ImportConfirm|FullyQualifiedName~ImportUndo|FullyQualifiedName~ImportConcurrency|FullyQualifiedName~ImportIdempotency"` → FAIL.
- [ ] **Step 3:** Implementar serviço e endpoints (sem migration nova: `EditedByUser` já existe).
- [ ] **Step 4:** Suíte completa → PASS; repetir os testes de concorrência 3× para checar estabilidade.
- [ ] **Step 5: Commit** `feat(import): confirmar, desfazer lote e proteção contra duplicação concorrente`.

### Task 9: Endurecimento, privacidade, documentação e verificação ponta a ponta

**Files:**
- Modify: `deploy/nginx.conf` (`client_max_body_size 6m;` no `location /api/`), `README.md` (fluxo `curl` completo: csrf → login → csrf → criar conta → `POST /api/imports` → prévia → confirmar → resumo → desfazer; limites; semântica de pendentes/exclusão lógica), `docs/adr/0003-importacao-idempotente-e-desfazer.md` (por que chave+ordinal, prévia no servidor, exclusão lógica), `Program.cs`/OpenAPI se faltarem `.Produces`
- Create: `tests/.../Privacy/NoSensitiveLoggingTests.cs`, `tests/.../Importing/LocalSamplesTests.cs` (atributo `LocalSamplesFact` que usa `Skip` quando `finance-samples/` não existe; **nunca** imprime descrições em falhas)
- Test: `tests/.../OpenApiTests.cs` (estender)

**Interfaces:**
- Consumes: tudo das Tasks 1–8, `CapturingLoggerProvider` (S0), `ApiFactory.LoggerProvider`.

- [ ] **Step 1: Write failing tests** — `NoSensitiveLoggingTests.Import_flow_logs_never_contain_descriptions_amounts_names_or_body` (marcadores únicos no memo, valor, nome da contraparte; executa conta+cartão, prévia, confirmar, desfazer e um upload inválido; nenhuma entrada capturada contém os marcadores); `OpenApiTests.Documents_accounts_categories_transactions_summary_imports_routes`; `LocalSamplesTests.Real_samples_parse_and_roundtrip` (conta e cartão; só conta linhas/checkpoint, nunca imprime texto; ignorado na CI e quando a pasta não existe).
- [ ] **Step 2:** `dotnet test FinanceDashboard.sln --filter "FullyQualifiedName~Privacy|FullyQualifiedName~OpenApi|FullyQualifiedName~LocalSamples"` → falhas esperadas nos novos.
- [ ] **Step 3:** Ajustar código/OpenAPI, nginx, README e ADR.
- [ ] **Step 4:** Verificação final: `dotnet test FinanceDashboard.sln` (todos verdes); `docker compose up -d --build`; seguir o fluxo `curl` do README com um arquivo sintético (nunca amostras reais em logs) e confirmar prévia→confirmar→resumo→desfazer; `docker compose logs api` não mostra descrições; parar a stack só se o João pedir.
- [ ] **Step 5: Commit** `feat(import): endurecimento, documentação e verificação da S1a`.
