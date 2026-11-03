# S3(b) Pendências residuais Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Boleto e Pix para terceiros entram como Normal; Pix no crédito, Pix para a própria conta e pagamento de fatura sem extrato da conta passam a ser tratados por vínculos (inclusive de lado vazio) e pelo botão "é meu" com aprendizado, e a fila de Pendências só guarda o que exige decisão.

**Architecture:** Estende o modelo de vínculos da S3(a): `Link` ganha lados opcionais e o tipo `PixCredit`; `LinkMatcher` ganha coerência de nome e o pareamento dentro da mesma conta; `LinkService` reclassifica linhas, cria vínculos de lado vazio (marcações do usuário) e sugestões por apelido (`OwnAlias`); neutralidade passa a valer para linhas em qualquer estado. API, front e demo acompanham.

**Tech Stack:** .NET 10, EF Core + Npgsql, xUnit + Testcontainers (`postgres:17.11-trixie`), Vue 3 + Pinia + vue-i18n + vitest, openapi-typescript.

**Spec:** `docs/superpowers/specs/2026-11-03-finance-v2-s3b-pendencias-residuais-design.md` (continua `2026-11-03-finance-v2-s3a-conciliacao-design.md` e ADR 0007). Ler os três.

## Global Constraints

- Branch `feat/v2-s3b-pendencias-residuais` (já criado, com o spec). Commits terminam com as duas linhas: `Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>` e `Claude-Session: https://claude.ai/code/session_01LyReQVTVdSLwPd47UPx4Ls`.
- Constantes: `LinkMatcher.WindowDays = 3`, `AutoTransferDays = 1`, `PixCreditWindowDays = 1`, apelido/coerência de nome com o texto mais curto de **no mínimo 7 caracteres** (prefixo mútuo, normalizado: maiúsculas, sem acento, espaços colapsados).
- Dinheiro `numeric(18,2)`, igualdade exata. Dados sintéticos apenas; **nunca imprimir conteúdo de `finance-samples/` ou `.env`**. gitleaks pelo PowerShell antes de push (`C:\Users\JPST\AppData\Local\Microsoft\WinGet\Packages\Gitleaks.Gitleaks_Microsoft.Winget.Source_8wekyb3d8bbwe\gitleaks.exe git --config .gitleaks.toml --log-opts "main..HEAD"`).
- Isolamento por usuário (`IUserOwned` + filtro global); 404 para recurso de outro usuário; escritas com `AntiforgeryFilter.ValidateAsync`; nomes de terceiros nunca como HTML.
- Comandos: backend `cd backend && dotnet test`; front `npm test`, `npm run build`, `npm run build:real`, `node scripts/check-bundles.mjs`, `npm run api:types`. Snapshot OpenAPI: `UPDATE_OPENAPI_SNAPSHOT=1 dotnet test --filter OpenApiSnapshot`; fixtures do demo: `UPDATE_DEMO_FIXTURES=1 dotnet test --filter DemoFixtures` (nunca na CI). Migração: `cd backend && dotnet tool restore && dotnet ef migrations add <Nome> -p src/FinanceDashboard.Infrastructure -s src/FinanceDashboard.Api`.
- Property tests: `[Theory]` com `seed` (sem FsCheck). i18n em `src/real/i18n/pt-BR.json` e `en-US.json` com as mesmas chaves.
- Cada commit deve deixar a suíte verde: o snapshot OpenAPI e as fixtures do demo são atualizados na tarefa que muda o contrato/semente (ruling de preflight, como na S3(a)).

## Review Focus

1. Pagamento e recebimento de terceiros de mesmo valor (aluguel × freela, dia 0/1, bancos diferentes, nomes incoerentes): vira `Suggested`, nunca `Confirmed`/`Auto`, e as duas linhas continuam contando no resumo. Teste na Task 1 e na Task 4.
2. "É meu" antes da contrapartida chegar: a outra ponta, importada depois, forma par com a linha de lado vazio (confirmado se coerente, sugestão caso contrário) e nunca fica contando sem aviso. Task 4.
3. Pix no crédito marcado como Normal pelo usuário: o vínculo não é recriado. Task 4.
4. Reclassificar nunca toca linha editada pelo usuário nem manual, e é idempotente. Task 4.
5. Linha `Normal` com vínculo `Confirmed` sai de receita, despesa e categorias do resumo, mas saldos não mudam. Task 5.

---

## File Structure

