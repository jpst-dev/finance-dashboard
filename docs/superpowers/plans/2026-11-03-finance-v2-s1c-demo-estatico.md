# Finance v2 — S1c: demo estático com a nova interface Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** O build `demo` (Vercel) passa a iniciar a interface real (`src/real`) sobre um `ApiClient` estático alimentado por fixtures geradas pelo backend, somente leitura, sem rede e sem login; o app legado é apagado.

**Architecture:** `src/real/` ganha só pontos de extensão (objeto `AppConfig` com `readOnly`/`hideLogout`/`fixedToday`, servido por provide/inject e por `setAppConfig`/`getAppConfig` para as stores, no mesmo padrão de `api/context.ts`; código de erro `demo_read_only`). Um teste xUnit semeia um banco Testcontainers por `DbContext` com ids fixos, chama os endpoints reais e grava JSON normalizado em `src/demo-static/fixtures/` (`UPDATE_DEMO_FIXTURES=1` grava; caso contrário compara e falha em deriva). `src/demo-static/` contém o adaptador `createStaticApi(fixtures)`, o boot do demo e o e2e; `src/main.ts` escolhe entre `real/boot` e `demo-static/boot`. Depois de verde, a tag `demo-legado` preserva o app antigo e ele é apagado.

**Tech Stack:** Vue 3 + TS + Pinia + vue-router + vue-i18n + Vitest/jsdom; .NET 10, xUnit, Testcontainers PostgreSQL, `Microsoft.Extensions.TimeProvider.Testing` (já referenciado); Node 24 (`scripts/check-bundles.mjs`).

**Spec:** `docs/superpowers/specs/2026-11-03-finance-v2-s1c-demo-estatico-design.md` (autoridade, incluindo "Decisões minhas" e "Decisões complementares"). Pais: `2026-11-02-finance-v2-backend-design.md` (seção Front-end), `2026-11-02-finance-v2-s1b-interface-design.md`, `docs/adr/0004-modo-de-build-e-camada-de-api-do-front.md`.

## Global Constraints
- Valem todas as restrições do pai/S1a/S1b/S2: dinheiro é string decimal de ponta a ponta (nunca `number`, nenhuma aritmética de dinheiro no navegador; filtrar/paginar fixtures é seleção); tipos da API gerados (`src/real/api/types.ts`); antiforgery só no HTTP; i18n pt-BR/en-US com conjuntos de chaves idênticos (`i18n.test.ts`).
- **Nada de dado real**: não abrir `finance-samples/` nem `.env`. Semente, fixtures e testes são sintéticos: nomes genéricos ("Exemplo"/"Fictício"), e-mail `visitante@exemplo.invalid`, valores inventados por fórmula determinística em `decimal` (sem `Random`). A senha do usuário do gerador é uma constante sintética do teste e nunca aparece nas fixtures.
- Data de referência do demo **2026-10-15** (mês 2026-10); fixtures cobrem **2026-05 a 2026-10**; nenhum lançamento depois de 2026-10-15; outros meses mostram o resumo vazio gerado ("sem movimento").
- Fixtures determinísticas byte a byte (CI Linux × Windows): ids fixos (nenhum `Guid.NewGuid()` no gerador), `CreatedAt`/`ExpiresAt` fixos e `FakeTimeProvider` em `2026-10-15T12:00:00Z`, JSON indentado com `JavaScriptEncoder.UnsafeRelaxedJsonEscaping`, LF, `\n` final, cultura invariante, ordem estável. `.gitattributes`: `src/demo-static/fixtures/*.json text eol=lf`.
- `ApiClient` (`src/real/api/client.ts`) é a única costura: páginas e stores não conhecem o adaptador. `src/demo-static/` **nunca** importa `src/real/boot.ts` nem `src/real/api/index.ts`/`http.ts` (usa `api/context.ts`, `api/errors.ts`, `api/types.ts`).
- Marcadores: real = `data-finance-real-mode` (só `src/real/boot.ts`); demo = `data-finance-demo-mode` (só `src/demo-static/boot.ts`); fixtures = `finance-demo-fixtures` (campo `meta.generator`); cliente HTTP = `/api/auth/csrf`.
- Erro do demo: `new ApiError(403, "demo_read_only")`; constante `DEMO_READ_ONLY_CODE` em `src/real/api/errors.ts`. Textos: `real.demo.banner` = "Demonstração com dados fictícios, somente leitura" / "Demo with fictitious data, read-only"; `real.errors.demo_read_only` = "Demonstração somente leitura: alterações não são salvas." / "Read-only demo: changes are not saved.".
- Versões fixas; nenhuma dependência nova. Remover só o que ficar sem uso (`axios`, `lodash`, `@types/lodash`, `vue-toastification`); `chart.js`/`vue-chartjs` continuam (usados por `src/real`).
- Comandos: backend em `backend/` (`dotnet test FinanceDashboard.sln`, Docker rodando); front na raiz (`npm test`, `npm run build`, `npm run build:real`, `node scripts/check-bundles.mjs`).
- Modo ausente: decisões novas de implementação vão para `docs/away-log.md` (uma linha cada, prefixo `S1c/T<n>`) no commit da tarefa; gitleaks limpo antes de cada push; sem force-push.

