# S3(a) Conciliação (fatura e transferência entre contas próprias) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ligar as duas pontas de cada pagamento de fatura e de cada transferência entre contas próprias (vínculo), automático quando inequívoco e sugerido quando ambíguo, tirando essas linhas da fila de pendências.

**Architecture:** Tabela `Link` (par saída/entrada, tipo, estado). Um `LinkMatcher` puro no Domínio propõe pares; um `LinkService` na Infraestrutura o aplica (sob trava por usuário) dentro da transação de confirmação da importação e num endpoint de reconciliação. Neutralidade é derivada de vínculo `Confirmed`, sem campo novo em `Transaction`. Front e demo ganham as seções Sugestões/Vinculados e o selo na tabela.

**Tech Stack:** .NET 10, EF Core + Npgsql, xUnit + Testcontainers (Postgres 17.11-trixie), Vue 3 + Pinia + vue-i18n + vitest, openapi-typescript.

**Spec:** `docs/superpowers/specs/2026-11-03-finance-v2-s3a-conciliacao-design.md` (pai: `2026-11-02-finance-v2-backend-design.md`). Ler os dois.

## Global Constraints

- Branch `feat/v2-s3a-conciliacao` (já criado, com o chore e o spec). Commits terminam com as duas linhas: `Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>` e `Claude-Session: https://claude.ai/code/session_01LyReQVTVdSLwPd47UPx4Ls`.
- Dinheiro `decimal`/`numeric(18,2)`, igualdade exata; trafega como string na API. Datas `DateOnly`.
- Janela de pareamento `WindowDays = 3`; auto-confirmação de `OwnTransfer` só com `|Δdata| ≤ AutoTransferDays = 1`.
- Dados sintéticos apenas em testes/fixtures; nunca ler/imprimir `finance-samples/` ou `.env`. gitleaks limpo antes de push (rodar `gitleaks` pelo PowerShell; no Bash do projeto ele não está no PATH).
- Isolamento por usuário igual ao resto (`IUserOwned` + filtro global); vínculo de outro usuário = 404.
- Escritas da API com `AntiforgeryFilter.ValidateAsync`.
- Strings de terceiros nunca como HTML; dinheiro no front só via `Money.vue`.
- Comandos: backend `cd backend && dotnet test`; front `npm test`, `npm run build`, `npm run build:real`, `npm run api:types`. Snapshot OpenAPI: `UPDATE_OPENAPI_SNAPSHOT=1 dotnet test --filter OpenApiSnapshot`. Fixtures do demo: `UPDATE_DEMO_FIXTURES=1 dotnet test --filter DemoFixtures` (nunca na CI).
- Property tests neste repo são `[Theory]` com `seed` (sem FsCheck): seguir `ItauIdempotencyTests.Property_…`.
- Mensagens ao usuário e docs em pt-BR; i18n em `src/real/i18n/pt-BR.json` e `en-US.json`.

## Review Focus

1. Pix a terceiro com valor igual ao de um recebimento no outro banco, dia seguinte e sem outro candidato: confirma sozinho (Δ ≤ 1); com Δ = 2 ou 3 vira `Suggested`. Teste na Task 1.
2. Dois lotes confirmados ao mesmo tempo disputando a mesma linha: no máximo um `Confirmed` por ponta, sem 500. Teste na Task 5.
3. Editar valor/data/estado ou ocultar linha vinculada apaga o vínculo e a linha volta à fila. Teste na Task 6.
4. Desfazer um lote remove vínculos; reimportar religa; vínculo `Rejected` não volta. Teste na Task 5.
5. Pagamento de fatura feito de outro banco, pagamento parcial ou a maior: ficam pendentes sem vínculo (nunca viram gasto). Teste na Task 1/5.

---

## File Structure