- Domain: `Link.cs` (lados anuláveis, `LinkKind.PixCredit`, `PairingRole` novos), `LinkMatcher.cs` (coerência de nome, `PixCredit`), `OwnAlias.cs`, `NameKeys.cs` (normalização/prefixo mútuo, puro).
- Infrastructure: `Importing/NubankRules.cs`, `Importing/ItauRules.cs` (classificação), `Importing/PairingRules.cs` (papéis novos, `CounterpartyKey`), `Linking/LinkService.cs` (reclassificação, candidatos, upgrade, marcações, apelidos), `AppDbContext.cs`, `QueryExtensions.cs`, migração `S3bOpenLinks`.
- API: `Links/LinkEndpoints.cs`, `Transactions/TransactionEndpoints.cs` (ações `own-transfer`/`no-counterpart`, campo `Pairing`), `Summary/SummaryEndpoints.cs`, `openapi/v1.json`.
- Front: `src/real/api/*`, `stores/links.ts`, `pages/Pending.vue`, `components/{LinkSuggestions,LinkedPairs,TransactionTable}.vue`, i18n, `src/demo-static/*`.
- Docs: ADR `0007` (atualizar) ou `0008-pendencias-residuais.md`, README, `docs/away-log.md`.

---

### Task 1: Domínio — lados opcionais, `PixCredit`, coerência de nome

**Files:**
- Modify: `backend/src/FinanceDashboard.Domain/Link.cs`, `LinkMatcher.cs`
- Create: `backend/src/FinanceDashboard.Domain/NameKeys.cs`, `OwnAlias.cs`
- Test: `backend/tests/FinanceDashboard.Tests/Domain/LinkMatcherTests.cs` (estender), `NameKeysTests.cs`

**Interfaces:**
- Produces:
  - `Link.OutTransactionId`/`InTransactionId` passam a `Guid?`; `LinkKind` ganha `PixCredit`; `bool Link.IsOpen => OutTransactionId is null || InTransactionId is null` (lado vazio).
  - `PairingRole` ganha `PixCreditCredit`, `PixCreditDebit`.
  - `PairingCandidate` ganha `string? NameKey` (último parâmetro, com default `null`) — mantém as chamadas antigas compilando.
  - `class OwnAlias : IUserOwned { Guid Id; string UserId; string Fragment; DateTimeOffset CreatedAt; }`
  - `static class NameKeys { public const int MinLength = 7; public static string? Normalize(string? raw); public static bool Coincide(string? a, string? b); }` — `Normalize`: maiúsculas, sem acento, espaços colapsados, trim; `null` se vazio. `Coincide`: ambos não nulos, o mais curto tem `MinLength` ou mais caracteres e é prefixo do outro.
  - `LinkMatcher.PixCreditWindowDays = 1`.

Regras novas de `LinkMatcher.Match` (além das da S3(a)): (a) `PixCredit` = `PixCreditCredit` (valor > 0) com `PixCreditDebit` (valor < 0), `Source` ambos `ItauChecking`, **mesma conta permitida e exigida**, `|Δdata| ≤ PixCreditWindowDays`; auto-confirma sozinho em componente de um par, senão `Suggested`. (b) `OwnTransfer` só auto-confirma se, além das regras da S3(a) (um par no componente, `|Δ| ≤ AutoTransferDays`), `NameKeys.Coincide(out.NameKey, in.NameKey)`; caso contrário o par entra `Suggested`. (c) a regra "contas diferentes" continua valendo para `InvoicePayment` e `OwnTransfer`.