## Review Focus
1. **Rede no demo**: nenhum `fetch`/`XMLHttpRequest` em nenhuma tela, inclusive no `loadPendingCount` do shell e no fluxo Importar (Task 3 `no_network_used`, Task 4 `visits_every_page_without_network`); `dist/` não contém `/api/auth/csrf` (Task 5).
2. **Arquivo do visitante lido**: `imports.preview` ignora o `File`; nenhum `arrayBuffer`/`text`/`stream`/`slice`/`FileReader` é chamado (Task 3 e Task 4 com `File` espião).
3. **Dado real ou fixture não determinística**: gerador só com literais sintéticos; geração dupla (cultura invariante × pt-BR) produz bytes iguais; `byCategory` do resumo (ordem de `GroupBy` sem `ORDER BY`) é ordenado pelo gerador; LF garantido (Task 2 `Generation_is_deterministic_across_cultures`, `fixtures.test.ts` na Task 3).
4. **Relógio/mês do visitante vazando**: com o relógio do sistema em 2027-03-01 ou 2026-01-01 o demo abre em outubro de 2026, "próximo" desabilitado, formulários com data padrão 2026-10-15; o modo real continua usando o dia local (Task 1, Task 4).
5. **Esconder valores no demo** e **dinheiro como number**: com "Esconder valores" ligado nenhuma página do demo mostra padrão de dinheiro; todo campo de dinheiro das fixtures é string no formato do contrato (Task 3 `fixtures.test.ts`, Task 4).
6. **Regressão do modo real e do isolamento**: padrões de `AppConfig` = comportamento atual (sair visível, sem faixa, dia local); `dist-real/` sem marcador do demo/fixtures; exclusão do legado não quebra import de `src/real` (Task 1, 5, 6 `real_sources_import_only_within_src`).
7. **Mutação no demo sem mensagem**: toda mutação (criar/editar/excluir lançamento, conta, ponto de saldo, confirmar/desfazer importação) mostra a mensagem traduzida, nos dois idiomas, e nada muda nas listas (Task 1 mapeamento, Task 4 e2e).

---

### Task 1: Pontos de extensão do `src/real` (configuração, relógio, faixa, sair, `demo_read_only`)

**Files:**
- Create: `src/real/appConfig.ts`, `src/real/appConfig.test.ts`, `src/real/components/AppShell.test.ts`, `src/real/stores/errorCode.test.ts`
- Modify: `src/real/money/today.ts`, `src/real/stores/summary.ts`, `src/real/components/CheckpointForm.vue`, `src/real/components/TransactionForm.vue`, `src/real/components/AppShell.vue`, `src/real/api/errors.ts`, `src/real/stores/errorCode.ts`, `src/real/stores/imports.ts`, `src/real/i18n/pt-BR.json`, `src/real/i18n/en-US.json`
- Test: `src/real/stores/summary.test.ts`, `src/real/stores/imports.test.ts`, `src/real/stores/accounts.test.ts` (acrescentar)

**Interfaces:**
- Produces:
  - `src/real/appConfig.ts`: `interface AppConfig { readOnly: boolean; hideLogout: boolean; fixedToday: string | null }`; `const DEFAULT_APP_CONFIG: Readonly<AppConfig>` (`false`, `false`, `null`); `const APP_CONFIG_KEY: InjectionKey<AppConfig>`; `setAppConfig(c: Partial<AppConfig> | null): void` (mescla com os padrões; `null` restaura; `fixedToday` fora de `^\d{4}-\d{2}-\d{2}$` lança `Error`); `getAppConfig(): AppConfig`; `installAppConfig(app: App, c: Partial<AppConfig>): void` (chama `setAppConfig` e `app.provide`); `useAppConfig(): AppConfig` (`inject(APP_CONFIG_KEY, null) ?? getAppConfig()`).
  - `src/real/money/today.ts`: `todayISO(): string` = `getAppConfig().fixedToday ?? todayLocalISO()` (`todayLocalISO(d?)` inalterada).
  - `src/real/stores/summary.ts`: `currentMonth(): string` = `todayISO().slice(0, 7)`; a store usa `currentMonth()` no mês inicial, em `canGoNext` e no limite de `setMonth` (`currentMonthLocal(d?)` continua exportada).
  - `CheckpointForm.vue`/`TransactionForm.vue`: data padrão `todayISO()`.
  - `AppShell.vue`: `const config = useAppConfig()`; faixa `<p role="status" data-testid="demo-banner">{{ t("real.demo.banner") }}</p>` no topo do `<main>` só com `config.readOnly`; botão `data-testid="logout"` só com `!config.hideLogout`.
  - `src/real/api/errors.ts`: `export const DEMO_READ_ONLY_CODE = "demo_read_only"`.
  - `errorCode.ts`: `"demo_read_only"` em `KNOWN_ERROR_CODES`; `errorCodeOf` devolve `"demo_read_only"` quando `e instanceof ApiError && e.code === DEMO_READ_ONLY_CODE` (antes do mapeamento por status).
  - `imports.ts`: `importErrorCode` devolve `"demo_read_only"` para esse código em qualquer contexto (antes de `SERVER_CODES`); `IMPORT_ERROR_CODES` inclui `"demo_read_only"`.
  - i18n: `real.demo.banner`, `real.errors.demo_read_only` (textos em Global Constraints).