- Domain (`backend/src/FinanceDashboard.Domain/`): `Link.cs` (entidade + enums), `LinkMatcher.cs` (puro).
- Infrastructure: `Importing/PairingRules.cs` (descrição → papel), `Linking/LinkService.cs`, `AppDbContext.cs` (mapa de `Link`), `QueryExtensions.cs` (+ `WithoutConfirmedLink`), `Migrations/*_S3aLinks`, `Importing/ImportService.cs` (chamada na confirmação, `ConfirmResult` ganha contagens).
- API: `Links/LinkEndpoints.cs`, `Transactions/TransactionEndpoints.cs` (campo `Link`, filtro da fila, invalidação), `Summary/SummaryEndpoints.cs`, `Program.cs` (registro), `openapi/v1.json`.
- Front: `src/real/api/{types,client,http,schema.d}.ts`, `src/real/stores/links.ts`, `src/real/pages/Pending.vue`, `src/real/components/{LinkSuggestions,LinkedPairs,TransactionTable}.vue`, `src/real/pages/Import.vue` (resultado), i18n, `src/demo-static/{staticApi,fixtures}.ts` + `fixtures/links.json`.
- Tests: `backend/tests/FinanceDashboard.Tests/{Domain,Importing,Linking}/…`, `DemoFixtures/DemoSeed.cs`.
- Docs: `docs/adr/0007-vinculos-e-neutralidade-derivada.md`, `README.md`, `docs/away-log.md`.

---

### Task 1: Entidade `Link` e `LinkMatcher` (Domínio, puro)

**Files:**
- Create: `backend/src/FinanceDashboard.Domain/Link.cs`, `backend/src/FinanceDashboard.Domain/LinkMatcher.cs`
- Test: `backend/tests/FinanceDashboard.Tests/Domain/LinkMatcherTests.cs`

**Interfaces:**
- Produces:
  - `enum LinkKind { InvoicePayment, OwnTransfer }`, `enum LinkState { Suggested, Confirmed, Rejected }`, `enum LinkOrigin { Auto, User }`
  - `class Link : IUserOwned { Guid Id; string UserId; LinkKind Kind; LinkState State; Guid OutTransactionId; Guid InTransactionId; LinkOrigin Origin; DateTimeOffset CreatedAt; DateTimeOffset? DecidedAt; }`
  - `enum PairingRole { InvoiceAccountSide, InvoiceCardSide, Transfer }`
  - `record PairingCandidate(Guid Id, Guid AccountId, ImportSource Source, PairingRole Role, DateOnly Date, decimal Amount)`
  - `record ProposedLink(Guid OutId, Guid InId, LinkKind Kind, bool AutoConfirm)`
  - `static class LinkMatcher { public const int WindowDays = 3; public const int AutoTransferDays = 1; public static IReadOnlyList<ProposedLink> Match(IReadOnlyList<PairingCandidate> candidates, IReadOnlySet<(Guid OutId, Guid InId)> rejected); }`

Regras de `Match` (spec, "Matcher"): par possível = saída (`Amount < 0`) × entrada (`Amount > 0`) com `Out.Amount == -In.Amount`, contas diferentes, `|Δdata| ≤ WindowDays`, par fora de `rejected`, e papéis/fontes compatíveis: `InvoicePayment` = (`InvoiceAccountSide`, `InvoiceCardSide`) do mesmo banco (NubankChecking↔NubankCard, ItauChecking↔ItauCard), qualquer das pontas pode ser a saída ou a entrada (o lado conta é negativo, o cartão positivo, mas o matcher só exige sinais opostos); `OwnTransfer` = ambos `Transfer`, ambos com `Source` em {NubankChecking, ItauChecking}. Componentes conexos do grafo de pares possíveis: componente de um único par → `AutoConfirm = true`, exceto `OwnTransfer` com `|Δdata| > AutoTransferDays` (`false`); demais componentes → um `ProposedLink` por par com `AutoConfirm = false`. Saída ordenada por (data da saída, id da saída, id da entrada); mesma entrada ⇒ mesma saída.

- [ ] **Step 1: Escrever os testes (falhando)** em `LinkMatcherTests`, com candidatas sintéticas (helpers `Out(...)`/`In(...)`): `Single_exact_invoice_pair_is_auto_confirmed`; `Invoice_pair_across_banks_does_not_match` (NubankChecking × ItauCard); `Amount_differs_by_one_cent_does_not_match` (parcial/a maior ficam sem vínculo); `Same_account_does_not_match`; `Outside_window_does_not_match` (Δ = 4); `Transfer_delta_1_is_auto_confirmed`, `Transfer_delta_2_and_3_are_suggested_not_auto` (Review Focus 1); `Two_outs_one_in_suggests_both_pairs_none_auto`; `Two_by_two_identical_suggests_all_four`; `Rejected_pair_is_not_proposed_and_leaves_the_other_candidate_unambiguous`; `Transfer_and_invoice_roles_never_mix`; `Manual_like_source_is_ignored_by_roles` (um `Transfer` com `Source = NubankCard` não casa); `Order_of_input_does_not_change_output` e `Property_random_sets_are_deterministic_and_each_line_has_at_most_one_auto_pair(int seed)` (Theory com ~20 seeds: gera 0–12 candidatas aleatórias; afirma saída idêntica sob permutação da entrada e que nenhuma linha aparece em dois pares `AutoConfirm`).
- [ ] **Step 2: Rodar** `dotnet test --filter LinkMatcherTests` → falha por tipos ausentes.
- [ ] **Step 3: Implementar** `Link.cs` e `LinkMatcher.cs` com as assinaturas acima (União-busca ou DFS para componentes; sem estado estático).
- [ ] **Step 4: Rodar** o filtro → tudo verde.
- [ ] **Step 5: Commit** `feat(domain): entidade Link e LinkMatcher puro`.