- [ ] **Step 1: Testes (falhando)** — `LinkMatcherTests`: `PixCredit_same_account_single_pair_is_auto_confirmed`; `PixCredit_estorno_pairs_credit_with_EST_CRED_same_day`; `PixCredit_different_accounts_do_not_match`; `PixCredit_outside_window_does_not_match` (Δ = 2); `PixCredit_two_debits_one_credit_suggests_both`; `OwnTransfer_with_incoherent_names_is_suggested_not_auto` (Review Focus 1: Itaú `ALUGUEL` × Nubank `CLIENTE X`, Δ = 0); `OwnTransfer_with_coherent_names_is_auto_confirmed` (`MARIA L` prefixo de `MARIA LUISA SANTOS`); `OwnTransfer_without_name_is_suggested`; `OwnTransfer_name_shorter_than_7_is_not_coherent`; atualizar os testes da S3(a) que esperavam auto-confirmação de `OwnTransfer` sem nome para passarem `NameKey`; `Property_…(int seed)` existente continua verde (estendê-lo com `PixCredit`). `NameKeysTests`: normalização (caixa, acento, espaços), `Coincide` (prefixo nos dois sentidos, <7, nulo, iguais).
- [ ] **Step 2: Rodar** `dotnet test --filter "LinkMatcherTests|NameKeysTests"` → falha por tipos ausentes.
- [ ] **Step 3: Implementar** as mudanças de `Link`, `PairingRole`, `PairingCandidate`, `NameKeys`, `OwnAlias` e `LinkMatcher` com as assinaturas acima (nulos nos lados de `Link` exigem ajustes de compilação nos usos existentes em `Infrastructure`/`Api`/testes: tratar `.Value` ou `HasValue` de forma mínima, sem mudar comportamento; o comportamento novo vem nas tasks seguintes).
- [ ] **Step 4: Rodar** `cd backend && dotnet test` completo → verde.
- [ ] **Step 5: Commit** `feat(domain): vínculos com lado vazio, PixCredit e coerência de nome`.

---

### Task 2: Regras de classificação e papéis (`NubankRules`, `ItauRules`, `PairingRules`)

**Files:**
- Modify: `backend/src/FinanceDashboard.Infrastructure/Importing/NubankRules.cs`, `ItauRules.cs`, `PairingRules.cs`
- Test: `NubankRulesTests.cs`, `ItauCheckingTests.cs`, `PairingRulesTests.cs`, e testes de importação que afirmam estado pendente de Pix/boleto

**Interfaces:**
- Produces:
  - `NubankRules.Classify`: "transferencia enviada/recebida" **deixa de ser pendente** (passa a `Normal`); continuam pendentes "pagamento de fatura", "pagamento recebido", "pix no credito".
  - `ItauRules.ClassifyChecking`: `PIX TRANSF` e `PAG BOLETO` → `Normal` (mantêm `Known = true`); demais entradas inalteradas.
  - `PairingRules.RoleOf(source, originalDescription, amount)`: novos retornos `PixCreditCredit` (`ItauChecking`, prefixo `CREDITO LIBERAD`, valor > 0) e `PixCreditDebit` (`ItauChecking`, prefixos `PIX CARTAO` ou `EST CRED`, valor < 0); papéis existentes inalterados.
  - `PairingRules.CounterpartyKey(ImportSource source, string originalDescription): string?`: Itaú `PIX TRANSF`: texto após o prefixo, sem o sufixo `\s\d{2}\s\d{2}$`, normalizado por `NameKeys.Normalize`; Nubank `Transferência enviada/recebida pelo Pix`: nome extraído por `NubankRules.ExtractCounterparty`, normalizado; outros → `null`.

- [ ] **Step 1: Testes (falhando)**: tabela de classificação nova (Itaú: `PIX TRANSF` e `PAG BOLETO` → Normal; `PIX CARTAO`, `CREDITO LIBERAD`, `EST CRED`, `FATURA PAGA`, `CREDITO CONSIGNADO`, `APLICACAO`, `RESGATE` continuam Pendente; Nubank: transferências Normal, "pagamento de fatura"/"pagamento recebido"/"pix no credito" Pendente); `RoleOf` para `CREDITO LIBERAD PIX 1234` (+) → `PixCreditCredit`, `PIX CARTAO Exemplo20 08` (−) e `EST CRED LIB PIX 1234` (−) → `PixCreditDebit`, sinais errados → `null`; `CounterpartyKey`: `PIX TRANSF Maria L11 08` e `PIX TRANSF MaRia L19 08` → `MARIA L` (mesma chave apesar da caixa e do sufixo), nome vazio → `null`, Nubank com acento e espaços; atualizar os testes de importação existentes que esperavam `PendingClassification` para esses prefixos.
- [ ] **Step 2: Rodar** `--filter "NubankRulesTests|ItauCheckingTests|PairingRulesTests"` → falha.
- [ ] **Step 3: Implementar** as mudanças nas três classes; reutilizar a normalização existente (`SourceKeys.NormalizeText` / `NubankRules.Normalize`), sem terceira normalizadora de texto de memo; `NameKeys.Normalize` só para chaves de nome.
- [ ] **Step 4: Rodar** `dotnet test` completo; ajustar as fixtures do demo só na Task 7 (se a `Committed_fixtures_match_generator` falhar aqui por causa da nova classificação, regenerar com `UPDATE_DEMO_FIXTURES=1` e revisar o diff: só estado/pendentes).
- [ ] **Step 5: Commit** `feat(import): Pix e boleto entram como Normal; papéis de Pix no crédito`.