- [ ] **Step 1: Write failing tests** (todo teste novo restaura com `setAppConfig(null)` no `afterEach`)
  - `appConfig.test.ts`: `defaults_match_current_real_behavior` (`getAppConfig()` deep-equal `{ readOnly: false, hideLogout: false, fixedToday: null }`); `partial_config_merges_with_defaults` (`setAppConfig({ readOnly: true })` → `hideLogout === false`); `invalid_fixed_today_throws` (`"2026-10"`, `"15/10/2026"`); `null_restores_defaults`; `useAppConfig_prefers_injected_value` (componente montado com `provide` de outro objeto).
  - `accounts.test.ts` (bloco `todayLocalISO`): `todayISO_uses_fixed_date_when_configured` (`vi.setSystemTime(new Date(2027, 2, 1, 10))`, `fixedToday: "2026-10-15"` → `"2026-10-15"`); `todayISO_without_config_is_local_date` (= `todayLocalISO()`).
  - `summary.test.ts`: `fixed_clock_sets_initial_month_and_blocks_next` (sistema em 2027-03-01, `fixedToday: "2026-10-15"`, store nova → `month === "2026-10"`, `canGoNext === false`, `setMonth("2026-11")` não chama a API); `fixed_clock_in_the_future_of_system_time_still_rules` (sistema em 2026-01-01 → `month === "2026-10"`, `setMonth("2026-09")` aceito).
  - `AppShell.test.ts` (shell montado com `fakeApi` de `testing.ts`, sessão autenticada, `fakeAccountsApi`, `transactions.list` falso): `real_defaults_show_logout_and_no_banner`; `read_only_config_shows_banner_and_hides_logout` (`installAppConfig`/provide com `{ readOnly: true, hideLogout: true }` → `[data-testid=demo-banner]` com o texto pt-BR exato e `[data-testid=logout]` ausente).
  - `errorCode.test.ts`: `demo_read_only_code_is_kept` (`errorCodeOf(new ApiError(403, "demo_read_only")) === "demo_read_only"`); `other_403_stays_server`.
  - `imports.test.ts`: `demo_read_only_maps_in_every_import_context` (`importErrorCode(new ApiError(403, "demo_read_only"), ctx)` para `"upload"`, `"confirm"`, `"undo"`); o teste existente `Each_known_error_code_has_a_translation_in_both_languages` passa a cobrir a chave nova.
- [ ] **Step 2:** `npx vitest run src/real/appConfig.test.ts src/real/components/AppShell.test.ts src/real/stores` → FAIL (módulo `appConfig` inexistente, chaves ausentes).
- [ ] **Step 3:** Implementar pelas assinaturas acima; nenhuma tela além do `AppShell` lê `readOnly`/`hideLogout`.
- [ ] **Step 4:** `npm test` → PASS (suíte inteira, inclusive `e2e-smoke.test.ts` e `i18n.test.ts`); `npm run build:real` → PASS.
- [ ] **Step 5: Commit** `feat(real): configuração do app (somente leitura, ocultar sair, relógio) e erro demo_read_only`.

### Task 2: Gerador de fixtures no backend e fixtures commitadas

**Files:**
- Create: `backend/tests/FinanceDashboard.Tests/DemoFixtures/DemoSeed.cs`, `backend/tests/FinanceDashboard.Tests/DemoFixtures/DemoFixtureGenerator.cs`, `backend/tests/FinanceDashboard.Tests/DemoFixtures/DemoJson.cs`, `backend/tests/FinanceDashboard.Tests/DemoFixtures/DemoFixturesTests.cs`, `src/demo-static/fixtures/{meta,accounts,checkpoints,categories,transactions,summaries,summary-empty,imports,import-details}.json` (gerados)
- Modify: `.gitattributes`

