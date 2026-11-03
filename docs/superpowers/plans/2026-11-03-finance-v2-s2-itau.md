# Finance v2 — S2: importadores do Itaú Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Importar o extrato da conta Itaú (OFX) e a fatura do cartão Itaú (xlsx) sem digitação, sem duplicar, sem perder lançamentos idênticos legítimos e sem contar em silêncio pagamento/transferência/crédito como despesa ou receita.

**Architecture:** O pipeline da S1a passa a tratar a identidade da linha como o par `(SourceKey, Ordinal)` de ponta a ponta (prévia, overrides, `DistinctBy`, existentes, insert). Um despachante `StatementReader` escolhe o leitor pelo conteúdo: OFX → leitor tolerante comum (`OfxReader`) + perfil por `BANKID` (Nubank, Itaú); zip → pré-validação própria (`XlsxSafety`) + leitor SAX genérico (`XlsxSheetReader`, Open XML SDK) + layout/regra da fatura (`ItauInvoiceLayout`, `ItauRules`). As conferências que precisam do banco ficam em `ImportChecks`; o front ganha `.xlsx`, ordinal nos overrides, parcela/cartão na prévia e os códigos novos.

**Tech Stack:** .NET 10, EF Core + Npgsql, PostgreSQL (Testcontainers), `DocumentFormat.OpenXml` (MIT, leitura SAX), `System.IO.Compression`; Vue 3 + TS + Pinia + vue-i18n + Vitest.

**Spec:** `docs/superpowers/specs/2026-11-03-finance-v2-s2-itau-design.md` (autoridade, incluindo "Decisões minhas" e "Decisões complementares"). Pais: `2026-11-02-finance-v2-backend-design.md`, `2026-11-02-finance-v2-s1a-nucleo-nubank-design.md`, `2026-11-02-finance-v2-s1b-interface-design.md`, `docs/adr/0003-importacao-idempotente-e-desfazer.md`.

## Global Constraints
- Valem todas as restrições do pai/S1a/S1b: .NET 10, `numeric(18,2)`, `DateOnly` literal do arquivo (sem UTC), antiforgery, upload ≤ 5 MB (`StatementReader.MaxBytes = 5 * 1024 * 1024`; nginx `client_max_body_size 6m`), um arquivo por requisição, DTD proibido, Testcontainers, dinheiro string no front, nenhuma aritmética de dinheiro no navegador.
- **Nada de dado real**: não abrir `finance-samples/`, `.ofx`/`.xlsx` reais, exceto a suíte `LocalSamplesFact` (Task 11), que nunca imprime conteúdo. Fixtures **sintéticas geradas por código** com os rótulos literais do spec (nomes "FICTICIO", valores inventados).
- Sinal normalizado no leitor: conta (entrada +/saída −); cartão (cobrança −/pagamento +). **xlsx do Itaú invertido no leitor** (cobrança positiva → negativa; pagamento/estorno negativo → positivo).
- Números com `CultureInfo.InvariantCulture`; CP1252 via `CodePagesEncodingProvider`; datas do OFX tomadas dos 8 primeiros dígitos do texto, ignorando `[-3:BRT]`.
- `SourceKey` do Itaú = SHA-256 hex minúsculo (64 chars) de componentes unidos por `|`; normalização de texto **congelada** (maiúsculas invariantes, sem marcas de acento após FormD, `\s+` → um espaço, trim). Mudar exige migração.
- Logs e mensagens de erro nunca contêm nome de arquivo, descrição, valor, nome, ACCTID, final de cartão ou conteúdo de célula; erros citam só `kind` e número de linha/linha da planilha.
- xlsx: nunca avaliar fórmula, seguir link externo, abrir macro/embedding/activeX, ler mídia/drawing como XML; XML com `DtdProcessing.Prohibit` e `XmlResolver = null`. EPPlus 5+ e ClosedXML proibidos.
- Códigos de aviso: os **já existentes** mantêm o nome do código (`AccountIdentifierMismatch`, `BalanceMismatch`, `PeriodGap`, `PeriodOverlap`, `DateOutsidePeriod`, `FitidInOtherAccount`, `AlreadyImported`); novos: `InvoiceTotalMismatch`, `InvoiceHasMissingLines`, `LedgerBalanceBeforeLastLine`, `PossibleDuplicate`, `UnknownLineKinds`, `UnknownCardSuffix`.
- Códigos de erro novos (422 `{error}`): `unsupported_bank`, `not_xlsx`, `encrypted_xlsx`, `xlsx_limits_exceeded`, `xlsx_unsafe_content`, `unsupported_layout`, `invalid_cell`.
- Toda mudança de contrato: `UPDATE_OPENAPI_SNAPSHOT=1` + `npm run api:types` no mesmo commit (a CI faz `git diff --exit-code src/real/api/schema.d.ts`).
- Comandos: backend em `backend/` (`dotnet test FinanceDashboard.sln`, Docker rodando); front na raiz (`npm test`, `npm run build`, `npm run build:real`, `node scripts/check-bundles.mjs`). Migrations: `dotnet tool restore && dotnet ef migrations add <Nome> -p src/FinanceDashboard.Infrastructure -s src/FinanceDashboard.Api`.
- Modo ausente: decisões novas de implementação vão para `docs/away-log.md` (uma linha cada) no commit da tarefa.

## Review Focus
1. **Linha legítima idêntica perdida**: 4 lançamentos iguais no mesmo dia viram 4 linhas (ordinais 0..3); linha em branco ou data em texto no meio da tabela da fatura **não** pode encerrar a tabela em silêncio e descartar o resto (Task 3, 6, 8).
2. **Duplicação em exportações sobrepostas / corrida**: reimportar N vezes, exportações sobrepostas e confirmações simultâneas convergem para a multiplicidade máxima por chave; buracos de ordinal após desfazer são relatados com honestidade (Task 2, 8).
3. **Pagamento/transferência/crédito contado em silêncio**: `FATURA PAGA`, `PIX TRANSF`, `PIX CARTAO`, `PAG BOLETO`, `CREDITO LIBERAD`, `EST CRED`, `CREDITO CONSIGNADO`, `APLICACAO`, `RESGATE` e o `PAGAMENTO` da fatura ficam Pendente; estorno vira positivo e Normal; total da fatura confere (Task 3, 6, 8).
4. **Parcela datada no mês errado**: parcela N>1 tem `Date` = vencimento − 1 mês (com fim de mês) e `PurchaseDate` = data original; o resumo mensal conta no mês certo (Task 6, 8).
5. **zip/xlsx hostil**: bomba de zip (inclusive com tamanho declarado mentiroso), macro, link externo, conexão, embedding, activeX, criptografado/.xls, nome com `..`/absoluto, DTD em parte XML, fórmula em lançamento, número como texto (Task 4, 5, 6).
6. **Vazamento de conteúdo** em logs (Trace), exceções e mensagens de aviso, para os dois leitores (Task 3, 6, 8).
7. **Regressão do Nubank**: suíte S1a/S1b inteira verde a cada tarefa; FITID repetido no arquivo continua importado uma vez; corpo do confirm do Nubank sem `ordinal` continua aceito; `FitidInOtherAccount` continua só para Nubank (Task 2, 3, 9).
8. **Esconder valores** cobre o total declarado da fatura na prévia e no histórico; final do cartão só aparece como `•••• 1234` (Task 10).

---

### Task 1: Domínio, migration e tipos de apoio (fonte, cartão, aviso não sobrescrevível)

**Files:**
- Modify: `backend/src/FinanceDashboard.Domain/Enums.cs`, `backend/src/FinanceDashboard.Domain/Transaction.cs`, `backend/src/FinanceDashboard.Domain/ImportBatch.cs`, `backend/src/FinanceDashboard.Infrastructure/AppDbContext.cs`, `backend/src/FinanceDashboard.Infrastructure/Importing/ImportWarning.cs`
- Create: `backend/src/FinanceDashboard.Infrastructure/Importing/ImportSources.cs`, `backend/src/FinanceDashboard.Infrastructure/Migrations/<ts>_S2Itau.cs` (gerada, + Designer e snapshot)
- Test: `backend/tests/FinanceDashboard.Tests/Persistence/ConstraintTests.cs` (acrescentar), `backend/tests/FinanceDashboard.Tests/Importing/ImportSourcesTests.cs`, `backend/tests/FinanceDashboard.Tests/Importing/ImportWarningTests.cs`