---

### Task 3: Persistência — lados opcionais, `OwnAliases`, migração `S3bOpenLinks`

**Files:**
- Modify: `backend/src/FinanceDashboard.Infrastructure/AppDbContext.cs`, `QueryExtensions.cs`
- Create: migração `S3bOpenLinks`
- Test: `backend/tests/FinanceDashboard.Tests/Persistence/LinkPersistenceTests.cs` (estender), `OwnAliasPersistenceTests.cs`

**Interfaces:**
- Produces: `DbSet<OwnAlias> OwnAliases`; colunas `Links.OutTransactionId`/`InTransactionId` anuláveis (FKs cascade mantidos); índice único `(OutTransactionId, InTransactionId)` continua (nulos distintos); índices parciais únicos por coluna com `State = 'Confirmed'` continuam (nulos ignorados); novo índice único parcial em `OutTransactionId` onde `InTransactionId IS NULL AND State <> 'Rejected'`... **decidir o mínimo necessário** para impedir dois vínculos de lado vazio vivos (`Suggested` ou `Confirmed`) na mesma linha, e idem para `InTransactionId`; `OwnAliases` único `(UserId, Fragment)`, filtro global por usuário; `QueryExtensions.WithoutConfirmedLink` inalterado (qualquer vínculo `Confirmed`, de um ou dois lados, em qualquer coluna) e novo `WithoutPairedLink(db)` (exclui só linhas com `Confirmed` de **dois** lados).

- [ ] **Step 1: Testes (falhando)** (Postgres real): `Open_link_with_null_side_is_saved`; `Two_live_open_links_on_same_line_violate_unique` (SQLSTATE 23505); `Rejected_open_link_does_not_block_new_one`; `Open_confirmed_blocks_second_confirmed_on_same_line`; `OwnAlias_unique_per_user_and_fragment`; `OwnAlias_isolated_between_users`; `WithoutConfirmedLink_excludes_open_confirmed_lines`; `WithoutPairedLink_keeps_lines_with_only_open_confirmed_link`; a migração aplica sobre uma base com dados da S3(a) (`MigrationTests`: inserir link com os dois lados antes, migrar, ler).
- [ ] **Step 2: Rodar** `--filter "LinkPersistenceTests|OwnAlias"` → falha.
- [ ] **Step 3: Implementar** mapeamentos e gerar a migração com o comando do cabeçalho; conferir que `Up` só altera as duas colunas para anuláveis, recria o índice necessário e cria `OwnAliases`; `Down` simétrico.
- [ ] **Step 4: Rodar** `dotnet test` completo → verde (inclui `MigrationTests` e a comparação de snapshot do modelo).
- [ ] **Step 5: Commit** `feat(infra): lados opcionais em Links e tabela OwnAliases`.

---

### Task 4: `LinkService` — reclassificação, candidatos, upgrade, marcações e apelidos

**Files:**
- Modify: `backend/src/FinanceDashboard.Infrastructure/Linking/LinkService.cs`
- Test: `backend/tests/FinanceDashboard.Tests/Linking/LinkServiceTests.cs`, `ImportLinkingTests.cs` (estender)

**Interfaces:**
- Consumes: `LinkMatcher`, `PairingRules` (papéis, `CounterpartyKey`), `NameKeys`, `OwnAlias`, regras de classificação (Task 2).
- Produces:
  - `record ReconcileResult(int Linked, int Suggested, int Reclassified)`.
  - `Task<LinkOutcome> MarkOwnAsync(Guid transactionId, CancellationToken ct = default)` e `Task<LinkOutcome> MarkNoCounterpartAsync(Guid transactionId, CancellationToken ct = default)` retornando `LinkResult(LinkOutcome Outcome, Guid? LinkId)` (ou equivalente que exponha o id do vínculo criado; manter `LinkOutcome { Ok, NotFound, Conflict }` e adicionar `NotApplicable` para "linha sem papel adequado").