**Interfaces:**
- Consumes: `ApiFactory` (`TimeProvider` init), `PersistenceTestSupport.Create(cs, userId)`, `AuthTestHelpers.LoginAsync`, `AuthedClient`-like GET, `StoredPreview` (público) + `StoredPreview.SerializeWarnings` (internal, `InternalsVisibleTo` já existe), `ImportWarning.PossibleDuplicate(int)`, `DefaultCategories.EnsureForUserAsync`.
- Produces:
  - `static class DemoSeed`: `const string UserId = "demo-visitor"`, `const string Email = "visitante@exemplo.invalid"`; `static readonly DateOnly ReferenceDate = new(2026, 10, 15)`; `static readonly string[] Months` = `2026-05`…`2026-10`; `static Guid Id(int kind, int n)` = `new Guid($"{kind:x8}-0000-4000-8000-{n:x12}")` (kinds: conta 0xA, categoria 0xC, lançamento 0x7, ponto de saldo 0xB, lote 0x1B); `static Guid PreviewBatchId`, `static Guid ConfirmedBatchId`; `static Task SeedAsync(ApiFactory f, CancellationToken ct = default)` — cria o `AppUser { Id = UserId, UserName = Email, Email = Email }` via `UserManager` (senha sintética constante) e grava por `DbContext`:
    - 3 contas: "Conta Corrente Exemplo" (`Checking`, "Banco Exemplo"), "Cartão Exemplo" (`CreditCard`, "Banco Exemplo"), "Carteira" (`Wallet`, "").
    - as 13 categorias padrão com ids fixos (mesmos nomes/tipos de `DefaultCategories`, `IsDefault = true`), seguidas de `EnsureForUserAsync` que **não** pode adicionar nada.
    - ≥ 60 lançamentos visíveis em 2026-05..2026-10 (todo mês com receita e despesa, mais de uma categoria de despesa, ≥ 1 sem categoria, ≥ 1 `EditedByUser`), compra parcelada 3x no cartão (1/3 em 08, 2/3 em 09, 3/3 em 10 com `InstallmentNumber/Total`, `PurchaseDate`, `CardSuffix "0000"`, `CardKind.Physical`), pendentes `PendingClassification` (2 em 2026-10, 1 em 2026-09), lançamentos de outubro da conta corrente com `Origin.Imported` + `SourceKey` sintético; nenhum com `Date > ReferenceDate`; nenhum `IsDeleted`.
    - pontos de saldo `Manual` na corrente (2026-04-30) e na carteira (2026-04-30), `CreatedAt` fixo.
    - lote **confirmado** `ItauCard` no cartão (`CreatedAt 2026-10-05T13:00Z`, `DueDate 2026-10-10`, `DeclaredTotal` = soma `decimal` das suas linhas, `PreviewJson null`, lançamentos com `BatchId`).
    - lote **em prévia** `ItauChecking` na corrente (`PeriodStart 2026-10-01`, `PeriodEnd 2026-10-14`, `CreatedAt 2026-10-15T11:00Z`, `ExpiresAt 2026-10-16T11:00Z`, `PreviewJson = new StoredPreview(null, new ImportCheckpointView(2026-10-14, …), linhas).Serialize()` com 3 linhas existentes (mesmas `SourceKey` dos importados de outubro), 2 novas `Normal`, 1 nova `PendingClassification`; `WarningsJson = StoredPreview.SerializeWarnings([ImportWarning.PossibleDuplicate(1)])`; `NewCount 3`, `ExistingCount 3`); `FileHash` de cada lote = SHA-256 hex minúsculo de um rótulo sintético fixo.
  - `static class DemoJson`: `string Normalize(string json)` (reserializa indentado, encoder relaxado, `\r\n`→`\n`, `\n` final — mesmo formato do `OpenApiSnapshotTests.Normalize`); `JsonNode SortByCategory(JsonNode summary)` (ordena `byCategory` por `categoryId` ordinal, `null` por último).
  - `sealed class DemoFixtureGenerator`: `static Task<IReadOnlyDictionary<string, string>> GenerateAsync(PostgresFixture pg)` — sobe `ApiFactory(cs) { TimeProvider = new FakeTimeProvider(2026-10-15T12:00:00Z) }`, `SeedAsync`, loga como o visitante e devolve nome de arquivo → conteúdo normalizado:
    - `meta.json` = `{ "generator": "finance-demo-fixtures", "referenceDate": "2026-10-15", "months": [...], "previewBatchId": "...", "user": <GET /api/auth/me> }`
    - `accounts.json` = GET `/api/accounts`; `checkpoints.json` = objeto `accountId → GET /api/accounts/{id}/checkpoints` (chaves em ordem ordinal); `categories.json` = GET `/api/categories`
    - `transactions.json` = todas as páginas de GET `/api/transactions?page=n&size=200` concatenadas (ordem do servidor: data desc, id desc)
    - `summaries.json` = objeto `mês → SortByCategory(GET /api/summary?month=mês)` para os 6 meses; `summary-empty.json` = GET `/api/summary?month=2026-04` (o gerador afirma `income`/`expenses`/`net` = `"0.00"`, `byCategory` vazio, `pending.count` 0)
    - `imports.json` = GET `/api/imports`; `import-details.json` = objeto `batchId → GET /api/imports/{id}` para os dois lotes.
  - `static string FixturesDir()` = sobe de `AppContext.BaseDirectory` até `FinanceDashboard.sln` e resolve `../src/demo-static/fixtures`.