**Interfaces:**
- Produces:
  - `enum ImportSource { NubankChecking, NubankCard, ItauChecking, ItauCard }` (coluna já é string(32): sem mudança de esquema para o enum).
  - `enum CardKind { Physical, Virtual, VirtualRecurring, Other }`.
  - `Transaction`: `int? InstallmentNumber`, `int? InstallmentTotal`, `string? CardSuffix` (`HasMaxLength(4)`), `CardKind? CardKind` (`HasConversion<string>().HasMaxLength(32)`). `PurchaseDate`/`StatementDate` já existem (= DataCompra/DataFatura do spec).
  - `ImportBatch`: `DateOnly? DueDate`, `decimal? DeclaredTotal` (`numeric(18,2)`), `decimal? LedgerBalance` (`numeric(18,2)`), `DateOnly? LedgerBalanceDate`.
  - Migration única `S2Itau` (só colunas anuláveis novas; nada destrutivo).
  - `ImportWarning(string Code, string Severity, string Message, bool Overridable = true)`; fábricas novas: `InvoiceTotalMismatch()` (Blocking), `InvoiceHasMissingLines(int count)` (Blocking, `Overridable: false`), `LedgerBalanceBeforeLastLine()` (Warn), `PossibleDuplicate(int count)` (Warn), `UnknownLineKinds(int count)` (Warn), `UnknownCardSuffix(int count)` (Warn). Mensagens em inglês, só contagens.
  - `static class ImportSources`: `AccountType ExpectedAccountType(ImportSource s)` (Nubank/ItauChecking → `Checking`; NubankCard/ItauCard → `CreditCard`); `bool UsesContentKeys(ImportSource s)` (true só para Itaú); `bool AccountIdMatches(ImportSource s, string accountExternalIdentifier, string fileAccountId)` (Itaú: remove tudo que não é dígito e zeros à esquerda dos dois lados, compara ordinal; vazio após limpeza = não compara → true; Nubank: `Trim()` + ordinal, como hoje).

- [ ] **Step 1: Write failing tests**
  - `ConstraintTests.Installment_and_card_columns_roundtrip`: grava linha com `InstallmentNumber=2, InstallmentTotal=10, CardSuffix="1234", CardKind=VirtualRecurring`; relê igual; SQL cru `SELECT "CardKind"` = `"VirtualRecurring"`.
  - `ConstraintTests.Card_suffix_longer_than_four_is_rejected` (`DbUpdateException`).
  - `ConstraintTests.Batch_invoice_and_ledger_columns_roundtrip` (`DueDate`, `DeclaredTotal=1234.56m`, `LedgerBalance`, `LedgerBalanceDate`).
  - `ImportSourcesTests.Expected_account_type_per_source` (Theory, 4 casos); `Only_itau_sources_use_content_keys`; `Itau_account_id_ignores_non_digits_and_leading_zeros` (`"0012345"` × `"12.345"` → true; `"12345"` × `"12346"` → false); `Nubank_account_id_is_trimmed_and_case_sensitive` (`" ACC-1 "` × `"ACC-1"` → true; `"acc-1"` × `"ACC-1"` → false).
  - `ImportWarningTests.Legacy_warning_json_without_overridable_reads_as_true` (`StoredPreview.DeserializeWarnings("[{\"code\":\"AccountIdentifierMismatch\",\"severity\":\"blocking\",\"message\":\"x\"}]")[0].Overridable == true`; tornar `DeserializeWarnings` `internal` visível ao teste via `InternalsVisibleTo("FinanceDashboard.Tests")` no csproj da Infrastructure, ou expor público — escolher um e anotar); `Missing_lines_warning_is_blocking_and_not_overridable`; `New_warning_messages_carry_no_content` (mensagens das fábricas novas com `count=7` contêm só `[A-Za-z0-9 ().,';-]`).
- [ ] **Step 2:** `dotnet test FinanceDashboard.sln --filter "FullyQualifiedName~ConstraintTests|FullyQualifiedName~ImportSourcesTests|FullyQualifiedName~ImportWarningTests"` → FAIL (compilação: membros inexistentes).
- [ ] **Step 3:** Implementar enums/propriedades/mapeamento, `ImportSources`, `ImportWarning`; gerar a migration `S2Itau`; trocar em `ImportService.CreatePreviewAsync` o cálculo de `expectedType` e a comparação de ACCTID (preview e `BlockingWarningsAsync`) por `ImportSources.ExpectedAccountType`/`AccountIdMatches` (comportamento do Nubank idêntico).
- [ ] **Step 4:** Suíte completa `dotnet test FinanceDashboard.sln` → PASS (Nubank intacto; `MigrationTests` aplica `S2Itau`).
- [ ] **Step 5: Commit** `feat(domain): fontes do Itaú, metadados de parcela/cartão e aviso não sobrescrevível`.

### Task 2: Pipeline com identidade (SourceKey, Ordinal) de ponta a ponta

**Files:**
- Modify: `backend/src/FinanceDashboard.Infrastructure/Importing/ParsedStatement.cs`, `.../Importing/NubankOfxReader.cs` (preenche o formato novo), `.../Importing/ImportService.cs`, `backend/src/FinanceDashboard.Api/Imports/ImportEndpoints.cs` (doc do `ConfirmImportRequest`), `backend/openapi/v1.json`, `src/real/api/schema.d.ts` (gerado), `src/real/api/types.ts`, `src/real/testingImport.ts` (campos novos com padrão)
- Create: `backend/src/FinanceDashboard.Infrastructure/Importing/SourceKeys.cs`, `backend/src/FinanceDashboard.Infrastructure/Importing/ImportChecks.cs`
- Test: `backend/tests/FinanceDashboard.Tests/Importing/SourceKeysTests.cs`, `backend/tests/FinanceDashboard.Tests/Importing/ImportIdentityTests.cs` (serviço direto sobre Postgres com `PersistenceTestSupport.Create(cs, userId)`)

**Interfaces:**
- Consumes: Task 1 (`ImportSources`, `ImportWarning.Overridable`, colunas novas).
- Produces:
  - `sealed record ParsedLine(string SourceKey, int Ordinal, DateOnly Date, decimal Amount, string Memo, TransactionState State)` com `init`: `string? CounterpartyName`, `string? CounterpartyDocument`, `DateOnly? PurchaseDate`, `DateOnly? StatementDate`, `int? InstallmentNumber`, `int? InstallmentTotal`, `string? CardSuffix`, `CardKind? CardKind`.
  - `sealed record ParsedInvoice(DateOnly DueDate, decimal DeclaredTotal)`.
  - `sealed record ParsedStatement(ImportSource Source, string? AccountExternalId, DateOnly? PeriodStart, DateOnly? PeriodEnd, IReadOnlyList<ParsedLine> Lines, ParsedBalance? LedgerBalance)` com `init`: `ParsedBalance? LedgerRead` (LEDGERBAL lido, mesmo sem checkpoint), `ParsedInvoice? Invoice`, `IReadOnlyList<ImportWarning> Warnings = []` (avisos do leitor, sem banco). `LedgerBalance` = candidato a checkpoint.
  - `NubankOfxReader.Parse` passa a devolver `SourceKey = FITID`, `Ordinal = 0`, `State = NubankRules.Classify(...)`, contraparte preenchida, `PurchaseDate = Date`, `LedgerRead = LedgerBalance` (classificação e contraparte saem do `ImportService`).
  - `static class SourceKeys`: `string NormalizeText(string text)`; `string ContentKey(params string[] parts)` (SHA-256 hex minúsculo de `string.Join("|", parts)` em UTF-8); `string FormatAmount(decimal amount)` (`"0.00"` invariante; zero negativo → `"0.00"`); `string FormatDate(DateOnly d)` (`yyyy-MM-dd`); `int[] Ordinals(IReadOnlyList<string> keys)` (k-ésima ocorrência, ordem do arquivo).
  - `ImportService`:
    - `public Task<PreviewOutcome> CreatePreviewAsync(Guid accountId, byte[] content, CancellationToken ct = default)` (inalterada por fora) delega a `public Task<PreviewOutcome> CreatePreviewAsync(Guid accountId, ParsedStatement parsed, string fileHash, CancellationToken ct = default)` (usada pelos testes desta tarefa).
    - `ImportLineView(string SourceKey, DateOnly Date, decimal Amount, string Description, TransactionState State, bool Existing, int Ordinal, int? InstallmentNumber, int? InstallmentTotal, string? CardSuffix, CardKind? CardKind, DateOnly? PurchaseDate)`.
    - `StoredPreviewLine([property: JsonPropertyName("fitId")] string SourceKey, DateOnly Date, decimal Amount, string Memo, TransactionState State, string? CounterpartyName, string? CounterpartyDocument, bool Existing, int Ordinal = 0, DateOnly? PurchaseDate = null, DateOnly? StatementDate = null, int? InstallmentNumber = null, int? InstallmentTotal = null, string? CardSuffix = null, CardKind? CardKind = null)` (JSON antigo lê com padrões — Decisão complementar 10).
    - `StoredPreview(string? AccountExternalId, ImportCheckpointView? Checkpoint, IReadOnlyList<StoredPreviewLine> Lines, ImportInvoiceView? Invoice = null)`; `sealed record ImportInvoiceView(DateOnly DueDate, decimal DeclaredTotal)`; `ImportPreviewView` e `ImportBatchSummary` ganham `ImportInvoiceView? Invoice` no fim.
    - `ImportLineOverride(string SourceKey, TransactionState? State = null, Guid? CategoryId = null, int Ordinal = 0)`.
    - Existente = par `(SourceKey, Ordinal)` presente na conta (inclusive excluído logicamente); linhas repetidas com o mesmo par dentro do arquivo: a primeira é nova, as seguintes existentes (Nubank com FITID repetido continua igual). `DistinctBy((SourceKey, Ordinal))` e ordenação `(SourceKey ordinal, Ordinal)` antes do insert; `InsertLinesAsync` grava `Ordinal`, `PurchaseDate` (`l.PurchaseDate ?? l.Date`), `StatementDate`, `InstallmentNumber`, `InstallmentTotal`, `CardSuffix`, `CardKind`; `ON CONFLICT ("AccountId","SourceKey","Ordinal")` inalterado.
    - Overrides validados por par: par inexistente na prévia, par repetido, `Ordinal < 0` ⇒ `InvalidOverride`.
    - `FitidInOtherAccount` só quando `!ImportSources.UsesContentKeys(source)` (prévia e confirm).
    - `ConfirmAsync`: se algum aviso bloqueante recalculado tem `Overridable == false` ⇒ `Fail(BlockingWarnings, blocking)` mesmo com `acknowledgeBlockingWarnings` (gatilho real vem na Task 7).
    - Avisos do leitor (`parsed.Warnings`) entram na lista da prévia; batch grava `DueDate`/`DeclaredTotal` de `parsed.Invoice` e `LedgerBalance`/`LedgerBalanceDate` de `parsed.LedgerRead`.
  - `internal sealed class ImportChecks(AppDbContext db)` (instanciada dentro do `ImportService`): `Task<HashSet<(string Key, int Ordinal)>> ExistingPairsAsync(Guid accountId, IReadOnlyCollection<string> keys, CancellationToken ct)`; `Task<int> KeysInOtherAccountsAsync(Guid accountId, IReadOnlyCollection<string> keys, CancellationToken ct)`; `Task<int> PossibleDuplicatesAsync(Guid accountId, IReadOnlyList<StoredPreviewLine> lines, CancellationToken ct)` — conta linhas **novas** do arquivo para as quais existe linha **visível** da conta (importada ou manual) com `SourceKey` nulo ou fora de todas as chaves do arquivo, mesmo `Amount`, `|Date − date| ≤ 1` dia e mesmo prefixo de 12 caracteres de `SourceKeys.NormalizeText(descrição)`; aviso `PossibleDuplicate(n)` quando n > 0 (todas as fontes).