---

### Task 2: `PairingRules` (descrição → papel) e contrato de sinais

**Files:**
- Create: `backend/src/FinanceDashboard.Infrastructure/Importing/PairingRules.cs`
- Test: `backend/tests/FinanceDashboard.Tests/Importing/PairingRulesTests.cs`

**Interfaces:**
- Consumes: `ImportSource`, `NubankRules` e `ItauRules` (reutilizar a normalização existente: tornar `NubankRules.Normalize` `internal`/pública se preciso, sem copiar regex), `SourceKeys.NormalizeText`.
- Produces: `static class PairingRules { public static PairingRole? RoleOf(ImportSource source, string originalDescription, decimal amount); }`

Tabela (spec, "Candidatas e tipos"): NubankChecking "pagamento de fatura" → `InvoiceAccountSide`; NubankCard "pagamento recebido" → `InvoiceCardSide`; NubankChecking "transferencia enviada"/"transferencia recebida" → `Transfer`; ItauChecking "FATURA PAGA" → `InvoiceAccountSide`; ItauCard primeiro token `PAGAMENTO` com `amount > 0` → `InvoiceCardSide`; ItauChecking "PIX TRANSF" → `Transfer`. Qualquer outro → `null` (inclui `PIX CARTAO`, `APLICACAO`, `RESGATE`, `PAG BOLETO`, `CREDITO LIBERAD`, `EST CRED`, "pix no credito").

- [ ] **Step 1: Testes** `PairingRulesTests`: um `[Theory]` com cada linha da tabela (descrições sintéticas, caixa e acento variados) e um `[Theory]` de negativos (os `null`); `Itau_card_payment_with_negative_amount_is_not_a_role`; `Sign_contract_per_reader` (fixtures sintéticas dos leitores: lado conta negativo, lado cartão positivo, para Nubank cartão "Pagamento recebido" e Itaú fatura "PAGAMENTO"; reaproveitar `OfxTestFile` e `XlsxTestFile` e as asserções dos testes dos leitores, sem redefinir convenção).
- [ ] **Step 2: Rodar** `--filter PairingRulesTests` → falha.
- [ ] **Step 3: Implementar** `RoleOf` com a normalização única existente.
- [ ] **Step 4: Rodar** → verde. Se o contrato de sinais revelar sinal oposto ao spec, parar e registrar no diário (a premissa do matcher mudaria).
- [ ] **Step 5: Commit** `feat(import): PairingRules para candidatas a vínculo`.

---

### Task 3: Persistência (`Link`, índices, migração, `WithoutConfirmedLink`)

**Files:**
- Modify: `backend/src/FinanceDashboard.Infrastructure/AppDbContext.cs`, `QueryExtensions.cs`
- Create: migração `S3aLinks` em `Migrations/` (gerada com `dotnet ef migrations add S3aLinks`, via `dotnet-tools.json`)
- Test: `backend/tests/FinanceDashboard.Tests/Persistence/LinkPersistenceTests.cs`, ajustar `MigrationTests` se houver lista de tabelas

**Interfaces:**
- Produces: `DbSet<Link> Links`; `IQueryable<Transaction> WithoutConfirmedLink(this IQueryable<Transaction> q, AppDbContext db)` (exclui transações presentes em `Links` com `State == Confirmed` como `OutTransactionId` ou `InTransactionId`).

Mapa: tabela `Links`; `Kind`/`State`/`Origin` como string(32); FKs para `Transactions` (Out e In) com `OnDelete(DeleteBehavior.Cascade)`; índice único `(OutTransactionId, InTransactionId)`; índices únicos parciais em `OutTransactionId` e em `InTransactionId` com filtro `"State" = 'Confirmed'`; índice `(UserId, State)`; filtro global por usuário.