- [ ] **Step 1: Write failing tests** (`[Collection(PostgresCollection.Name)]`)
  - `DemoJson_normalizes_line_endings_and_trailing_newline` (`"{\r\n\"a\":1}"` → `"{\n  \"a\": 1\n}\n"`); `SortByCategory_orders_by_id_with_null_last`.
  - `Seed_covers_every_default_category` (após `SeedAsync`, contagem de categorias = 13 e `EnsureForUserAsync` não altera).
  - `Seed_has_no_line_after_reference_date_and_covers_every_month` (consulta direta: max `Date` ≤ 2026-10-15; cada mês de `Months` com receita e despesa; 3 pendentes).
  - `Generation_is_deterministic_across_cultures`: gera duas vezes em bancos distintos, a segunda com `CultureInfo.CurrentCulture = CurrentUICulture = new("pt-BR")` → dicionários iguais byte a byte.
  - `Generated_preview_matches_seed` (`import-details[previewBatchId]` tem `state "Preview"`, 6 linhas, 3 `existing`, aviso `PossibleDuplicate`; `imports.json` tem um `Confirmed` e um `Preview`).
  - `Committed_fixtures_match_generator`: com `UPDATE_DEMO_FIXTURES=1` apaga os `*.json` da pasta e grava os gerados; senão afirma o mesmo conjunto de nomes de arquivo e `Assert.Equal(gerado, commitado.Replace("\r\n", "\n"))` por arquivo, com mensagem `"Fixtures do demo divergem do gerador: rode UPDATE_DEMO_FIXTURES=1 (<arquivo>)"`.
- [ ] **Step 2:** `cd backend && dotnet test FinanceDashboard.sln --filter "FullyQualifiedName~DemoFixtures"` → FAIL (tipos inexistentes; depois, fixtures ausentes).
- [ ] **Step 3:** Implementar `DemoSeed`, `DemoJson`, `DemoFixtureGenerator` pelas assinaturas acima (só código de teste; nenhum código de produção muda). Se alguma resposta trouxer id não semeado, parar e registrar (o teste de determinismo acusa).
- [ ] **Step 4:** `UPDATE_DEMO_FIXTURES=1 dotnet test FinanceDashboard.sln --filter "FullyQualifiedName~Committed_fixtures_match_generator"`; acrescentar `src/demo-static/fixtures/*.json text eol=lf` ao `.gitattributes`; `dotnet test FinanceDashboard.sln` → PASS (suíte inteira, incluindo o modo comparação).
- [ ] **Step 5:** Revisar à mão as fixtures geradas: só nomes sintéticos, e-mail `visitante@exemplo.invalid`, nenhum valor `number` em campo de dinheiro.
- [ ] **Step 6: Commit** `feat(demo): gerador de fixtures do demo no backend e fixtures geradas` (o hook roda gitleaks nos arquivos staged; se acusar falso positivo num valor sintético, mudar a semente, nunca afrouxar o gitleaks).

### Task 3: Adaptador estático `createStaticApi`

**Files:**
- Create: `src/demo-static/fixtures.ts`, `src/demo-static/staticApi.ts`, `src/demo-static/staticApi.test.ts`, `src/demo-static/fixtures.test.ts`

**Interfaces:**
- Consumes: Task 1 (`DEMO_READ_ONLY_CODE`), Task 2 (arquivos JSON e seus formatos), `ApiClient` e tipos de `src/real/api/client.ts`/`types.ts`, `ApiError`.
- Produces:
  - `src/demo-static/fixtures.ts`: `interface DemoFixtures { meta: { generator: string; referenceDate: string; months: string[]; previewBatchId: string; user: CurrentUser }; accounts: Account[]; checkpoints: Record<string, Checkpoint[]>; categories: Category[]; transactions: Transaction[]; summaries: Record<string, Summary>; emptySummary: Summary; imports: ImportBatch[]; importDetails: Record<string, ImportPreview> }`; `const FIXTURES_MARKER = "finance-demo-fixtures"`; `const DEMO_FIXTURES: DemoFixtures` (imports JSON estáticos; único cast `as unknown as DemoFixtures`, coberto por `fixtures.test.ts`).
  - `src/demo-static/staticApi.ts`: `createStaticApi(f: DemoFixtures): ApiClient`. Toda leitura devolve `structuredClone` (a UI não altera as fixtures). Regras:
    - `auth.me()` → `f.meta.user`; `login`/`logout` → rejeita `new ApiError(403, DEMO_READ_ONLY_CODE)`.
    - `accounts.list()`/`categories.list()` → fixtures; `accounts.checkpoints(id)` → `f.checkpoints[id]` ou `ApiError(404)`.
    - `transactions.list(q)`: espelha `ListAsync` do servidor — `page ?? 1`, `size = min(size ?? 50, 200)`; `page < 1` ou `size < 1` ou `month` fora de `^\d{4}-(0[1-9]|1[0-2])$` → `ApiError(400)`; filtros por igualdade em `accountId`, `categoryId`, `state` e prefixo `date.startsWith(month)`; **preserva a ordem gerada** (não reordena); devolve `{ items: fatia, page, size, total }`.
    - `summary.month(m, accountId?)`: `accountId !== undefined` → rejeita `new Error("demo: resumo por conta não está nas fixtures")` (falha alta, não `ApiError`); mês inválido → `ApiError(400)`; `f.summaries[m]` se existir; senão `{ ...clone(f.emptySummary), month: m }` (só troca o rótulo do mês).
    - `imports.preview(_accountId, _file)` → `f.importDetails[f.meta.previewBatchId]` sem tocar no `File`; `imports.list()` → `f.imports`; `imports.get(id)` → `f.importDetails[id]` ou `ApiError(404)`.
    - `accounts.create/update/addCheckpoint`, `transactions.create/update/remove`, `imports.confirm/undo` → rejeitam `new ApiError(403, DEMO_READ_ONLY_CODE)`.