- [ ] **Step 1: Write failing tests**
  - `SourceKeysTests.Normalization_is_frozen`: `NormalizeText("  Pão   de\tAçúcar ") == "PAO DE ACUCAR"`, `NormalizeText("ção") == "CAO"`, `NormalizeText("") == ""`.
  - `SourceKeysTests.Content_key_contract`: `ContentKey("itau-conta","2026-10-05","-10.00","PIX QRS PADARIA FICTICIA") == "48d3baf8781065ccc10dae077df3ff95df5cb5eced28ff91a4640210a51e99b0"`; `ContentKey("itau-fatura","2026-11-10","2026-10-03","-120.00","LOJA FICTICIA","2/10","1234") == "9b7512557b951f38da6f343be4ebed8cde8c1f487551e502bdfa7244da56510d"`; comprimento 64 ≤ `MaxSourceKey` (200).
  - `SourceKeysTests.Amount_and_date_format`: `FormatAmount(-10m) == "-10.00"`, `FormatAmount(-0.00m) == "0.00"`, `FormatAmount(1234.5m) == "1234.50"`; `FormatDate(new(2026,1,5)) == "2026-01-05"`.
  - `SourceKeysTests.Ordinals_count_occurrences_in_file_order`: `Ordinals(["a","b","a","a","b"])` = `[0,0,1,2,1]`.
  - `ImportIdentityTests` (ParsedStatement sintético `ItauChecking` numa conta `Checking`, chave `"K"` fixa):
    - `Same_key_different_ordinals_are_all_created`: K#0,K#1,K#2 → `created == 3`; banco tem ordinais {0,1,2}.
    - `Existing_is_decided_per_pair`: confirma K#0,K#1; nova prévia K#0..K#2 → `existing` = `[true,true,false]`, `NewCount == 1`.
    - `Ordinal_holes_are_reported_honestly`: só K#1 existe; prévia K#0,K#1 → `[false,true]`; confirm cria K#0.
    - `Override_targets_one_ordinal`: override `{K, Ordinal 1, State Normal}` em linhas Pendentes → só K#1 fica `Normal`.
    - `Override_with_unknown_or_negative_ordinal_is_invalid` (ordinal 5 e −1 ⇒ `InvalidOverride`, nada gravado); `Repeated_pair_in_overrides_is_invalid_but_sibling_ordinal_is_fine`.
    - `Key_in_other_account_blocks_only_nubank`: mesma chave já em outra conta → `ItauChecking` sem `FitidInOtherAccount`; `NubankChecking` com FITID em outra conta → aviso bloqueante (regressão).
    - `Possible_duplicate_warns_on_amount_date_and_prefix` (linha manual −10.00 em 05/10 "PIX QRS PADARIA X" × linha nova −10.00 em 06/10 "PIX QRS PADARIA Y" → `PossibleDuplicate` count 1, severidade `warning`); variantes sem aviso: valor diferente, 2 dias, prefixo diferente, linha existente de mesma chave, irmãos K#0/K#1 do mesmo arquivo, linha excluída logicamente.
    - `Metadata_reaches_transactions`: linha com `InstallmentNumber 2/10`, `CardSuffix "1234"`, `CardKind Physical`, `PurchaseDate`, `StatementDate` → colunas gravadas iguais; view da prévia expõe `ordinal`, `installmentNumber`, `installmentTotal`, `cardSuffix`, `cardKind`, `purchaseDate`.
    - `Legacy_preview_json_confirms_with_defaults`: lote em Preview com `PreviewJson` no formato S1a (`{"accountExternalId":null,"checkpoint":null,"lines":[{"fitId":"X","date":"2026-10-05","amount":-10.00,"memo":"M","state":"Normal","counterpartyName":null,"counterpartyDocument":null,"existing":false}]}`) → confirm cria 1 linha com `Ordinal 0` e metadados nulos.
    - `Reader_warnings_and_batch_summary_fields_are_kept`: `parsed.Warnings = [UnknownLineKinds(2)]`, `Invoice`, `LedgerRead` → aparecem em `Warnings`, `ImportBatch.DueDate/DeclaredTotal/LedgerBalance/LedgerBalanceDate`.
- [ ] **Step 2:** `dotnet test FinanceDashboard.sln --filter "FullyQualifiedName~SourceKeysTests|FullyQualifiedName~ImportIdentityTests"` → FAIL.
- [ ] **Step 3:** Implementar `SourceKeys`, `ImportChecks`, o novo formato de `ParsedStatement`/`ParsedLine` e a refatoração do `ImportService` pelas assinaturas acima (classificação e contraparte do Nubank movidas para o leitor).
- [ ] **Step 4:** `UPDATE_OPENAPI_SNAPSHOT=1 dotnet test FinanceDashboard.sln --filter "FullyQualifiedName~OpenApiSnapshot"`; na raiz `npm run api:types`; `types.ts`: `ImportLine` = `Omit<S["ImportLineView"], "ordinal"|"installmentNumber"|"installmentTotal"> & { ordinal: number; installmentNumber: number | null; installmentTotal: number | null }` e `ImportPreview` com `lines: ImportLine[]` (mesma justificativa de `IntFields`); `CardKind = S["CardKind"]`, `ImportInvoice = S["ImportInvoiceView"]`; `testingImport.line()` com `ordinal: 0` e metadados `null`.
- [ ] **Step 5:** `dotnet test FinanceDashboard.sln` → PASS (S1a inteira, inclusive concorrência e overrides); `npm test && npm run build:real` → PASS.
- [ ] **Step 6: Commit** `refactor(import): identidade (chave, ordinal) de ponta a ponta e aviso de possível duplicado`.

### Task 3: Leitor OFX comum, perfis por banco e Itaú conta

**Files:**
- Rename: `.../Importing/NubankOfxReader.cs` → `.../Importing/OfxReader.cs` (`git mv`; leitor genérico)
- Create: `.../Importing/StatementReader.cs`, `.../Importing/OfxProfiles.cs` (`IOfxBankProfile`, `NubankOfxProfile`, `ItauOfxProfile`, seleção), `.../Importing/ItauRules.cs`
- Modify: `.../Importing/ImportService.cs` e `backend/src/FinanceDashboard.Api/Imports/ImportEndpoints.cs` (`NubankOfxReader.MaxBytes`/`Parse` → `StatementReader`), `backend/tests/FinanceDashboard.Tests/Importing/OfxTestFile.cs` (`Itau()`, `Bank(string?)`), `NubankOfxReaderTests.cs` (helper `Parse` → `StatementReader.Read`, `.FitId` → `.SourceKey`), `LocalSamplesTests.cs` (idem, só compilação)
- Test: `backend/tests/FinanceDashboard.Tests/Importing/OfxProfileTests.cs`, `backend/tests/FinanceDashboard.Tests/Importing/ItauCheckingTests.cs`