- [ ] **Step 1: Testes** (Postgres real): `Confirmed_unique_per_side` (segunda `Confirmed` na mesma saída/entrada viola o índice; `Suggested` repetido para o mesmo ponto é permitido); `Duplicate_pair_is_rejected`; `Deleting_transaction_cascades_links`; `Other_user_cannot_see_links`; `WithoutConfirmedLink_excludes_both_sides_only_when_confirmed`.
- [ ] **Step 2: Rodar** → falha.
- [ ] **Step 3: Implementar** mapa + extensão; `dotnet ef migrations add S3aLinks -p src/FinanceDashboard.Infrastructure -s src/FinanceDashboard.Api` (ajustar ao comando que a S2 usou, ver README); conferir que o `Up` só cria `Links` (nada destrutivo).
- [ ] **Step 4: Rodar** `dotnet test` completo → verde, incluindo `MigrationTests` e o teste que compara o modelo ao snapshot.
- [ ] **Step 5: Commit** `feat(infra): tabela Links e migração S3aLinks`.

---

### Task 4: `LinkService`

**Files:**
- Create: `backend/src/FinanceDashboard.Infrastructure/Linking/LinkService.cs` (+ registro em `InfrastructureServiceCollectionExtensions.cs`)
- Test: `backend/tests/FinanceDashboard.Tests/Linking/LinkServiceTests.cs`

**Interfaces:**
- Consumes: `LinkMatcher`, `PairingRules`, `Link`, `AppDbContext`, `TimeProvider`.
- Produces:
  - `record ReconcileResult(int Linked, int Suggested)`
  - `class LinkService(AppDbContext db, TimeProvider time)` com:
    - `Task<ReconcileResult> ReconcileAsync(CancellationToken ct = default)`: adquire `pg_advisory_xact_lock(hashtextextended(userId, 0))` (usuário do contexto), carrega candidatas (visíveis, importadas com lote, `PendingClassification`, sem `Confirmed`, `RoleOf != null`, fonte vinda de `ImportBatch.Source`), os `Rejected` e os `Suggested`; apaga os `Suggested` e insere a saída do matcher (`Confirmed`/`Origin Auto` quando `AutoConfirm`, senão `Suggested`); devolve `Linked` = novos `Confirmed`, `Suggested` = total de sugestões abertas. Deve participar da transação ambiente se já houver uma (não abrir outra).
    - `Task<LinkOutcome> ConfirmAsync(Guid id, CancellationToken ct = default)`, `RejectAsync(Guid id, …)`, `UndoAsync(Guid id, …)` com `enum LinkOutcome { Ok, NotFound, Conflict }`: Confirm só de `Suggested` (apaga os demais `Suggested` das duas pontas; `Conflict` se uma ponta já tem `Confirmed` ou o estado não é `Suggested`); Reject só de `Suggested` → `Rejected`; Undo só de `Confirmed` → `Rejected`; todos gravam `DecidedAt`, `Origin = User` no confirm.
    - `Task InvalidateAsync(Guid transactionId, CancellationToken ct = default)`: apaga todos os vínculos (qualquer estado) que citam a linha e chama `ReconcileAsync`.

- [ ] **Step 1: Testes** (Postgres real, linhas inseridas direto no contexto): `Reconcile_confirms_unambiguous_invoice_pair`; `Reconcile_is_idempotent` (duas chamadas, mesmas linhas em `Links`); `Reconcile_recomputes_suggestions_not_accumulating`; `Rejected_pair_is_not_recreated`; `Undo_marks_rejected_and_lines_return_to_pending`; `Confirm_suggested_removes_competing_suggestions`; `Confirm_conflict_returns_Conflict`; `Other_users_lines_never_pair`; `Hidden_and_manual_lines_are_not_candidates`; `Invalidate_removes_links_and_relinks_to_other_candidate`.
- [ ] **Step 2: Rodar** `--filter LinkServiceTests` → falha.
- [ ] **Step 3: Implementar** o serviço conforme as assinaturas; sem lógica de pareamento fora do `LinkMatcher`.
- [ ] **Step 4: Rodar** → verde.
- [ ] **Step 5: Commit** `feat(linking): LinkService (reconciliar, confirmar, rejeitar, desfazer)`.

---

### Task 5: Reconciliar na confirmação da importação