- [ ] **Step 1: Write failing tests**
  - `fixtures.test.ts`: `meta_is_the_reference_set` (`generator === FIXTURES_MARKER`, `referenceDate === "2026-10-15"`, `months` = 2026-05..2026-10, todas com chave em `summaries`); `every_money_field_is_a_contract_string` (percorre todo o objeto: campos `amount|balance|total|income|expenses|net|declaredTotal` são `string` casando `^-?[0-9]{1,16}(\.[0-9]{1,2})?$`); `no_transaction_after_reference_date`; `only_synthetic_identity` (`meta.user.email === "visitante@exemplo.invalid"`; nos campos de texto livre — `name`, `institution`, `description`, `originalDescription`, `counterpartyName`, `categoryName` — nenhum valor casa `\d{3}\.\d{3}\.\d{3}-\d{2}`, `\d{11,}` ou um e-mail; ids e hashes ficam fora dessa checagem, pois os ids fixos têm 12 dígitos seguidos); `preview_batch_exists_in_preview_state`; `json_has_lf_only` (lê os arquivos com `fs`, sem `\r`).
  - `staticApi.test.ts` (com `DEMO_FIXTURES`):
    - `me_returns_fixed_visitor`; `lists_return_copies` (mutar o retorno não muda a chamada seguinte).
    - `transactions_filter_by_account_month_category_state_preserving_order` (resultado = `f.transactions.filter(...)` na mesma ordem; `total` coerente).
    - `transactions_paginate_and_out_of_range_page_is_empty` (`size 10, page 2` = itens 10..19; página além do fim → `items []`, `total` inalterado); `transactions_size_capped_at_200`; `transactions_invalid_page_size_or_month_is_400` (`ApiError.status === 400`).
    - `summary_returns_generated_month` (deep-equal `f.summaries["2026-10"]`); `summary_outside_fixtures_returns_generated_empty_with_requested_month` (`"2019-01"` → `{ ...f.emptySummary, month: "2019-01" }`); `summary_with_account_fails_loudly` (rejeita, `not.toBeInstanceOf(ApiError)`); `summary_invalid_month_is_400`.
    - `checkpoints_known_account_and_404_for_unknown`.
    - `import_preview_ignores_account_and_file` (`File` cujo `arrayBuffer`/`text`/`stream`/`slice` são `vi.fn` que lançam, e `vi.spyOn(globalThis, "FileReader")` → nenhum chamado; resultado = `importDetails[previewBatchId]`); `import_list_get_and_404`.
    - `every_mutation_rejects_with_demo_read_only` (`it.each` sobre as 10 mutações: `ApiError`, `status 403`, `code "demo_read_only"`).
    - `no_network_used` (`vi.stubGlobal("fetch", vi.fn())` e `XMLHttpRequest` espião; chama todos os métodos de leitura → nenhum chamado).
- [ ] **Step 2:** `npx vitest run src/demo-static` → FAIL (módulos inexistentes).
- [ ] **Step 3:** Implementar `fixtures.ts` e `staticApi.ts` pelas regras acima (sem aritmética de dinheiro; nenhum import de `src/real/api/index.ts`/`http.ts`).
- [ ] **Step 4:** `npx vitest run src/demo-static` → PASS; `npm test` → PASS.
- [ ] **Step 5: Commit** `feat(demo): adaptador estático do ApiClient sobre as fixtures geradas`.

### Task 4: Boot do demo, `main.ts` e e2e do demo

**Files:**
- Create: `src/demo-static/boot.ts`, `src/demo-static/e2e-demo.test.ts`
- Modify: `src/main.ts`

**Interfaces:**
- Consumes: Task 1 (`installAppConfig`, `AppConfig`), Task 3 (`createStaticApi`, `DEMO_FIXTURES`), `RealApp.vue`, `createRealRouter(history?)`, `i18n` de `src/real/i18n`, `installApi` de `src/real/api/context.ts`, `useSessionStore`.
- Produces:
  - `src/demo-static/boot.ts`: `const DEMO_BUNDLE_MARKER = "data-finance-demo-mode"`; `const DEMO_CONFIG: AppConfig = { readOnly: true, hideLogout: true, fixedToday: DEMO_FIXTURES.meta.referenceDate }`; `createDemoApp(history?: RouterHistory): Promise<{ app: App; router: Router; pinia: Pinia }>` (Pinia, i18n, `installApi(app, createStaticApi(DEMO_FIXTURES))`, `installAppConfig(app, DEMO_CONFIG)`, `session.bootstrap()` antes do router, `router.isReady()`, `document.documentElement.lang`, atributo `DEMO_BUNDLE_MARKER="1"` no `<html>`); `bootDemo(): Promise<void>` = `createDemoApp()` + `app.mount("#app")`.
  - `src/main.ts`: ramo não-real passa a `import("./demo-static/boot").then((m) => m.bootDemo())` (comparação literal com `import.meta.env.VITE_APP_MODE` mantida).