**Interfaces:**
- Consumes: Task 2 (`ParsedStatement`, `ParsedLine`, `SourceKeys`), Task 1 (`ImportWarning` fábricas).
- Produces:
  - `static class OfxReader { const int MaxFieldChars = 2000; static OfxDocument Parse(ReadOnlySpan<byte> content) }` — mesmo scanner/limites/decodificação da S1a; captura também `BANKID` (em `BANKACCTFROM`/`CCACCTFROM`), `TRNTYPE`, `FITID` opcional e a linha de cada `STMTTRN`; não exige FITID.
  - `sealed record OfxDocument(string? BankId, bool IsCard, string? AccountId, DateOnly? PeriodStart, DateOnly? PeriodEnd, IReadOnlyList<OfxTransaction> Transactions, ParsedBalance? Ledger)`; `sealed record OfxTransaction(string? FitId, string? TrnType, DateOnly Posted, decimal Amount, string Memo, int Line)`.
  - `interface IOfxBankProfile { ParsedStatement Build(OfxDocument doc); }`; `static class OfxProfiles { static IOfxBankProfile For(OfxDocument doc) }`: `BANKID` sem zeros à esquerda `"260"` **ou ausente** → Nubank (o cartão do Nubank não traz `BANKID`); `"341"` com `IsCard == false` → Itaú; qualquer outro (inclusive Itaú cartão em OFX) → `ImportFormatException("unsupported_bank", null)`.
  - `NubankOfxProfile`: comportamento S1a (FITID ausente ⇒ `malformed` com a linha; `NubankRules`).
  - `ItauOfxProfile` (fonte `ItauChecking`): `SourceKey = SourceKeys.ContentKey("itau-conta", FormatDate(Posted), FormatAmount(Amount), NormalizeText(Memo))`; `Ordinal` = `SourceKeys.Ordinals`; sinal de `TRNAMT` mantido (FITID/CHECKNUM/TRNTYPE ignorados para identidade); `State` por `ItauRules.ClassifyChecking`; `Warnings` += `UnknownLineKinds(n)` se n > 0; `LedgerRead = Ledger`; `LedgerBalance = Ledger` só se nenhuma linha tem `Date > Ledger.AsOf`, senão `null` + `LedgerBalanceBeforeLastLine()`; `AccountExternalId = ACCTID`.
  - `static class ItauRules`: `(TransactionState State, bool Known) ClassifyChecking(string memo)` — sobre `NormalizeText(memo)`, prefixos Pendente `FATURA PAGA`, `PIX TRANSF`, `PIX CARTAO`, `PAG BOLETO`, `CREDITO LIBERAD`, `EST CRED`, `CREDITO CONSIGNADO`, `APLICACAO`, `RESGATE`; prefixos Normal conhecidos `PIX QRS`, `REND PAGO`, `REMUNERACAO SALARIO`; resto = `(Normal, false)`. Lista única no arquivo.
  - `static class StatementReader { const int MaxBytes = 5 * 1024 * 1024; static ParsedStatement Read(ReadOnlySpan<byte> content) }` — nesta tarefa só OFX (vazio → `empty`; acima do limite → `too_large`; o resto como `OfxReader`); Task 7 acrescenta o ramo zip/CFB.
  - Builder: `OfxTestFile.Itau()` = conta com `Bank("0341")`, `Unclosed()`, `Charset("1252")`, `AccountId("12345")`; `Bank(string? id)` (null omite a folha); `Card()` continua sem `BANKID`.

- [ ] **Step 1: Write failing tests**
  - `OfxProfileTests`: `Bankid_0260_selects_nubank_checking`; `Card_without_bankid_selects_nubank_card`; `Bankid_0341_selects_itau_checking`; `Other_bankid_is_unsupported_bank` (`Bank("0001")` ⇒ `Kind == "unsupported_bank"`); `Itau_card_ofx_is_unsupported_bank`; `Nubank_line_without_fitid_is_malformed_with_line`.
  - `ItauCheckingTests`:
    - `Parses_unclosed_sgml_cp1252_with_accents_intact` (memo `"PIX TRANSF JOÃO FICTICIO"` em CP1252 → `Memo` igual).
    - `Dates_are_literal_ignoring_timezone` (`"20261031235959[-3:BRT]"` → 2026-10-31).
    - `Four_identical_lines_same_day_become_four_ordinals` (mesma data/valor/memo ×4 → ordinais `[0,1,2,3]`, mesma `SourceKey`; uma 5ª com memo diferente → ordinal 0).
    - `Key_ignores_fitid_and_checknum` (dois arquivos com FITIDs diferentes para as mesmas linhas → mesmas chaves).
    - `Classifies_pending_prefixes` (Theory com os 9 prefixos, débito e crédito, inclusive `"PIX TRANSF"` entrada e saída e `"crédito liberad"` minúsculo/acentuado → `PendingClassification`); `Known_normal_prefixes_are_normal_without_unknown_count` (`PIX QRS`, `REND PAGO`, `REMUNERACAO SALARIO`); `Unknown_lines_are_normal_and_counted` (2 desconhecidas → `UnknownLineKinds`, mensagem contém `2`).
    - `Ledger_becomes_checkpoint_when_no_line_after_asof`; `Ledger_before_last_line_warns_and_has_no_checkpoint` (`LedgerBalance == null`, `LedgerRead != null`, aviso `LedgerBalanceBeforeLastLine` não bloqueante).
    - `Reader_exceptions_carry_no_content` (memo com marcador + `TRNAMT` inválido → `Message` contém `malformed` e a linha, não o marcador).
  - `NubankOfxReaderTests` existentes inalterados nos asserts (regressão).
- [ ] **Step 2:** `dotnet test FinanceDashboard.sln --filter "FullyQualifiedName~Importing"` → FAIL.
- [ ] **Step 3:** Implementar `OfxReader`, perfis, `ItauRules.ClassifyChecking`, `StatementReader`; `ImportService`/endpoints usam `StatementReader`.
- [ ] **Step 4:** Integração curta em `ItauCheckingTests` (Postgres): `Itau_ofx_upload_into_checking_account_previews_and_confirms` (HTTP, conta `Checking` com `externalIdentifier "00012345"` e arquivo `ACCTID 12345` → sem `AccountIdentifierMismatch`; `"99999"` → bloqueante com override); `Itau_ofx_into_credit_card_account_is_account_type_mismatch` (422); `Itau_ledger_mismatch_against_previous_checkpoint_warns` (checkpoint manual anterior + linhas que não fecham → `BalanceMismatch` não bloqueante); `Itau_ledger_before_last_line_creates_no_checkpoint_on_confirm` (`Checkpoints` vazio; lote com `LedgerBalance` gravado). Rodar suíte completa → PASS.
- [ ] **Step 5: Commit** `feat(import): leitor OFX comum com perfis e extrato da conta Itaú`.

### Task 4: Pré-validação segura do zip/xlsx

**Files:**
- Create: `backend/src/FinanceDashboard.Infrastructure/Importing/Xlsx/XlsxSafety.cs`, `backend/tests/FinanceDashboard.Tests/Importing/XlsxTestFile.cs` (builder sintético; partes XML escritas à mão via `System.IO.Compression.ZipArchive`)
- Test: `backend/tests/FinanceDashboard.Tests/Importing/XlsxSafetyTests.cs`