**Files:**
- Modify: `backend/src/FinanceDashboard.Infrastructure/Importing/ImportService.cs` (`ConfirmAsync`, `ConfirmResult`), `ImportEndpoints.cs` (DTO de resposta)
- Test: `backend/tests/FinanceDashboard.Tests/Linking/ImportLinkingTests.cs`

**Interfaces:**
- Consumes: `LinkService.ReconcileAsync`.
- Produces: `record ConfirmResult(Guid BatchId, int Created, int Skipped, int Linked, int Suggested)`.

`ConfirmAsync` chama `new LinkService(db, time).ReconcileAsync(ct)` (ou injeta `LinkService`) depois de `InsertLinesAsync`/`AddCheckpointAsync` e antes do `CommitAsync`. Resultado preenche `Linked`/`Suggested`.

- [ ] **Step 1: Testes** de integração via HTTP (`ImportTestApi`, `OfxTestFile`, `XlsxTestFile`; contas Nubank e Itaú sintéticas): `Nubank_invoice_payment_links_after_both_files_in_either_order`; `Itau_invoice_payment_links_checking_and_card_file`; `Itau_to_nubank_pix_transfer_links_when_same_day` e `_suggested_when_two_days_apart`; `Confirm_reports_linked_and_suggested_counts`; `Undo_batch_removes_its_links_and_reimport_relinks` (Review Focus 4); `Rejected_link_survives_next_import_but_not_batch_undo`; `Parcial_and_larger_payments_stay_unlinked_and_pending` e `Invoice_payment_from_other_bank_stays_pending` (Review Focus 5); `Concurrent_confirms_never_create_two_confirmed_for_one_line` (seguir `ImportConcurrencyTests`; Review Focus 2); `Existing_import_tests_still_pass` (rodar a suíte `Importing` inteira).
- [ ] **Step 2: Rodar** `--filter ImportLinkingTests` → falha.
- [ ] **Step 3: Implementar** a chamada e o DTO; ajustar `openapi` depois (Task 6).
- [ ] **Step 4: Rodar** `dotnet test --filter "FullyQualifiedName~Importing|FullyQualifiedName~Linking"` → verde (o snapshot OpenAPI falhará até a Task 6; ok).
- [ ] **Step 5: Commit** `feat(import): reconciliar vínculos na confirmação`.

---

### Task 6: API de vínculos, campo `Link` nas linhas, fila e resumo

**Files:**
- Create: `backend/src/FinanceDashboard.Api/Links/LinkEndpoints.cs`
- Modify: `Program.cs` (map + DI), `Transactions/TransactionEndpoints.cs`, `Summary/SummaryEndpoints.cs`, `Domain/MonthlySummaryCalculator.cs` (se preciso), `openapi/v1.json`
- Test: `backend/tests/FinanceDashboard.Tests/Linking/LinkApiTests.cs`, ajustes em `Summary` e `Transactions` tests

**Interfaces:**
- Consumes: `LinkService`, `WithoutConfirmedLink`.
- Produces (HTTP):
  - `GET /api/links?state=&page=&size=` → `LinkPage { Items: LinkResponse[], Page, Size, Total }`; `LinkResponse(Guid Id, LinkKind Kind, LinkState State, LinkOrigin Origin, LinkLine Out, LinkLine In, DateTimeOffset CreatedAt)`; `LinkLine(Guid TransactionId, Guid AccountId, DateOnly Date, string Description, decimal Amount)`
  - `POST /api/links/{id}/confirm`, `POST /api/links/{id}/reject` → 200 `LinkResponse` / 404 / 409; `DELETE /api/links/{id}` → 204 / 404 / 409; `POST /api/links/reconcile` → 200 `ReconcileResult`
  - `TransactionResponse` ganha `Link: TransactionLink?` = `(Guid Id, LinkKind Kind, LinkState State, Guid CounterpartTransactionId)` (só vínculos `Confirmed` e `Suggested`)
  - `GET /api/transactions?state=PendingClassification` exclui linhas com `Confirmed`; `PendingSummary.Count/Total` idem.
  - `PUT`/`DELETE /api/transactions/{id}`: se a mudança altera `Amount`, `Date` ou `State` (para `Normal`), ou oculta a linha, chamar `LinkService.InvalidateAsync(id)` na mesma unidade de trabalho.