Comportamento (spec "Design"): 
1. **Reclassificação** no início de `ReconcileAsync`: linhas importadas (com lote), `!EditedByUser`, visíveis, `Pendente`, cuja regra atual (`NubankRules.Classify`/`ItauRules.ClassifyChecking`/`ClassifyInvoice` conforme `ImportBatch.Source`) devolve `Normal` → `Normal`; conta `Reclassified`.
2. **Candidatas** a pareamento: visíveis, importadas, sem vínculo `Confirmed` **de dois lados** (`WithoutPairedLink`), papel ≠ null; papéis `Transfer` em qualquer estado, os demais só `Pendente`. `PairingCandidate.NameKey` = `PairingRules.CounterpartyKey`.
3. **Upgrade**: ao aplicar a saída do matcher, para cada par `AutoConfirm`: apagar os vínculos de lado vazio (`Confirmed` ou `Suggested`) das duas linhas e criar o par `Confirmed`/`Auto`; para par `Suggested`: manter os vínculos de lado vazio e criar a sugestão; `ConfirmAsync` de um par apaga os vínculos de lado vazio das duas linhas e as demais sugestões.
4. **Sugestão por apelido**: após o pareamento, para cada candidata `Transfer` sem nenhum vínculo vivo (`Suggested`/`Confirmed`, de qualquer lado) e sem `Rejected` de lado vazio, se `NameKey` coincide (`NameKeys.Coincide`) com algum `OwnAlias` do usuário ⇒ vínculo de lado vazio `Suggested` (`Origin = Auto`, `Kind = OwnTransfer`). Sugestões são recalculadas a cada reconciliação (apagar as `Suggested` de lado vazio antes, como as de par).
5. `MarkOwnAsync`: linha visível do usuário com papel `Transfer`, sem `Confirmed`; cria `Confirmed` de lado vazio (`OwnTransfer`, `User`; lado preenchido pelo sinal: valor < 0 ⇒ `Out`, > 0 ⇒ `In`), apaga sugestões vivas da linha e qualquer `Rejected` de lado vazio dela, grava `OwnAlias` (`NameKey` ≥ 7 caracteres, idempotente) e roda `ReconcileAsync` para sugerir as outras linhas coincidentes; `Conflict` se já tem `Confirmed`; `NotApplicable` se sem papel/sem `NameKey` utilizável para o apelido **não impede** o vínculo (apenas não grava apelido).
6. `MarkNoCounterpartAsync`: linha com papel de fatura (`InvoiceAccountSide`/`InvoiceCardSide`), sem `Confirmed`; cria `Confirmed` de lado vazio (`InvoicePayment`, `User`), sem apelido.
7. `RejectAsync`/`UndoAsync`/`ConfirmAsync` funcionam para vínculos de lado vazio (confirmar uma sugestão de lado vazio: checar conflito com `Confirmed` da linha; desfazer marca `Rejected`).
8. `InvalidateAsync(transactionId, reason)` com `enum InvalidateReason { ValueOrDate, Hidden, StateToNormal }`: `ValueOrDate` apaga vínculos de par `Suggested`/`Confirmed` e sugestões automáticas de lado vazio, **preserva** `Confirmed` de lado vazio de origem `User`; `Hidden` apaga todos os vínculos da linha; `StateToNormal` apaga os vínculos da linha só se o papel dela não é `Transfer`. Sempre seguido de `ReconcileAsync`. Ajustar os chamadores existentes (`TransactionEndpoints`) para passar o motivo.

- [ ] **Step 1: Testes (falhando)**, em `LinkServiceTests`/`ImportLinkingTests`: `Reclassify_moves_unedited_pending_pix_and_boleto_to_normal`, `Reclassify_leaves_edited_and_manual_lines` e `Reclassify_is_idempotent` (Review Focus 4); `Normal_pix_pair_with_coherent_names_is_confirmed_and_both_leave_summary_inputs` e `Pix_third_party_same_amount_incoherent_names_stays_suggested_and_normal` (Review Focus 1, via importação de um OFX Itaú e um Nubank sintéticos); `MarkOwn_creates_open_confirmed_link_and_alias`; `MarkOwn_then_counterpart_import_pairs_and_replaces_open_link` e `MarkOwn_then_incoherent_counterpart_keeps_open_link_and_suggests_pair` (Review Focus 2); `Alias_suggests_open_link_for_matching_line_never_confirms`; `Alias_prefix_match_across_banks`; `Rejected_open_link_blocks_alias_suggestion_for_that_line`; `Undo_open_link_marks_rejected`; `MarkNoCounterpart_creates_invoice_open_link`; `MarkNoCounterpart_requires_invoice_role`; `PixCredit_pair_via_import_in_both_orders`; `PixCredit_line_set_to_normal_is_not_relinked` (Review Focus 3); `Invalidate_value_edit_preserves_user_open_link`; `Invalidate_hidden_removes_everything`; `Invalidate_state_to_normal_ignored_for_transfer_role`; `Confirm_pair_removes_open_links_of_both_lines`; `Concurrent_mark_own_creates_single_confirmed`; `Other_user_cannot_mark_foreign_line`.
- [ ] **Step 2: Rodar** `--filter "LinkServiceTests|ImportLinkingTests"` → falha.
- [ ] **Step 3: Implementar** o comportamento acima em `LinkService` (mesma trava por usuário e mesma regra de transação ambiente da S3(a)); atualizar `ImportService.ConfirmAsync` para `ConfirmResult(..., Linked, Suggested)` inalterado e repassar `Reclassified` apenas no endpoint de reconciliação.
- [ ] **Step 4: Rodar** `dotnet test` completo → verde (o snapshot OpenAPI muda só na Task 5).
- [ ] **Step 5: Commit** `feat(linking): reclassificação, vínculos de lado vazio e apelidos`.