**Interfaces:**
- Produces:
  - `static class XlsxSafety { const int MaxEntries = 200; const long MaxTotalUncompressed = 50L * 1024 * 1024; const int MaxRatio = 100; const long RatioMinBytes = 64 * 1024; static void Validate(byte[] content) }` — lança `ImportFormatException(kind, null)`:
    - assinatura CFB `D0 CF 11 E0 A1 B1 1A E1`: contém o nome UTF-16LE `EncryptedPackage` → `encrypted_xlsx`, senão `not_xlsx` (.xls); sem `PK\x03\x04` → `not_xlsx`; zip ilegível (`InvalidDataException`) → `not_xlsx`.
    - mais de `MaxEntries` entradas, soma descompactada **medida lendo com contador** (para de ler ao passar do limite; nunca confia em `Length`) > `MaxTotalUncompressed`, ou entrada com descompactado ≥ `RatioMinBytes` e razão > `MaxRatio` → `xlsx_limits_exceeded`.
    - nome com `..`, iniciado por `/` ou `\`, ou com `:` → `xlsx_unsafe_content`; entradas `xl/vbaProject.bin`, prefixos `xl/externalLinks/`, `xl/embeddings/`, `xl/activeX/`, `xl/connections.xml`; `[Content_Types].xml` com tipo contendo `macroEnabled`; qualquer `*.rels` com `TargetMode="External"`; `<!DOCTYPE` em qualquer parte `.xml`/`.rels` → `xlsx_unsafe_content`.
    - `[Content_Types].xml`, `xl/workbook.xml` e `*.rels` lidos com `XmlReader` (`DtdProcessing.Prohibit`, `XmlResolver = null`, `MaxCharactersInDocument` limitado); `xl/media/*` e `xl/drawings/*` só contados, nunca parseados; ausência de `xl/workbook.xml` → `not_xlsx`.
  - `sealed class XlsxTestFile` (testes): `static XlsxTestFile Invoice(string due = "2026-11-10", string total = "0.00")`; `Line(string date, string description, string amount, string? installment = null, string? cardKind = "Físico", string? cardNumber = "****1234", string holder = "Titular", string name = "FULANO FICTICIO")`; `WithLogo()`; `FooterFormula()`; `Date1904()`; `HiddenFirstSheet()`; `InlineStrings()`; `FormulaInLine(int index)`; `AmountAsText(int index, string text)`; `DateAsText(int index, string text)`; `BlankRowBefore(int index)`; `Header(params string[] labels)`; `SecondSummaryBlock()`; `WithoutSummary()`; `Entry(string name, byte[] data)`; `ContentType(string partName, string type)`; `Relationship(string relsPart, string target, bool external)`; `byte[] Build()`; estáticos `byte[] ZipBomb(long uncompressedBytes)`, `byte[] LyingSizeBomb()` (cabeçalho local declara tamanho pequeno), `byte[] EncryptedCfb()`, `byte[] LegacyXls()`, `byte[] Raw(params (string Name, byte[] Data)[] entries)`. Rótulos literais: resumo `Cartão | Valor | Vencimento`, linha seguinte `produto | total | vencimento (serial)`; linha `Lançamentos`; cabeçalho `Data | Lançamento | Parcelamento | Valor | Titularidade | Nome | Tipo do cartão | Número do cartão`; rodapé com texto `Total` e fórmula `SUM(...)` com valor em cache.

- [ ] **Step 1: Write failing tests** — `Valid_synthetic_invoice_with_logo_passes`; `Cfb_encrypted_is_encrypted_xlsx`; `Legacy_xls_is_not_xlsx`; `Random_bytes_are_not_xlsx`; `Truncated_zip_is_not_xlsx`; `Too_many_entries_is_limits_exceeded` (201 entradas); `Zip_bomb_is_limits_exceeded` (`ZipBomb(60 MB)`); `Lying_declared_size_is_caught_by_counting` (`LyingSizeBomb()`); `High_ratio_entry_is_limits_exceeded`; `Small_repetitive_part_below_ratio_threshold_passes`; `Path_traversal_and_absolute_names_are_unsafe` (Theory: `"../x.xml"`, `"/x.xml"`, `"xl\\..\\x"`, `"C:x"`); `Macro_parts_are_unsafe` (Theory: `xl/vbaProject.bin`, content type `application/vnd.ms-excel.sheet.macroEnabled.main+xml`); `External_links_connections_embeddings_activex_are_unsafe` (Theory); `External_relationship_is_unsafe`; `Doctype_in_any_xml_part_is_unsafe`; `Missing_workbook_is_not_xlsx`; `Exception_messages_are_kind_only` (nenhuma mensagem contém nome de entrada).
- [ ] **Step 2:** `dotnet test FinanceDashboard.sln --filter "FullyQualifiedName~XlsxSafetyTests"` → FAIL.
- [ ] **Step 3:** Implementar `XlsxSafety` e o builder.
- [ ] **Step 4:** Mesmo filtro → PASS; teste de bomba termina em < 2 s e sem alocar o total (ler em blocos de 64 KB para um sink).
- [ ] **Step 5: Commit** `feat(import): pré-validação segura de zip/xlsx`.

### Task 5: Leitor SAX genérico da planilha

**Files:**
- Modify: `backend/src/FinanceDashboard.Infrastructure/FinanceDashboard.Infrastructure.csproj` (`<PackageReference Include="DocumentFormat.OpenXml" Version="<3.x estável mais recente, exata>" />`; anotar versão e licença MIT no `away-log.md`)
- Create: `backend/src/FinanceDashboard.Infrastructure/Importing/Xlsx/XlsxSheetReader.cs`, `.../Importing/Xlsx/ExcelDates.cs`
- Test: `backend/tests/FinanceDashboard.Tests/Importing/XlsxSheetReaderTests.cs`, `backend/tests/FinanceDashboard.Tests/Importing/ExcelDatesTests.cs`

**Interfaces:**
- Consumes: Task 4 (`XlsxSafety.Validate` é chamado **antes**; `XlsxTestFile`).
- Produces:
  - `enum XlsxCellKind { Empty, Number, Text, Other }`; `sealed record XlsxCell(int Column, XlsxCellKind Kind, string? Text, decimal? Number, bool HasFormula)` (coluna 0-based; `Other` = booleano/erro); `sealed record XlsxRow(int Number, IReadOnlyList<XlsxCell> Cells) { XlsxCell? At(int column) }`; `sealed record XlsxSheet(bool Date1904, IReadOnlyList<XlsxRow> Rows)`.
  - `static class XlsxSheetReader { const int MaxRows = 5000; const int MaxColumns = 64; const int MaxCellChars = 2000; const int MaxSharedStrings = 50_000; const long MaxSharedStringChars = 5_000_000; static XlsxSheet ReadFirstVisibleSheet(byte[] content) }` — `SpreadsheetDocument.Open(stream, false)`; primeira planilha com `State` nulo/`Visible`; `date1904` de `WorkbookProperties`; `sharedStrings` e a planilha lidas com `OpenXmlReader` (SAX); valor **em cache** (`<v>`), fórmula só marca `HasFormula`; número parseado com `NumberStyles.Float` invariante para `decimal` (falha/overflow → `invalid_cell` com a linha); `t="s"`/`inlineStr`/`str` → `Text`; `b`/`e` → `Other`; limites → `xlsx_limits_exceeded`; nenhuma planilha visível → `unsupported_layout`.
  - `static class ExcelDates { static DateOnly FromSerial(decimal serial, bool date1904, int row) }` — parte inteira; sistema 1900: serial 60 (29/02/1900 fantasma) → `invalid_cell`, `< 60` base 1899-12-31, `> 60` base 1899-12-30; 1904: base 1904-01-01; serial ≤ 0 ou fora de `DateOnly` → `invalid_cell`.

- [ ] **Step 1: Write failing tests**
  - `ExcelDatesTests`: `FromSerial(46000m,false) == 2025-12-09`; `FromSerial(1m,false) == 1900-01-01`; `FromSerial(59m,false) == 1900-02-28`; `FromSerial(61m,false) == 1900-03-01`; `FromSerial(60m,false)` ⇒ `invalid_cell`; `FromSerial(44538m,true) == 2025-12-09` (1904 = 1900 − 1462); `FromSerial(46000.75m,false) == 2025-12-09`; `FromSerial(0m,false)` ⇒ `invalid_cell`.
  - `XlsxSheetReaderTests`: `Reads_shared_and_inline_strings_and_numbers`; `Formula_cell_returns_cached_value_with_flag`; `Detects_date1904`; `Skips_hidden_first_sheet`; `Too_many_rows_is_limits_exceeded`; `Oversized_cell_or_shared_strings_is_limits_exceeded`; `Number_parse_is_invariant` (`<v>1234.5</v>` → 1234.5m, `<v>1E-2</v>` → 0.01m); `Bad_numeric_cell_is_invalid_cell_with_row_only` (mensagem contém a linha e não o texto).
- [ ] **Step 2:** `dotnet test FinanceDashboard.sln --filter "FullyQualifiedName~XlsxSheetReaderTests|FullyQualifiedName~ExcelDatesTests"` → FAIL.
- [ ] **Step 3:** Implementar.
- [ ] **Step 4:** Mesmo filtro → PASS; `dotnet list package --vulnerable` sem achados para o pacote novo.
- [ ] **Step 5: Commit** `feat(import): leitura SAX de planilha xlsx (Open XML SDK) e datas do Excel`.

### Task 6: Fatura do Itaú — layout, inversão de sinal, parcelas, cartão e identidade

**Files:**
- Create: `backend/src/FinanceDashboard.Infrastructure/Importing/Xlsx/ItauInvoiceLayout.cs`, `backend/src/FinanceDashboard.Infrastructure/Importing/ItauInvoiceReader.cs`
- Modify: `backend/src/FinanceDashboard.Infrastructure/Importing/ItauRules.cs`
- Test: `backend/tests/FinanceDashboard.Tests/Importing/ItauInvoiceLayoutTests.cs`, `backend/tests/FinanceDashboard.Tests/Importing/ItauInvoiceReaderTests.cs`

**Interfaces:**
- Consumes: Task 5 (`XlsxSheet`, `ExcelDates`), Task 4 (`XlsxSafety`, `XlsxTestFile`), Task 2 (`SourceKeys`, `ParsedStatement`).
- Produces:
  - `sealed record RawInvoiceLine(int Row, DateOnly Date, string Description, int? InstallmentNumber, int? InstallmentTotal, decimal FileAmount, CardKind? CardKind, string? CardSuffix)`; `sealed record RawInvoice(DateOnly DueDate, decimal DeclaredTotal, IReadOnlyList<RawInvoiceLine> Lines)`.
  - `static class ItauInvoiceLayout { static RawInvoice Parse(XlsxSheet sheet) }` — rótulos comparados com `SourceKeys.NormalizeText`:
    - resumo: linha com células de texto `CARTAO`, `VALOR`, `VENCIMENTO`; a linha seguinte dá total (número, coluna de `VALOR`) e vencimento (serial, coluna de `VENCIMENTO`); ausente ou mais de um bloco → `unsupported_layout`; total/vencimento não numérico → `invalid_cell`.
    - cabeçalho: linha cujos textos são todos do conjunto `DATA`, `LANCAMENTO`, `PARCELAMENTO`, `VALOR`, `TITULARIDADE`, `NOME`, `TIPO DO CARTAO`, `NUMERO DO CARTAO` e inclui `DATA` e `LANCAMENTO`; obrigatórios `DATA`, `LANCAMENTO`, `VALOR` (falta → `unsupported_layout`); texto não reconhecido na linha → `unsupported_layout`. `TITULARIDADE`/`NOME` reconhecidos e **descartados**.
    - tabela: da linha seguinte ao cabeçalho até a primeira linha cuja célula `DATA` não é número (vazia ou texto); texto com cara de data (`^\d{1,2}/\d{1,2}/\d{2,4}$`) → `invalid_cell`; depois do fim, qualquer linha com número em `DATA` **e** célula em `VALOR` → `unsupported_layout` (impede descartar lançamentos depois de linha em branco).
    - por linha: `Valor` precisa ser `Number` (texto como `"R$ 1.234,56"` → `invalid_cell`); arredondado a 2 casas, diferença > 0,000001 → `invalid_cell`; `HasFormula` em qualquer célula do resumo ou da tabela → `invalid_cell`; fórmulas depois do fim da tabela ignoradas; `Parcelamento` vazio = à vista, senão `^PARCELA (\d{1,3}) DE (\d{1,3})$` com `1 ≤ N ≤ M` (não casar → `invalid_cell`); `Tipo do cartão`: `FISICO` → `Physical`, `VIRTUAL` → `Virtual`, `VIRTUAL RECORRENTE` → `VirtualRecurring`, outro não vazio → `Other`, vazio → null; `Número do cartão` `^\*+(\d{4})$` → sufixo, vazio → null, outro → `invalid_cell`; `Lançamento` vazio → `invalid_cell`.
  - `ItauRules` (acréscimos): `TransactionState ClassifyInvoice(string description, decimal normalizedAmount)` (primeiro token de `NormalizeText` == `PAGAMENTO` **e** `normalizedAmount > 0` → Pendente; prefixos `SALDO ANTERIOR`, `SALDO ROTATIVO` → Pendente; resto Normal); `DateOnly InvoiceLineDate(DateOnly purchaseDate, DateOnly dueDate, int? installmentNumber)` (N > 1 → `dueDate.AddMonths(-1)`, senão `purchaseDate`); `bool InvoiceTotalMatches(decimal declaredTotal, IEnumerable<(decimal Amount, TransactionState State)> lines)` (`declaredTotal == -Σ Amount` das linhas não Pendentes).
  - `static class ItauInvoiceReader { static ParsedStatement Read(byte[] content) }` — `XlsxSafety.Validate` → `XlsxSheetReader` → `ItauInvoiceLayout`; `Amount = -FileAmount`; `Date = InvoiceLineDate`; `PurchaseDate = linha`; `StatementDate = DueDate`; `SourceKey = ContentKey("itau-fatura", FormatDate(due), FormatDate(linha), FormatAmount(Amount), NormalizeText(desc), parcela ? $"{N}/{M}" : "", suffix ?? "")`; `Ordinal` = `SourceKeys.Ordinals`; fonte `ItauCard`; `PeriodStart/End = null`; `AccountExternalId = null`; `Invoice = new(due, total)`; `Warnings` += `InvoiceTotalMismatch()` quando `!InvoiceTotalMatches`.

- [ ] **Step 1: Write failing tests**
  - `ItauInvoiceLayoutTests`: `Parses_summary_header_and_lines_by_label` (colunas embaralhadas no builder → mesmo resultado); `Footer_formula_is_ignored`; `Formula_in_line_is_invalid_cell`; `Amount_as_text_is_invalid_cell`; `Date_as_text_inside_table_is_invalid_cell`; `Subtotal_row_ends_table`; `Blank_row_followed_by_more_lines_is_unsupported_layout`; `Missing_required_header_is_unsupported_layout` (Theory sem `Data`/`Lançamento`/`Valor`); `Unknown_header_label_is_unsupported_layout`; `Two_summary_blocks_is_unsupported_layout`; `Missing_summary_is_unsupported_layout`; `Installment_regex_is_case_insensitive` (`"parcela 2 de 10"`) and `Invalid_installment_is_invalid_cell` (`"Parcela 11 de 10"`, `"2/10"`); `Card_kind_mapping` (Theory 5 casos, inclusive `"Físico"` acentuado); `Card_number_masked_suffix` (`"****1234"` → `"1234"`, `"1234"` → `invalid_cell`); `Date1904_invoice_reads_same_dates`; `Holder_and_name_are_not_kept` (nenhum campo de `RawInvoiceLine` contém `"FULANO FICTICIO"`).
  - `ItauInvoiceReaderTests`:
    - `Inverts_sign_both_ways`: compra `120.00` → `-120.00` Normal; pagamento `-500.00` `"PAGAMENTO EFETUADO"` → `500.00` Pendente; estorno `-30.00` `"ESTORNO LOJA FICTICIA"` → `30.00` Normal.
    - `Pagamento_requires_whole_token_and_positive_amount` (`"PAGAMENTOS ONLINE FICTICIO"` cobrança → Normal; `"PAGAMENTO X"` cobrança positiva no arquivo → Normal).
    - `Saldo_anterior_is_pending`; `Encargos_iof_juros_anuidade_are_normal`.
    - `Installment_after_first_is_dated_due_minus_one_month` (vencimento 2026-11-10, compra 2026-03-15 `"Parcela 3 de 10"` → `Date 2026-10-10`, `PurchaseDate 2026-03-15`, `StatementDate 2026-11-10`, `InstallmentNumber 3`, `InstallmentTotal 10`); `First_installment_and_cash_keep_purchase_date`; `Due_on_31st_minus_one_month_clamps` (vencimento 2026-03-31 → 2026-02-28).
    - `Identical_lines_get_increasing_ordinals`; `Key_contract_matches_spec_components` (linha 2026-10-03 `"Loja Ficticia"` `120.00` `"Parcela 2 de 10"` `****1234`, vencimento 2026-11-10 → mesmo hash da fatura fixado em `SourceKeysTests.Content_key_contract` da Task 2; o teste compara com a constante compartilhada `SourceKeysTests.InvoiceContractHash`).
    - `Declared_total_equal_to_non_pending_sum_has_no_warning` (compras + estorno + IOF, pagamento fora da soma); `Total_mismatch_is_blocking_overridable_warning`.
    - `Invoice_has_no_period_and_no_checkpoint` (`PeriodStart/End`, `LedgerBalance`, `LedgerRead` nulos; `Invoice` preenchido).
    - `Reader_exceptions_carry_no_cell_content` (marcadores em descrição/valor-texto/nome → nenhuma mensagem de exceção os contém).
- [ ] **Step 2:** `dotnet test FinanceDashboard.sln --filter "FullyQualifiedName~ItauInvoice"` → FAIL.
- [ ] **Step 3:** Implementar.
- [ ] **Step 4:** Mesmo filtro → PASS.
- [ ] **Step 5: Commit** `feat(import): leitor da fatura xlsx do Itaú`.

### Task 7: Serviço e API aceitando a fatura (despacho, conferências com banco, códigos)

**Files:**
- Modify: `.../Importing/StatementReader.cs` (ramo zip/CFB), `.../Importing/ImportService.cs`, `.../Importing/ImportChecks.cs`, `backend/src/FinanceDashboard.Api/Imports/ImportEndpoints.cs` (summary do upload menciona OFX Nubank/Itaú e xlsx da fatura Itaú), `backend/openapi/v1.json`, `src/real/api/schema.d.ts` (gerado)
- Modify (testes): `backend/tests/FinanceDashboard.Tests/Importing/ImportTestApi.cs` (`UploadAsync(c, accountId, content, fileName = "extrato.ofx")`, `ConfirmWithAsync(c, batchId, bool ack, params object[] overrides)`)
- Test: `backend/tests/FinanceDashboard.Tests/Importing/ItauInvoiceImportTests.cs`

**Interfaces:**
- Consumes: Task 6 (`ItauInvoiceReader.Read`, `ItauRules.InvoiceTotalMatches`), Task 2 (`ImportChecks`, `Overridable`).
- Produces:
  - `StatementReader.Read`: começa com `PK\x03\x04` ou assinatura CFB → `ItauInvoiceReader.Read` (que valida e lança `not_xlsx`/`encrypted_xlsx`/…); senão OFX. Arquivo que não é nenhum → `not_ofx` (como hoje).
  - `ImportChecks`: `Task<int> MissingInvoiceLinesAsync(Guid accountId, DateOnly dueDate, IReadOnlyList<StoredPreviewLine> lines, CancellationToken ct)` — linhas da conta com `StatementDate == dueDate`, `SourceKey != null`, **não** excluídas logicamente (editadas inclusive), cujo par não está no arquivo; `Task<int> UnknownCardSuffixLinesAsync(Guid accountId, IReadOnlyList<StoredPreviewLine> lines, CancellationToken ct)` — 0 se a conta não tem nenhuma linha com `CardSuffix`; senão quantas linhas do arquivo têm sufixo fora do conjunto da conta.
  - Prévia (fonte `ItauCard`): avisos `InvoiceHasMissingLines(n)` (bloqueante, não sobrescrevível) e `UnknownCardSuffix(n)`; `DateOutsidePeriod`, `PeriodGap/Overlap`, `BalanceMismatch` e checkpoint **não** se aplicam; `StoredPreview.Invoice` preenchido; `ImportPreviewView.Invoice`/`ImportBatchSummary.Invoice` vindos de `DueDate/DeclaredTotal` do lote.
  - Confirm (`BlockingWarningsAsync`): recalcula `AccountIdentifierMismatch`, `FitidInOtherAccount` (só Nubank), `InvoiceTotalMismatch` (de `stored.Invoice` + linhas guardadas via `InvoiceTotalMatches`) e `InvoiceHasMissingLines` (banco atual); não sobrescrevível ⇒ `409 blocking_warnings` mesmo com ack.
  - HTTP: todos os `ImportFormatException.Kind` novos saem como `422 {error: kind}` (já é o caminho do `ErrorKind`); `.xlsx` acima de 5 MB → `413 too_large`.

- [ ] **Step 1: Write failing tests** (`ItauInvoiceImportTests`, HTTP sobre Postgres, conta `CreditCard`)
  - `Xlsx_upload_previews_invoice_with_metadata` (`source == "ItauCard"`, `invoice.dueDate`, `invoice.declaredTotal` string, `periodStart == null`, linha com `installmentNumber`, `cardSuffix "1234"`, `cardKind "Physical"`, `purchaseDate`, `ordinal`).
  - `Xlsx_into_checking_account_is_account_type_mismatch` (422 `account_type_mismatch`).
  - `New_error_codes_reach_client` (Theory: `EncryptedCfb` → `encrypted_xlsx`; `LegacyXls` → `not_xlsx`; `ZipBomb` → `xlsx_limits_exceeded`; macro → `xlsx_unsafe_content`; cabeçalho estranho → `unsupported_layout`; fórmula em lançamento → `invalid_cell`; OFX com `BANKID 0001` → `unsupported_bank`).
  - `Total_mismatch_blocks_until_acknowledged` (409 sem ack, 200 com ack).
  - `Missing_lines_block_without_override` (confirma v1 com A,B,C; v2 sem B → aviso `InvoiceHasMissingLines` com `overridable == false`; confirm com `acknowledgeBlockingWarnings: true` → 409 `blocking_warnings`, nada gravado; desfazer v1 → nova prévia de v2 sem o aviso).
  - `Soft_deleted_line_is_not_missing` (exclui B via `DELETE /api/transactions/{id}`; v2 sem B → sem aviso).
  - `Edited_line_counts_as_missing` (edita B; v2 sem B → aviso).
  - `Missing_lines_detected_at_confirm_time` (prévia de v2 aberta antes de confirmar v1 → confirm de v2 recusado com 409).
  - `Unknown_card_suffix_warns_only_after_first_import` (1ª importação com `1234` sem aviso; 2ª com `5678` → `UnknownCardSuffix`).
  - `Superset_invoice_inserts_only_new_lines`; `Identical_invoice_reexported_is_noop` (bytes diferentes — outro timestamp no zip —, todas existentes, `created == 0`).
  - `Overrides_with_ordinal_on_invoice` (`{sourceKey, ordinal: 1, state: "Normal"}` em pagamento duplicado).
- [ ] **Step 2:** `dotnet test FinanceDashboard.sln --filter "FullyQualifiedName~ItauInvoiceImportTests"` → FAIL.
- [ ] **Step 3:** Implementar; `UPDATE_OPENAPI_SNAPSHOT=1 ... --filter OpenApiSnapshot`; `npm run api:types`.
- [ ] **Step 4:** `dotnet test FinanceDashboard.sln` → PASS; `npm test && npm run build:real` → PASS.
- [ ] **Step 5: Commit** `feat(import): fatura xlsx do Itaú na API com conferências de total, linhas faltando e cartão`.

### Task 8: Integração ponta a ponta no backend (idempotência, corrida, resumo, isolamento, logs)

**Files:**
- Test: `backend/tests/FinanceDashboard.Tests/Importing/ItauIdempotencyTests.cs`, `backend/tests/FinanceDashboard.Tests/Importing/ItauConcurrencyTests.cs`, `backend/tests/FinanceDashboard.Tests/Privacy/NoSensitiveLoggingTests.cs` (novo `[Fact]`), `backend/tests/FinanceDashboard.Tests/Persistence/UserIsolationTests.cs` (acrescentar)
- Modify: só código de produção se um teste revelar defeito (registrar no `away-log.md`).

**Interfaces:**
- Consumes: Tasks 2–7 (HTTP), `ImportTestApi.RaceAsync`, `CapturingLoggerProvider`, `PersistenceTestSupport`.
- Produces: nenhuma interface nova.

- [ ] **Step 1: Write the tests**
  - `ItauIdempotencyTests`:
    - `Full_flow_preview_confirm_with_overrides_undo_reimport` (OFX Itaú: confirma com override de ordinal, desfaz, reimporta → mesmo estado de linhas).
    - `Reimport_n_times_is_noop` (3×: `created` = `[n,0,0]`, contagem no banco = n).
    - `Overlapping_exports_insert_only_missing` (arquivo A: dias 1–10, B: dias 5–15 com o mesmo grupo de 3 idênticos no dia 7 → total = união, grupo com 3).
    - `Multiset_with_ordinal_holes_after_undo` (lote A com 2 idênticos, lote B com 4; desfaz A → prévia de B relata 2 novos `[false,false,true,true]`; confirma → 4 linhas).
    - `Property_reimports_and_overlaps_converge_to_max_multiplicity` (Theory seeds 1..5: `Random(seed)` gera 4 arquivos a partir de um multiconjunto base de 6 chaves com multiplicidade 1..4 e recortes de período; importa em ordem aleatória com reimportações → por chave, linhas no banco = maior multiplicidade vista; nenhuma linha idêntica perdida).
    - `Installment_counts_in_due_minus_one_month_summary` (fatura com parcela 3/10 de compra em março → `GET /api/summary?month=2026-10` inclui; `month=2026-03` não; pagamento Pendente fora das despesas).
    - `Pending_checking_lines_stay_out_of_summary` (`FATURA PAGA`, `PIX TRANSF` crédito → fora de receitas/despesas; contagem em `pending`).
  - `ItauConcurrencyTests`: `Concurrent_confirms_of_overlapping_files_never_duplicate` (duas prévias de arquivos diferentes com o mesmo grupo de 4 idênticos, confirmadas em corrida via `RaceAsync` → 4 linhas, soma de `created` = 4 + exclusivas); `Concurrent_confirm_same_invoice_batch_twice` (um 200, outro 409 `already_confirmed`).
  - `UserIsolationTests.Itau_files_are_isolated_between_users` (mesmo arquivo por A e B: sem `FitidInOtherAccount`, cada um vê só o próprio lote/linhas; B não lê `GET /api/imports/{id}` de A → 404).
  - `NoSensitiveLoggingTests.Itau_import_flow_logs_never_contain_content` (Trace em tudo; marcadores em MEMO, ACCTID, valor, descrição da fatura, nome do titular, número do cartão `****9137`, e um xlsx com valor-texto marcador que falha como `invalid_cell`; nenhum aparece nos logs; corpo de erro 422 contém só o código).
- [ ] **Step 2:** `dotnet test FinanceDashboard.sln --filter "FullyQualifiedName~ItauIdempotency|FullyQualifiedName~ItauConcurrency|FullyQualifiedName~UserIsolation|FullyQualifiedName~NoSensitiveLogging"` → esperado PASS; qualquer FAIL é defeito: diagnosticar (systematic-debugging), corrigir e anotar.
- [ ] **Step 3:** Suíte completa → PASS.
- [ ] **Step 4: Commit** `test(import): idempotência, corrida, resumo, isolamento e logs do Itaú`.

### Task 9: Front — tipos, store e envio de `.ofx`/`.xlsx`

**Files:**
- Modify: `src/real/api/types.ts`, `src/real/stores/imports.ts`, `src/real/components/ImportUpload.vue`, `src/real/i18n/pt-BR.json`, `src/real/i18n/en-US.json`, `src/real/testingImport.ts` (`xlsx(name?)`, preview de fatura)
- Test: `src/real/stores/imports.test.ts`, `src/real/pages/Import.test.ts`

**Interfaces:**
- Consumes: contrato das Tasks 2 e 7 (`ordinal`, metadados, `invoice`, `overridable`, códigos novos).
- Produces:
  - `export type LineRef = Pick<ImportLine, "sourceKey" | "ordinal">`; `export function lineId(l: LineRef): string` (`` `${sourceKey}#${ordinal}` ``).
  - Store: `overrides: Map<string /* lineId */, LineOverride>`; `setOverride(l: LineRef, patch: LineOverride)`, `clearOverride(l: LineRef)`; `confirmBody` emite `{ sourceKey, state?, categoryId? }` e acrescenta `ordinal` **só quando ≠ 0** (corpo do Nubank idêntico ao da S1b); `blockedForGood: ComputedRef<boolean>` (algum aviso `severity === "blocking"` com `overridable === false`).
  - `export const IMPORT_WARNING_CODES: readonly string[]` = os 7 existentes + os 6 novos; cada um com `real.import.warnings.<code>` nos dois idiomas.
  - `SERVER_CODES` += `unsupported_bank`, `not_xlsx`, `encrypted_xlsx`, `xlsx_limits_exceeded`, `xlsx_unsafe_content`, `unsupported_layout`, `invalid_cell`; `CLIENT_CODES` += `unsupported_file`.
  - `validateFiles`: aceita `/\.(ofx|xlsx)$/i`; outra extensão → `"unsupported_file"`.
  - `ImportUpload.vue`: `accept=".ofx,.xlsx"`; dica por tipo da conta escolhida (`real.import.fileHint.Checking` = extrato `.ofx` Nubank/Itaú; `.CreditCard` = fatura `.ofx` Nubank ou `.xlsx` Itaú; sem conta = `real.import.fileHint.none`); textos `help`, `file`, `no_file` atualizados para OFX/xlsx.

- [ ] **Step 1: Write failing tests**
  - `imports.test.ts`: `validate_accepts_ofx_and_xlsx_any_case` (`FATURA.XLSX` ok; `fatura.xls` e `x.csv` → `unsupported_file`); `overrides_are_keyed_by_source_key_and_ordinal` (dois `{sourceKey:"k", ordinal:0|1}` com estados diferentes → dois itens); `confirm_body_omits_ordinal_zero_and_sends_nonzero` (`[{sourceKey:"k", state:"Normal"}, {sourceKey:"k", ordinal:1, state:"Normal"}]`); `blocked_for_good_when_non_overridable_blocking_warning`; `Each_known_error_code_has_a_translation_in_both_languages` (lista `brief` += os 7 códigos novos + `unsupported_file`); `Each_warning_code_has_a_translation_in_both_languages` (percorre `IMPORT_WARNING_CODES`).
  - `Import.test.ts`: `file_input_accepts_ofx_and_xlsx`; `file_hint_follows_selected_account_type`.
- [ ] **Step 2:** `npm test -- src/real/stores/imports.test.ts src/real/pages/Import.test.ts` → FAIL.
- [ ] **Step 3:** Implementar; ajustar chamadas de `setOverride/clearOverride` em `ImportPreview.vue` para passar a linha (sem mudar a UI ainda).
- [ ] **Step 4:** `npm test && npm run build:real && npm run build && node scripts/check-bundles.mjs` → PASS (inclui `pt-BR_and_en-US_have_identical_key_sets`).
- [ ] **Step 5: Commit** `feat(web): importar .ofx e .xlsx com overrides por ordinal e códigos novos`.

### Task 10: Front — prévia com parcela/cartão/fatura, bloqueio definitivo, histórico e e2e

**Files:**
- Modify: `src/real/components/ImportPreview.vue`, `src/real/components/ImportHistory.vue`, `src/real/i18n/pt-BR.json`, `src/real/i18n/en-US.json`, `src/real/e2e-smoke.test.ts`, `src/real/testingImport.ts`
- Create: `src/real/money/card.ts` (`formatCardSuffix`)
- Test: `src/real/components/ImportPreview.test.ts`, `src/real/money/card.test.ts`, `src/real/e2e-smoke.test.ts`

**Interfaces:**
- Consumes: Task 9 (`lineId`, `blockedForGood`, `IMPORT_WARNING_CODES`), `Money.vue`, `useValuesVisibility`.
- Produces:
  - `formatCardSuffix(suffix: string | null | undefined): string` → `"•••• 1234"` ou `""` (só 4 dígitos; outro valor → `""`).
  - Linhas da prévia (novas, pendentes, existentes): quando houver, "Parcela X de Y" (`real.import.installment`), tipo do cartão (`real.import.cardKinds.<CardKind>`) e `formatCardSuffix`, como **texto** (nada em atributo); `:key` das listas e `id` dos selects usam `lineId(l)`.
  - Cabeçalho da prévia de fatura: `real.import.invoice` com vencimento (`fmtDate`) e `<Money :value="invoice.declaredTotal" />`; histórico mostra o mesmo resumo na coluna Período quando `invoice` existe (período é nulo na fatura).
  - `blockedForGood`: botão Confirmar desabilitado, caixa "entendo" escondida, texto `real.import.blockedHelp` ("desfaça a importação anterior desta fatura e importe de novo").
  - Diálogo de desfazer: novo parágrafo `real.import.history.deletedKeys` (texto exato pt-BR: "Linhas que você excluiu e que pertencem a outras importações continuam ocupando a chave e não voltam ao reimportar; as excluídas desta importação voltam se você importar o arquivo de novo.").

- [ ] **Step 1: Write failing tests**
  - `card.test.ts`: `formats_four_digits`, `rejects_other_values`.
  - `ImportPreview.test.ts`: `shows_installment_card_kind_and_masked_suffix` (texto contém `Parcela 2 de 10`, `•••• 1234`, rótulo traduzido de `VirtualRecurring`; HTML não contém `1234` em nenhum atributo); `same_key_lines_have_distinct_controls_and_overrides` (dois pendentes `k#0`/`k#1`: mudar o select do segundo → `store.overrides` tem só `k#1`); `invoice_total_masked_when_hidden` (com esconder valores ligado, `outerHTML` não contém os dígitos do total); `non_overridable_blocking_hides_ack_and_disables_confirm`; `overridable_blocking_still_requires_ack` (regressão); `every_warning_code_renders_translated_text` (para cada código de `IMPORT_WARNING_CODES`, o texto não é o de `unknown`).
  - `e2e-smoke.test.ts`: novo `it("importa fatura .xlsx: parcela, cartão, override por ordinal, total escondido")` com API simulada (conta `CreditCard`, arquivo `fatura.xlsx`, prévia com duas linhas mesma chave ordinais 0/1, override no ordinal 1 → corpo do confirm contém `ordinal: 1`; esconder valores mascara o total).