- [ ] **Step 1: Write failing tests** em `e2e-demo.test.ts` (mesmo mock de `vue-chartjs` do `e2e-smoke.test.ts`; `vi.useFakeTimers({ toFake: ["Date"] })`; `fetch` e `XMLHttpRequest` espiões em todo teste com `afterEach` afirmando zero chamadas; monta `createDemoApp(createMemoryHistory())` num `<div>` do `document`; `afterEach` com `setApi(null)`, `setRouter(null)`, `setAppConfig(null)`, `localStorage.clear()`):
  - `boots_without_login_on_reference_month` (sistema em 2027-03-01: rota `/`, rótulo do mês "outubro de 2026", botão de próximo mês desabilitado, `[data-testid=demo-banner]` visível, `[data-testid=logout]` ausente, `<html data-finance-demo-mode="1">`).
  - `system_clock_in_the_past_still_opens_reference_month` (sistema em 2026-01-01 → "outubro de 2026").
  - `login_route_redirects_to_overview`.
  - `visits_every_page_without_network` (`/`, `/transactions`, `/pending`, `/accounts`, `/import`: cada uma mostra um nome de conta/lançamento vindo de `DEMO_FIXTURES`; badge de pendentes = número de pendentes das fixtures).
  - `month_without_movement_shows_empty_state_not_error` (voltar até 2026-04 → `[data-test=empty-state]`, nenhum `role=alert`).
  - `import_shows_example_preview_without_reading_file` (escolhe a conta corrente, `File` espião `extrato.ofx` com leitura que lança; prévia com as 6 linhas das fixtures; confirmar → texto pt-BR de `real.errors.demo_read_only`; espiões de leitura não chamados).
  - `mutation_shows_translated_read_only_message` (editar a categoria de um pendente e criar lançamento em `/transactions`: mensagem pt-BR; trocar para EN e repetir: mensagem en-US; listas iguais às fixtures depois).
  - `hide_values_masks_every_page` (liga `[data-testid=toggle-values]`; em cada página nenhum texto/atributo/título casa `/\d[.,]\d{2}(?!\d)/`).
- [ ] **Step 2:** `npx vitest run src/demo-static/e2e-demo.test.ts` → FAIL (`boot.ts` inexistente).
- [ ] **Step 3:** Implementar `boot.ts` e trocar o ramo de `main.ts`.
- [ ] **Step 4:** `npm test` → PASS; `npm run build` e `npm run build:real` → PASS.
- [ ] **Step 5: Commit** `feat(demo): demo estático inicia a interface real somente leitura`.

### Task 5: `check-bundles.mjs` novo e CI

**Files:**
- Modify: `scripts/check-bundles.mjs`, `.github/workflows/ci.yml`
- Create: `scripts/check-bundles.test.mjs`

**Interfaces:**
- Produces: `scripts/check-bundles.mjs` exporta `const RULES` = `[{ dir: "dist", required: ["data-finance-demo-mode", "finance-demo-fixtures"], forbidden: ["data-finance-real-mode", "/api/auth/csrf"] }, { dir: "dist-real", required: ["data-finance-real-mode"], forbidden: ["data-finance-demo-mode", "finance-demo-fixtures"] }]` e `checkBundles(root: string, rules = RULES): { ok: boolean; messages: string[] }`; executa e sai com código só quando chamado direto (`import.meta.url === pathToFileURL(process.argv[1]).href`). Mensagens citam pasta/arquivo e marcador, nunca conteúdo. O antigo `finance_dashboard_transactions` e a nota sobre o axios saem.

- [ ] **Step 1: Write failing tests** em `check-bundles.test.mjs` (pastas temporárias com `fs.mkdtempSync`): `passes_when_markers_are_where_they_belong`; `fails_when_demo_bundle_has_http_client` (arquivo em `dist/` com `/api/auth/csrf`); `fails_when_real_bundle_has_fixtures_or_demo_marker`; `fails_when_required_marker_missing`; `fails_when_dir_missing`; `messages_never_contain_file_content` (conteúdo com uma frase sentinela que não aparece em `messages`).
- [ ] **Step 2:** `npx vitest run scripts/check-bundles.test.mjs` → FAIL.
- [ ] **Step 3:** Reescrever `check-bundles.mjs`. Na CI: o job `backend` já roda a suíte inteira (inclui `Committed_fixtures_match_generator`); renomear o passo para `Test (inclui deriva do OpenAPI e das fixtures do demo)` e comentar que `UPDATE_DEMO_FIXTURES` nunca é definido na CI; job `frontend` inalterado nos comandos.
- [ ] **Step 4:** `npm test` → PASS; `npm run build && npm run build:real && node scripts/check-bundles.mjs` → `ok: bundles dist/ e dist-real/ isolados por modo`.
- [ ] **Step 5: Commit** `ci: checagem de bundles do demo estático (sem cliente HTTP, sem fixtures no real)`.

### Task 6: Tag `demo-legado`, exclusão do legado, i18n e dependências