---

### Task 5: API — marcações, DTOs, neutralidade em qualquer estado

**Files:**
- Modify: `backend/src/FinanceDashboard.Api/Links/LinkEndpoints.cs`, `Transactions/TransactionEndpoints.cs`, `Summary/SummaryEndpoints.cs`, `QueryExtensions.cs` (`ExcludingConfirmedPending` → versão que exclui por vínculo `Confirmed` em **qualquer estado** para receita/despesa/categorias), `openapi/v1.json`
- Test: `LinkApiTests.cs` (estender), testes de `Summary` e `Transactions`

**Interfaces:**
- Consumes: `LinkService` (Task 4).
- Produces (HTTP):
  - `LinkResponse`: `Out`/`In` anuláveis (`LinkLine?`), novo `External: bool`, `Kind` inclui `PixCredit`.
  - `POST /api/transactions/{id}/own-transfer` e `POST /api/transactions/{id}/no-counterpart` → 200 `LinkResponse`; 404 (outra pessoa/inexistente); 409 (já tem `Confirmed`); 422/400 conforme o padrão do projeto para `NotApplicable`.
  - `POST /api/links/reconcile` → `{ linked, suggested, reclassified }`.
  - `TransactionResponse.Pairing: "OwnTransfer" | "InvoicePayment" | "PixCredit" | null` (papel da linha via `PairingRules`, só linhas importadas visíveis; **declarar o campo como obrigatório no OpenAPI**, sem default `= null`, e fazer o mesmo para `Link`, corrigindo o Minor da S3(a)).
  - `GET /api/summary`: linhas `Normal` ou `Pendente` com vínculo `Confirmed` (um ou dois lados) não entram em receita, despesa, `net` nem `byCategory`; contagem/total de pendentes continuam sem as vinculadas. Saldos e checkpoints inalterados. `GET /api/transactions?state=PendingClassification` idem.
  - `UpdateAsync`/`DeleteAsync` passam o `InvalidateReason` certo (valor/data ⇒ `ValueOrDate`; ocultar/excluir ⇒ `Hidden`; estado → `Normal` ⇒ `StateToNormal`).

- [ ] **Step 1: Testes (falhando)**: `Own_transfer_endpoint_creates_open_confirmed_link_and_summary_drops_the_line` (Review Focus 5; linha Normal de −800 sai de despesa e categoria, saldo igual); `No_counterpart_endpoint_on_pending_invoice_payment`; 404/409/401/antiforgery das duas ações; `Reconcile_endpoint_reports_reclassified`; `Summary_excludes_normal_lines_with_confirmed_open_link_and_pair_link`; `Undo_open_link_brings_line_back_to_summary`; `Transaction_response_pairing_values` (Pix Itaú, boleto → null, `CREDITO LIBERAD` → PixCredit); `Links_list_shows_external_flag_and_null_side`; `Update_value_keeps_user_open_link_but_state_normal_on_invoice_role_removes_it`.
- [ ] **Step 2: Rodar** `--filter "LinkApiTests|Summary|Transactions"` → falha.
- [ ] **Step 3: Implementar** endpoints/DTOs/filtros; atualizar o snapshot: `UPDATE_OPENAPI_SNAPSHOT=1 dotnet test --filter OpenApiSnapshot`, conferir o diff (aditivo + `link`/`pairing` em `required`), rodar sem a variável. Se as fixtures do demo divergirem, regenerar com `UPDATE_DEMO_FIXTURES=1` e revisar o diff (somente `pairing`/`link`/`external`).
- [ ] **Step 4: Rodar** `dotnet test` completo → verde.
- [ ] **Step 5: Commit** `feat(api): ações é meu e sem contrapartida, neutralidade em qualquer estado`.