- [ ] **Step 1: Testes** `LinkApiTests`: lista por estado e paginação; confirm/reject/undo felizes; 404 para vínculo de outro usuário e para id inexistente; 409 em conflito; 401 sem sessão; antiforgery obrigatório nas escritas; `Reconcile_endpoint_is_idempotent`; `Transaction_response_carries_link`; `Pending_list_and_summary_exclude_confirmed_linked_lines` (e inclui de novo após `DELETE`); `Edit_amount_date_or_state_deletes_link_and_returns_line_to_queue` e `Hide_line_deletes_link` (Review Focus 3); `Balances_and_income_expenses_unchanged_by_links`.
- [ ] **Step 2: Rodar** → falha.
- [ ] **Step 3: Implementar** endpoints (padrão de `TransactionEndpoints`: `WithName`, `WithSummary`, `Produces`), filtros e invalidação.
- [ ] **Step 4: Rodar** `dotnet test`; atualizar o snapshot com `UPDATE_OPENAPI_SNAPSHOT=1 dotnet test --filter OpenApiSnapshot`, conferir o diff (só o novo) e rodar a suíte inteira → verde. Regras do snapshot que exigem padrão de dinheiro nas propriedades `amount` valem para `LinkLine.Amount`.
- [ ] **Step 5: Commit** `feat(api): endpoints de vínculos, selo nas linhas e fila sem vinculadas`.

---

### Task 7: Front real (Sugestões, Vinculados, selo, resultado da importação)

**Files:**
- Modify: `src/real/api/schema.d.ts` (via `npm run api:types`), `types.ts`, `client.ts`, `http.ts`, `src/real/pages/Pending.vue`, `components/TransactionTable.vue`, `pages/Import.vue` (resultado), `stores/imports.ts` (campos novos), i18n pt-BR/en-US
- Create: `src/real/stores/links.ts`, `src/real/components/LinkSuggestions.vue`, `src/real/components/LinkedPairs.vue`, testes `*.test.ts` ao lado dos arquivos (seguir o padrão dos existentes)

**Interfaces:**
- Consumes: endpoints da Task 6.
- Produces: em `ApiClient`, `links: { list(q: { state?: LinkState; page?: number; size?: number }): Promise<LinkPage>; confirm(id: string): Promise<LinkResponse>; reject(id: string): Promise<LinkResponse>; undo(id: string): Promise<void>; reconcile(): Promise<{ linked: number; suggested: number }> }`; `useLinksStore()` com `suggested`, `confirmed`, `load()`, `confirm(id)`, `reject(id)`, `undo(id)`, `error` (padrão `errorCode.ts`).

- [ ] **Step 1: Rodar** `npm run api:types` e escrever os testes (falhando): store (carrega, confirma, rejeita, desfaz e recarrega a fila de pendências; mapeia erro 409); `LinkSuggestions` renderiza par com datas, contas, `Money` e botões Confirmar/Rejeitar; `LinkedPairs` com Desfazer; `TransactionTable` mostra selo "Transferência"/"Pagamento de fatura" só com `link.state === 'Confirmed'`; `Import` mostra "N pares vinculados, M sugestões"; descrições de terceiros aparecem como texto (teste com `<b>` literal).
- [ ] **Step 2: Rodar** `npm test` → falha.
- [ ] **Step 3: Implementar** client HTTP, store, componentes, ajustes de `Pending.vue` (seções acima da lista) e i18n.
- [ ] **Step 4: Rodar** `npm test`, `npm run build`, `npm run build:real` → verdes (o adaptador estático falhará na checagem de tipos até a Task 8: implementar `links` ali como parte desta task para manter `vue-tsc` verde, com `readOnly` nas escritas e listas vazias provisórias).
- [ ] **Step 5: Commit** `feat(front): tela de vínculos na conciliação`.

---

### Task 8: Demo estático (semente, fixtures, adaptador)

**Files:**
- Modify: `backend/tests/FinanceDashboard.Tests/DemoFixtures/DemoSeed.cs`, `DemoFixtureGenerator.cs`, `src/demo-static/fixtures.ts`, `staticApi.ts`, testes do demo (`src/demo-static/*.test.ts`)
- Create/regenerate: `src/demo-static/fixtures/links.json` e demais fixtures alteradas

**Interfaces:**
- Consumes: API real de links (o gerador chama `GET /api/links?state=Confirmed|Suggested` como o visitante do demo).
- Produces: `DemoFixtures.links: { confirmed: LinkPage; suggested: LinkPage }` (ou o formato mais simples que `staticApi.links.list` precisar), servido com filtro por `state`.