**Files:**
- Delete: `src/App.vue`, `src/router/`, `src/pages/`, `src/components/`, `src/store/`, `src/services/`, `src/utils/`, `src/i18n/`, `src/demo/`
- Modify: `src/real/boot.ts`, `src/real/i18n/index.ts`, `src/assets/main.css` (mantido: é o CSS global/Tailwind importado por `src/main.ts` e usado pela interface real; sai só o bloco `.Vue-Toastification*`), `package.json`, `package-lock.json`, `index.html` (meta `description` sem metas/cripto)
- Create: `src/sources.test.ts`

**Interfaces:**
- Consumes: Tasks 1–5 verdes.
- Produces: `src/real/i18n/index.ts` com `messages: { "en-US": realEn, "pt-BR": realPt }`; `src/real/boot.ts` sem `mergeLocaleMessage` do legado. Conferido no código: `src/real` não usa nenhuma chave fora de `real.*` (nada a migrar).

- [ ] **Step 1:** Tag antes de apagar: `git fetch origin && git tag -a demo-legado origin/main -m "App demo legado (antes da S1c)"`; conferir `git ls-tree demo-legado -- src/App.vue src/store` (não vazio); `git push origin demo-legado`. Registrar no `away-log`.
- [ ] **Step 2: Write failing test** `src/sources.test.ts`: `legacy_tree_is_gone` (`fs.existsSync` falso para cada caminho apagado); `real_sources_import_only_within_src_real` (todo `import`/`import()` relativo em `src/real/**/*.{ts,vue}` resolve dentro de `src/real`); `demo_static_never_imports_http_client` (nenhum arquivo de `src/demo-static` importa `real/boot`, `real/api"`/`real/api/index` ou `real/api/http`); `no_legacy_dependency_is_imported` (nenhum `src/**` importa `axios`, `lodash`, `vue-toastification`).
- [ ] **Step 3:** `npx vitest run src/sources.test.ts` → FAIL (`legacy_tree_is_gone`).
- [ ] **Step 4:** `git rm -r` dos caminhos acima; tirar a mescla do i18n legado de `real/boot.ts` e `real/i18n/index.ts`; limpar o bloco de toast do `main.css`; atualizar `index.html`; `npm uninstall axios lodash @types/lodash vue-toastification` (conferir antes com busca que nada mais as importa; se outra dependência ficar sem uso, remover e anotar).
- [ ] **Step 5:** `npm test && npm run build && npm run build:real && node scripts/check-bundles.mjs` → PASS / `ok: ...`.
- [ ] **Step 6: Commit** `refactor: apaga o app demo legado (preservado na tag demo-legado)`.

### Task 7: README, ADR 0006, diário e verificação final

**Files:**
- Modify: `README.md`, `docs/away-log.md`
- Create: `docs/adr/0006-demo-estatico-por-adaptador-e-fixtures-geradas.md`
- Delete (se mostrarem o app antigo): `docs/screenshots/portfolio.png`, `docs/screenshots/visao-geral.png`

**Interfaces:**
- Consumes: tudo acima.

- [ ] **Step 1:** README: introdução e "Funcionalidades" passam a descrever o demo novo (mesma interface do uso real, dados fictícios gerados pelo backend, somente leitura, sem login); sai "Portfólio de Criptomoedas"/metas/CoinGecko (com nota de que voltam na S4/v2.1 e de que o app antigo está na tag `demo-legado`); tabela de builds com a linha `demo` atualizada; parágrafo "Checagens na CI" com as regras novas do `check-bundles` e o teste de deriva; subseção "Fixtures do demo" com `UPDATE_DEMO_FIXTURES=1 dotnet test backend/FinanceDashboard.sln --filter "FullyQualifiedName~Committed_fixtures_match_generator"`; "Fora desta fatia" sem a S1c/S2. Referências a screenshots do app antigo removidas (abrir as imagens para conferir; screenshot novo fica como pendência para o João no `away-log`).
- [ ] **Step 2:** ADR 0006 (formato dos ADRs existentes): contexto, decisão (adaptador atrás de `ApiClient`, fixtures geradas por teste xUnit com ids fixos e relógio falso, deriva na CI, somente leitura com `demo_read_only`, `AppConfig` como único conhecimento do demo em `src/real`, legado apagado com tag), alternativas B/C/D do spec, consequências (regerar fixtures ao mudar contrato/semente; resumo por conta não suportado no demo).
- [ ] **Step 3:** Verificação final: `cd backend && dotnet test FinanceDashboard.sln` → PASS; na raiz `npm test`, `npm run build`, `npm run build:real`, `node scripts/check-bundles.mjs` → PASS/`ok`; `git diff --exit-code src/demo-static/fixtures` após rodar o backend (sem alteração). Opcional: `npx vite preview` do `dist/` e navegar com a rede do DevTools aberta (nenhuma requisição a `/api`); se não houver navegador, o e2e da Task 4 é a evidência — anotar no `away-log`.
- [ ] **Step 4:** `docs/away-log.md`: linhas `S1c/T*` das decisões tomadas na execução.
- [ ] **Step 5: Commit** `docs: README, ADR 0006 e diário da S1c`.