---

### Task 6: Front real

**Files:**
- Modify: `src/real/api/{schema.d.ts (npm run api:types),types.ts,client.ts,http.ts}`, `src/real/stores/{links.ts,transactions.ts}`, `src/real/pages/{Pending.vue,Transactions.vue}`, `src/real/components/{LinkSuggestions,LinkedPairs,TransactionTable}.vue`, i18n pt-BR/en-US, `src/demo-static/staticApi.ts` (stubs `readOnly()` para as ações novas)
- Test: ao lado dos arquivos

**Interfaces:**
- Consumes: endpoints da Task 5.
- Produces: `ApiClient.transactions.markOwn(id): Promise<LinkResponse>`, `markNoCounterpart(id)`, `links.reconcile(): Promise<{linked,suggested,reclassified}>`; store: `useLinksStore().reconcile()`, `useTransactionsStore().markOwn(id)`/`markNoCounterpart(id)` (recarregam links e lista/contagem de pendentes).

Comportamento: Lançamentos mostra a ação "É meu" nas linhas com `pairing === 'OwnTransfer'` sem vínculo; ação "Sem contrapartida" nas linhas pendentes com `pairing === 'InvoicePayment'` (Pendências e Lançamentos); selos "Transferência", "Pagamento de fatura", "Pix no crédito" e as variantes de lado vazio ("conta fora do app", "sem extrato"); Pendências: botão "Reconciliar pendências" com resumo ("N ligadas, M sugestões, K reclassificadas"), cartões de sugestão de uma linha só (lado vazio) com Confirmar/Rejeitar, e `LinkedPairs` mostra vínculos de lado vazio com Desfazer; plurais corretos via vue-i18n (corrigir "1 sugestões"/"1 pares" da S3(a) no resultado da importação e no novo resumo); textos de terceiros sempre como texto.

- [ ] **Step 1: `npm run api:types`** e escrever os testes (falhando): store (markOwn, markNoCounterpart, reconcile recarregam links e pendentes; 404/409 mapeados), componentes (botão "É meu" só com `pairing === 'OwnTransfer'` sem vínculo; "Sem contrapartida" só em pendente com `InvoicePayment`; cartão de lado vazio; selos novos; plural 0/1/2; XSS com `<b>` literal), página Pendências (botão Reconciliar chama a API e mostra o resumo).
- [ ] **Step 2: Rodar** `npm test` → falha.
- [ ] **Step 3: Implementar** client/http/store/componentes/páginas/i18n; completar o stub `readOnly()` no adaptador estático.
- [ ] **Step 4: Rodar** `npm test`, `npm run build`, `npm run build:real`, `node scripts/check-bundles.mjs` → verdes.
- [ ] **Step 5: Commit** `feat(front): é meu, sem contrapartida e reconciliar pendências`.

---

### Task 7: Demo estático

**Files:**
- Modify: `backend/tests/FinanceDashboard.Tests/DemoFixtures/{DemoSeed.cs,DemoFixtureGenerator.cs,DemoFixturesTests.cs}`, `src/demo-static/{fixtures.ts,staticApi.ts}` + testes, fixtures regeneradas

**Interfaces:**
- Consumes: API real (Task 5). Produces: `links.json` com vínculos de lado vazio e `PixCredit`; `transactions.json` com `pairing`.

A semente sintética ganha: um Pix para si mesmo de lado vazio `Confirmed` (criado pelo `LinkService.MarkOwnAsync` real, com apelido), uma sugestão de lado vazio por apelido (outra linha com o mesmo trecho), um par `PixCredit` (`CREDITO LIBERAD`/`PIX CARTAO` no Itaú, mesmo dia e valor), um pagamento de fatura sem contrapartida (`no-counterpart`), um boleto e um Pix para terceiro `Normal` (sem vínculo). Tudo gerado pelos serviços reais; ids de vínculo renomeados de forma determinística como na S3(a). Adicionar ao gerador a conferência (falha se divergir) de que os ids da fila `state=PendingClassification` do servidor são exatamente `transactions.filter(state=Pendente && link?.state !== 'Confirmed')` (corrige o Minor T8-1 da S3(a)). O adaptador estático filtra a fila por `link.state` como já faz; ações de escrita seguem `readOnly()`.