A semente sintética ganha: um pagamento de fatura Nubank (conta + cartão) que se vincula sozinho, uma transferência Itaú→Nubank de Δ = 0 que se vincula sozinha, e dois Pix de mesmo valor com data a 3 dias que ficam `Suggested`. As linhas entram pelos importadores reais (como o resto da semente) para o pareamento rodar de verdade.

- [ ] **Step 1: Testes**: `staticApi.links.list` filtra por `state`, `confirm/reject/undo/reconcile` falham com `demo_read_only`; teste de fixtures exige ao menos um vínculo de cada tipo e uma sugestão; e2e do demo (`e2e-demo.test.ts`) abre Pendências e vê as duas seções sem nenhuma requisição a `/api`; `DemoFixturesTests.Committed_fixtures_match_generator` falha até regenerar.
- [ ] **Step 2: Rodar** `npm test` e o teste das fixtures → falham.
- [ ] **Step 3: Implementar** semente/gerador/adaptador; regenerar com `UPDATE_DEMO_FIXTURES=1 dotnet test --filter DemoFixtures` (local); revisar o diff das fixtures: só dados sintéticos, nada que pareça real.
- [ ] **Step 4: Rodar** `dotnet test` (sem a variável), `npm test`, `npm run build`, `npm run build:real`, `node scripts/check-bundles.mjs` (ver `package.json`/CI para o comando exato) → verdes.
- [ ] **Step 5: Commit** `feat(demo): vínculos nas fixtures e no adaptador estático`.

---

### Task 9: ADR, README, verificação final e PR

**Files:**
- Create: `docs/adr/0007-vinculos-e-neutralidade-derivada.md`
- Modify: `README.md` (seção de conciliação), `docs/away-log.md`, memória do projeto

- [ ] **Step 1: Escrever o ADR 0007** (contexto, decisão, consequências): vínculo como tabela; neutralidade derivada de `Confirmed`; pareamento exato com janela de 3 dias; `OwnTransfer` só confirma sozinho com Δ ≤ 1; sugestões recalculadas; `Rejected` perdido ao desfazer lote; limitações (1:N, parcial, outro banco, Pix a terceiro de mesmo valor). README: como funciona e como desfazer.
- [ ] **Step 2: Verificação completa** (skill `superpowers:verification-before-completion`): `cd backend && dotnet test` (tudo verde), `npm test`, `npm run build`, `npm run build:real`, check-bundles, gitleaks pelo PowerShell (`gitleaks detect --no-banner` e o hook de pre-commit), migração aplicada num Postgres vazio e sobre uma base com dados da S2 (os testes de migração cobrem).
- [ ] **Step 3: Revisão final do branch** com um revisor novo (skill `superpowers:requesting-code-review`), corrigir achados, registrar rulings no diário (linhas `S3a/T*`).
- [ ] **Step 4: Resumo de 5 linhas no `docs/away-log.md`** (entregue / decidido sem o João / o que precisa do olhar dele / pendências / próximo passo = S3(b)), atualizar `finance-v2-project-state.md` e `finance-away-mode.md` na memória.
- [ ] **Step 5: Push do branch, abrir PR** (`gh pr create`, corpo em pt-BR com as duas linhas de atribuição), esperar CI verde, mesclar (merge commit, como os anteriores) e **parar**: não iniciar S3(b)/(c).

---

## Self-Review

- **Cobertura do spec:** modelo/índices (T3), candidatas e tabela de papéis (T2), matcher incl. regra Δ ≤ 1 (T1), serviço e trava (T4), integração na confirmação e desfazer lote (T5), API/summary/fila/invalidação (T6), interface real (T7), demo (T8), ADR/README/diário (T9). Não-objetivos não têm tarefa (correto).
- **Consistência de tipos:** `PairingRole`, `PairingCandidate`, `ProposedLink`, `LinkKind/State/Origin`, `ReconcileResult(Linked, Suggested)`, `ConfirmResult(..., Linked, Suggested)`, `WithoutConfirmedLink` e `LinkOutcome` usados com os mesmos nomes entre tarefas.
- **Risco anotado:** o contrato de sinais (T2) é a premissa do matcher; se falhar, parar e registrar.
- **Proporção:** só assinaturas, testes nomeados e valores do spec; corpos ficam com o implementador.