- [ ] **Step 2:** `npm test -- src/real` → FAIL.
- [ ] **Step 3:** Implementar.
- [ ] **Step 4:** `npm test && npm run build:real && npm run build && node scripts/check-bundles.mjs` → PASS.
- [ ] **Step 5: Commit** `feat(web): prévia da fatura com parcela e cartão, bloqueio sem override e aviso no desfazer`.

### Task 11: Amostras locais, documentação, ADR e verificação no Compose

**Files:**
- Modify: `backend/tests/FinanceDashboard.Tests/Importing/LocalSamplesTests.cs`, `README.md` (seção "Importando Itaú": formatos, avisos, desfazer/linhas faltando, curl com `fatura.xlsx`), `docs/away-log.md` (resumo das decisões de implementação)
- Create: `docs/adr/0005-identidade-por-conteudo-e-leitores-itau.md`

**Interfaces:**
- Consumes: tudo acima. Não produz interface.

- [ ] **Step 1: Escrever os testes locais** (`[LocalSamplesFact]`, asserts com mensagens estáticas, nada de conteúdo em saída):
  - `Real_samples_parse_and_roundtrip` passa a usar `StatementReader.Read` para `*.ofx` e `*.xlsx`; aceita as 4 fontes.
  - `Real_itau_checking_samples_are_consistent`: linhas > 0, datas 2000–2100, `TRNTYPE` coerente com o sinal (verificado no `OfxDocument`), toda linha com chave de 64 hex, ordinais contíguos por chave.
  - `Real_itau_invoices_have_matching_totals`: nenhuma com `InvoiceTotalMismatch`; parcelas N>1 com `Date` = vencimento − 1 mês; sem `invalid_cell`/`unsupported_layout`.
  - `Real_samples_reimport_is_noop` (`[Collection(PostgresCollection.Name)]`): para cada amostra, prévia+confirm numa conta do tipo certo e de novo → 2ª com `created == 0`.