- [ ] **Step 1: Testes**: fixtures contêm um vínculo `PixCredit` `Confirmed`, um de lado vazio `Confirmed`, uma sugestão de lado vazio, um `InvoicePayment` de lado vazio; `pairing` presente nas linhas; `staticApi.links.list` filtra e pagina; e2e do demo abre Pendências e Lançamentos e vê selos/sugestão sem requisição a `/api`; `Committed_fixtures_match_generator` verde.
- [ ] **Step 2: Rodar** os testes → falham.
- [ ] **Step 3: Implementar** semente/gerador/adaptador; regenerar com `UPDATE_DEMO_FIXTURES=1` e revisar o diff (sintético: "Exemplo"/"Fictício", nada que pareça real); rodar sem a variável.
- [ ] **Step 4: Rodar** `cd backend && dotnet test` completo, `npm test`, `npm run build`, `npm run build:real`, `node scripts/check-bundles.mjs` → verdes.
- [ ] **Step 5: Commit** `feat(demo): exemplos de Pix no crédito, é meu e sem contrapartida`.

---

### Task 8: Documentação e fechamento

**Files:**
- Modify: `docs/adr/0007-vinculos-e-neutralidade-derivada.md` (atualizar) **ou** Create `docs/adr/0008-pendencias-residuais.md`; `README.md`; `docs/away-log.md`

- [ ] **Step 1: ADR 0008** (curto): Pix e boleto como Normal; neutralidade em qualquer estado; vínculo de lado vazio e por que (A em vez de marca/estado); apelido (prefixo mútuo ≥ 7, só sugestão, entre bancos); coerência de nome para auto-confirmar `OwnTransfer`; `PixCredit` (mesma conta, Δ ≤ 1, só `Pendente`); reclassificação idempotente; consequências e limitações (Pix no crédito neutro sem fatura importada some até importá-la; colisão de apelido gera sugestão; sem tela para apagar apelidos; CPF mascarado não usado). Atualizar o trecho do ADR 0007 que diz que `OwnTransfer` confirma só por valor/data. README: seção "Conciliação" com o fluxo novo (é meu, sem contrapartida, reconciliar) e a nota de que linhas antigas pendentes são reclassificadas pelo botão.
- [ ] **Step 2: Verificação completa** (skill `superpowers:verification-before-completion`): `cd backend && dotnet test`, `npm test`, `npm run build`, `npm run build:real`, `node scripts/check-bundles.mjs`, gitleaks.
- [ ] **Step 3: Revisão final do branch** com revisor novo (modelo mais capaz); uma onda de correções; re-revisão escopada; ledger com rulings.
- [ ] **Step 4: Resumo de 5 linhas no `docs/away-log.md`**, memória do projeto atualizada (S3(b) feita; S3(c) e S4 aguardam o João).
- [ ] **Step 5: Push do branch, PR** (corpo pt-BR com as duas linhas de atribuição), esperar CI verde, mesclar com merge commit e **parar**.

---

## Self-Review

- **Cobertura do spec:** classificação (T2), papéis/`CounterpartyKey` (T2), matcher com `PixCredit` e coerência de nome (T1), lados opcionais/índices/`OwnAliases`/migração (T3), reclassificação/candidatos/upgrade/marcações/apelidos/invalidação (T4), API/neutralidade em qualquer estado/`Pairing`/reconcile (T5), interface (T6), demo (T7), docs (T8). Não-objetivos sem tarefa.
- **Tipos consistentes:** `PairingRole.PixCreditCredit/PixCreditDebit`, `LinkKind.PixCredit`, `PairingCandidate.NameKey`, `NameKeys.Coincide`, `ReconcileResult(Linked, Suggested, Reclassified)`, `InvalidateReason`, `LinkOutcome` (+`NotApplicable`), `WithoutPairedLink` usados com os mesmos nomes entre tarefas.
- **Riscos anotados:** T1/T2 mudam o comportamento de auto-confirmação e classificação da S3(a) (testes existentes precisam acompanhar); T3 mexe em colunas e índices de uma tabela já em produção local (migração sem perda); a janela de nome ≥ 7 pode deixar pares legítimos como sugestão.
- **Proporção:** apenas assinaturas, regras e testes nomeados; corpos ficam com o implementador.