- [ ] **Step 2:** `dotnet test FinanceDashboard.sln --filter "FullyQualifiedName~LocalSamples"` na máquina com `finance-samples/` → PASS (sem amostras: Skip). Se falhar, corrigir o leitor com fixture **sintética** que reproduz só a estrutura e anotar no `away-log.md`.
- [ ] **Step 3:** ADR 0005 (contexto: formatos sem ID confiável; decisão: chave SHA-256 de conteúdo normalizado + ordinal, identidade por par no pipeline, perfis por `BANKID`, pré-validação de zip antes do Open XML SDK, fatura com linhas faltando bloqueia; consequências: normalização congelada, buracos de ordinal relatados, hash de arquivo não garante idempotência em xlsx; alternativas: contagem por chave, substituir conjunto da fatura, ClosedXML/EPPlus). README atualizado e link para o ADR.
- [ ] **Step 4: Verificação no Compose** (sem criar dados): `docker compose up -d --build`; `curl -s -o /dev/null -w "%{http_code}" http://127.0.0.1:<porta>/api/health` → `200`; `curl -s http://127.0.0.1:<porta>/api/openapi/v1.json | grep -c "invoice"` ≥ 1; `curl -s -o /dev/null -w "%{http_code}" -X POST http://127.0.0.1:<porta>/api/imports` → `401`/`400` (sem sessão, nada gravado). Nunca `down -v`.
- [ ] **Step 5:** Suíte completa backend + `npm test && npm run build && npm run build:real && node scripts/check-bundles.mjs` → PASS; gitleaks limpo.
- [ ] **Step 6: Commit** `docs: ADR 0005, README do Itaú e suíte local de amostras`.
