# Finance v2 — S1b: interface do modo real Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Interface do uso real (login, Visão geral, Contas, Lançamentos/Pendentes, Importar com prévia em abas, esconder valores) servida pelo Compose na mesma origem da API, sem alterar o demo da Vercel.

**Architecture:** Duas pequenas mudanças no backend (OpenAPI fiel ao contrato + `overrides` no confirm) e um app novo em `src/real/` atrás de uma interface `ApiClient` (implementação HTTP agora; demo estático na S1c). O modo é decidido no build (`VITE_APP_MODE`, padrão `demo`); o código de um modo não entra no bundle do outro.

**Tech Stack:** Vue 3 + TypeScript + Pinia + Vue Router + Tailwind + Chart.js + vue-i18n + Vitest (+ `@vue/test-utils`, `openapi-typescript`); backend .NET 10 (já existente).

**Spec:** `docs/superpowers/specs/2026-11-02-finance-v2-s1b-interface-design.md` (autoridade; inclui "Decisões complementares"). Pais: `...-backend-design.md`, `...-s1a-nucleo-nubank-design.md`.

## Global Constraints
- Contrato da API (S1a): dinheiro **string decimal** (`"1234.56"`, ≤2 casas), datas `YYYY-MM-DD`, enums string, erros de importação `{error: code}`, `401` sem sessão, `404` para recurso alheio, mutações exigem `X-XSRF-TOKEN` (token amarrado à identidade: buscar `/api/auth/csrf` de novo após login e antes de mutações).
- **Nenhuma aritmética de dinheiro no navegador**; dinheiro em estado/props é `string`. Converter para `number` só no limite do Chart.js (desenho). Sem token/sessão em `localStorage`/`sessionStorage`; só preferências (esconder valores, idioma).
- **Esconder valores** cobre texto, tooltips e rótulos de eixo dos gráficos, `aria-label`, `title`/`document.title` e mensagens de erro.
- Modo por `import.meta.env.VITE_APP_MODE` (`demo` padrão; `real` via `npm run build:real`/`dev:real`, que usam `vite --mode real` + `.env.real`). Bundle `demo` não contém `X-XSRF-TOKEN`; bundle `real` não contém `finance_dashboard_transactions`.
- Código novo só em `src/real/` (e `scripts/`, config, CI, `backend/`). **Não alterar** páginas, stores e componentes legados.
- pt-BR é a referência; en-US com as mesmas chaves (teste garante). Textos novos só em `src/real/i18n/*.json`.
- Tipos da API **gerados** de `backend/openapi/v1.json` para `src/real/api/schema.d.ts`; nunca escritos à mão. Dependências novas com versão exata fixada.
- Acessibilidade: foco visível, rótulos em formulários, abas por teclado, contraste. Nada de dado real em fixtures/testes/commits.
- Comandos: backend a partir de `backend/` (`dotnet test FinanceDashboard.sln`, Docker rodando); front a partir da raiz (`npm test`, `npm run build`, `npm run build:real`).

## Review Focus
1. Valor vazando com "esconder" ligado: tooltip do gráfico, eixo, `aria-label`, `title`, erro (Task 5, 10).
2. Token antiforgery velho: mutação logo após login/logout e após `401` (Task 4, 6).
3. `401` no meio da sessão volta ao login preservando a rota de destino; login com senha errada não vaza se o e-mail existe (Task 6).
4. Strings de dinheiro extremas: `"0.10"`+`"0.20"` nunca somados; `"-0.00"`, `"9999999999999999.99"`, entrada com vírgula na digitação (Task 5, 7, 8).
5. `overrides` só afeta linhas novas; `sourceKey`/categoria inválidos → nada gravado (Task 2, 9).
6. Isolamento de modos no bundle e demo da Vercel intacto (Task 3).

---

### Task 1: Backend — OpenAPI fiel ao contrato + snapshot

**Files:**
- Modify: `backend/src/FinanceDashboard.Api/Program.cs` (transformers do `AddOpenApi`)
- Create: `backend/openapi/v1.json` (gerado), `backend/tests/FinanceDashboard.Tests/OpenApiSnapshotTests.cs`
- Modify: `README.md` (comando de atualização do snapshot)

**Interfaces:**
- Produces: `/api/openapi/v1.json` com todo `decimal`/`decimal?` como `{type: string, pattern: "^-?[0-9]{1,16}(\\.[0-9]{1,2})?$"}` e enums como `string` com `enum`; arquivo `backend/openapi/v1.json` idêntico ao documento gerado (normalizado: LF, indentado); `UPDATE_OPENAPI_SNAPSHOT=1 dotnet test --filter OpenApiSnapshot` reescreve o arquivo.

- [ ] **Step 1: Write failing tests** — `Decimals_are_documented_as_strings_with_money_pattern` (percorre o JSON e falha se algum `amount`/`balance`/`total`/`income`/`expenses`/`net` for `number`); `Enums_are_documented_as_strings`; `Committed_snapshot_matches_generated_document`.
- [ ] **Step 2:** `dotnet test FinanceDashboard.sln --filter "FullyQualifiedName~OpenApiSnapshot"` → FAIL (decimais são `number`; arquivo ausente).
- [ ] **Step 3:** Implementar os schema transformers e o teste de snapshot (com modo de atualização); gerar e commitar `backend/openapi/v1.json`.
- [ ] **Step 4:** Suíte completa → PASS.
- [ ] **Step 5: Commit** `feat(api): OpenAPI com dinheiro como string e snapshot versionado`.

### Task 2: Backend — `overrides` no confirm da importação

**Files:**
- Modify: `backend/src/FinanceDashboard.Api/Imports/ImportEndpoints.cs` (request), `backend/src/FinanceDashboard.Infrastructure/Importing/ImportService.cs` (`ConfirmAsync`), `backend/openapi/v1.json` (regenerar)
- Test: `backend/tests/FinanceDashboard.Tests/Importing/ImportOverridesTests.cs`

**Interfaces:**
- Consumes: S1a `ConfirmAsync`, `PreviewJson`, `NubankRules`.
- Produces: `POST /api/imports/{id}/confirm` body `{acknowledgeBlockingWarnings: bool, overrides?: [{sourceKey: string, state?: "Normal"|"PendingClassification", categoryId?: guid}]}`. Overrides se aplicam às linhas **novas** (não às existentes) antes do insert; `state` e `categoryId` independentes; `sourceKey` ausente da prévia ou duplicado, ou `categoryId` inexistente/de outro usuário ⇒ `422 {error: "invalid_override"}` sem gravar nada; resposta de sucesso inalterada `{batchId, created, skipped}`.

- [ ] **Step 1: Write failing tests** — `Override_sets_state_and_category_of_new_line`; `Override_for_existing_line_is_ignored_and_existing_line_untouched`; `Unknown_source_key_is_422_and_nothing_written`; `Duplicate_source_key_in_overrides_is_422`; `Other_users_category_is_422`; `State_and_category_are_independent`; `No_overrides_keeps_current_behavior` (testes S1a seguem verdes); `Overrides_are_not_logged` (marcador único não aparece nos logs capturados).
- [ ] **Step 2:** `dotnet test FinanceDashboard.sln --filter "FullyQualifiedName~ImportOverrides"` → FAIL.
- [ ] **Step 3:** Estender o DTO e aplicar os overrides sobre as linhas da prévia dentro da validação que antecede qualquer escrita; regenerar o snapshot (`UPDATE_OPENAPI_SNAPSHOT=1`).
- [ ] **Step 4:** Suíte completa → PASS (inclui testes de concorrência da S1a).
- [ ] **Step 5: Commit** `feat(import): classificar linhas novas na confirmação (overrides)`.

### Task 3: Fundação do front — modo, tipos gerados, build e CI

**Files:**
- Create: `.env.real` (`VITE_APP_MODE=real`), `src/real/boot.ts`, `src/real/RealApp.vue` (casca mínima), `src/real/router.ts` (rota placeholder), `src/real/api/schema.d.ts` (gerado), `scripts/check-bundles.mjs`, `vitest` config para jsdom/test-utils em `vite.config.ts`
- Modify: `package.json` (scripts `dev:real`, `build:real`, `api:types`; deps `openapi-typescript`, `@vue/test-utils` fixas), `src/main.ts` (escolhe o modo; legado intacto), `src/env.d.ts`, `deploy/web.Dockerfile` (`npm run build:real`), `.github/workflows/ci.yml` (job frontend)
- Test: `src/real/mode.test.ts`

**Interfaces:**
- Produces: `export const APP_MODE: "demo" | "real"` em `src/real/mode.ts`; `bootReal(): Promise<void>` (monta `RealApp` com Pinia, router e i18n mesclado); `npm run api:types` gera `src/real/api/schema.d.ts` a partir de `backend/openapi/v1.json`.
- `build:real` escreve em `dist-real/` (`vite build --mode real --outDir dist-real`; adicionar ao `.gitignore`/`.dockerignore`), e `deploy/web.Dockerfile` copia de `dist-real`; `build` continua em `dist/` (Vercel).
- `scripts/check-bundles.mjs`: após `build` e `build:real` (saídas em pastas distintas, ex. `dist/` e `dist-real/`), falha se `dist/` contiver `X-XSRF-TOKEN` ou se `dist-real/` contiver `finance_dashboard_transactions`.

- [ ] **Step 1: Write failing tests** — `mode.test.ts`: `defaults_to_demo_when_env_missing`, `real_when_env_is_real`.
- [ ] **Step 2:** `npm test -- src/real/mode.test.ts` → FAIL.
- [ ] **Step 3:** Implementar modo, `main.ts` com importação dinâmica condicional, scripts, geração de tipos e job de CI: `npm ci` → `npm test` → `npm run api:types && git diff --exit-code src/real/api/schema.d.ts` → `npm run build` → `npm run build:real` → `node scripts/check-bundles.mjs`. `web.Dockerfile` passa a usar `build:real`.
- [ ] **Step 4:** `npm test`, `npm run build`, `npm run build:real`, `node scripts/check-bundles.mjs` → tudo verde; abrir o build `demo` mostra o app legado inalterado (teste `src/demo/exemplo.test.ts` segue passando).
- [ ] **Step 5: Commit** `feat(web): fundação do modo real (modo de build, tipos gerados, CI)`.

### Task 4: Camada de API (HTTP + interface)

**Files:**
- Create: `src/real/api/errors.ts`, `src/real/api/types.ts`, `src/real/api/client.ts` (interface), `src/real/api/http.ts`, `src/real/api/index.ts`
- Test: `src/real/api/http.test.ts`

**Interfaces:**
- Produces:
  - `class ApiError extends Error { status: number; code?: string; field?: string }`.
  - `interface ApiClient { auth: { me(): Promise<{email:string}|null>; login(email:string, password:string): Promise<void>; logout(): Promise<void> }; accounts: { list(): Promise<Account[]>; create(b: NewAccount): Promise<Account>; update(id: string, b: AccountUpdate): Promise<Account>; checkpoints(id: string): Promise<Checkpoint[]>; addCheckpoint(id: string, b: {date: string; amount: string}): Promise<Checkpoint> }; categories: { list(): Promise<Category[]> }; transactions: { list(q: TransactionQuery): Promise<Page<Transaction>>; create(b): Promise<Transaction>; update(id: string, b): Promise<Transaction>; remove(id: string): Promise<void> }; summary: { month(month: string, accountId?: string): Promise<Summary> }; imports: { preview(accountId: string, file: File): Promise<ImportPreview>; list(): Promise<ImportBatch[]>; get(id: string): Promise<ImportBatch>; confirm(id: string, b: ConfirmBody): Promise<ConfirmResult>; undo(id: string): Promise<void> } }`; tipos em `types.ts` derivados de `schema.d.ts`.
  - `createHttpApi(opts?: { fetch?: typeof fetch; onUnauthorized?: () => void }): ApiClient`.
- Regras: `credentials: "same-origin"`; mutações buscam `/api/auth/csrf` quando não há token em memória e **descartam o token** após `login`, `logout` e qualquer `400` de antiforgery (nova tentativa única); `401` fora de `auth.me/login` chama `onUnauthorized`; corpo de erro `{error}` ⇒ `ApiError.code`; `me()` devolve `null` em `401`; upload usa `FormData` (`file`, `accountId`) sem fixar `Content-Type`.

- [ ] **Step 1: Write failing tests** (`fetch` simulado) — `Mutation_fetches_csrf_once_and_sends_header`; `Csrf_token_is_dropped_after_login_and_logout`; `Antiforgery_400_refetches_token_and_retries_once`; `Unauthorized_calls_handler_but_not_for_login_or_me`; `Error_body_code_maps_to_ApiError`; `Me_returns_null_on_401`; `Preview_sends_multipart_without_manual_content_type`; `Network_failure_becomes_ApiError_with_status_0`.
- [ ] **Step 2:** `npm test -- src/real/api` → FAIL.
- [ ] **Step 3:** Implementar.
- [ ] **Step 4:** `npm test` → PASS; `npm run build:real` verde.
- [ ] **Step 5: Commit** `feat(web): camada de API tipada para o modo real`.

### Task 5: Dinheiro — formatação, esconder valores e gráficos

**Files:**
- Create: `src/real/money/format.ts`, `src/real/money/visibility.ts`, `src/real/money/Money.vue`, `src/real/money/chart.ts`, `src/real/money/input.ts`
- Test: `src/real/money/*.test.ts`

**Interfaces:**
- Produces:
  - `formatMoney(value: string, locale: string): string` (BRL; recebe a string decimal, nunca soma; `"-0.00"` ⇒ `R$ 0,00` sem sinal);
  - `useValuesVisibility()` (Pinia): `hidden: Ref<boolean>`, `toggle()`; persiste em `localStorage` (`fd.hideValues`) com try/catch; `MASK = "••••"`;
  - `<Money :value="string" :signed? />` renderiza texto mascarado quando `hidden`, e `aria-label` também mascarado;
  - `chartMoney.tooltip/tick(value: number|string)` retornam máscara quando `hidden`; `toPlot(value: string): number` é o **único** ponto onde dinheiro vira `number`;
  - `parseMoneyInput(text: string): string | null` aceita `1234,56`, `1.234,56`, `1234.56`, devolve `"1234.56"` normalizado ou `null` (inválido, >2 casas, vazio, 17+ dígitos); `isMoneyString(text): boolean` com o mesmo padrão da API.

- [ ] **Step 1: Write failing tests** — formatação (`"1234.5"`, `"-0.00"`, `"9999999999999999.99"`, locale en-US/pt-BR); `Money` oculto não deixa o valor em nenhum atributo/texto (`outerHTML` não contém dígitos do valor); persistência com `localStorage` indisponível; `tick/tooltip` mascarados; `parseMoneyInput` (`"1.234,56"→"1234.56"`, `"12,345"→null`, `"abc"→null`, `"0,1"→"0.10"`); `10.20 + 0.10` não é feito em lugar algum (teste de `toPlot` só converte).
- [ ] **Step 2:** `npm test -- src/real/money` → FAIL.
- [ ] **Step 3:** Implementar.
- [ ] **Step 4:** `npm test` → PASS.
- [ ] **Step 5: Commit** `feat(web): formatação de dinheiro e modo esconder valores`.

### Task 6: Sessão, login e casca do app

**Files:**
- Create: `src/real/stores/session.ts`, `src/real/pages/Login.vue`, `src/real/components/AppShell.vue` (nav lateral/topo com Visão geral · Lançamentos · Contas · Importar, botão esconder valores, idioma PT/EN, sair), `src/real/i18n/pt-BR.json`, `src/real/i18n/en-US.json`, `src/real/i18n/i18n.test.ts`
- Modify: `src/real/RealApp.vue`, `src/real/router.ts`, `src/real/boot.ts`
- Test: `src/real/stores/session.test.ts`, `src/real/pages/Login.test.ts`, `src/real/router.test.ts`

**Interfaces:**
- Consumes: `ApiClient` (Task 4), `useValuesVisibility` (Task 5).
- Produces: `useSessionStore()` com `user: {email}|null`, `status: "unknown"|"anonymous"|"authenticated"`, `bootstrap()`, `login(email, password)`, `logout()`, `handleUnauthorized()`; rotas `/login`, `/`, `/transactions`, `/pending`, `/accounts`, `/import` com guard (`meta.requiresAuth`; sem sessão ⇒ `/login?next=<rota>`; após login volta a `next` se for rota interna válida); `provideApi(client)`/`useApi()` para injetar o `ApiClient`.

- [ ] **Step 1: Write failing tests** — sessão: `bootstrap_sets_authenticated_when_me_returns_user`, `login_failure_shows_generic_error_without_email_hint`, `unauthorized_redirects_to_login_preserving_next`, `logout_clears_state_and_redirects`; router: `guard_blocks_anonymous`, `next_param_rejects_external_urls` (ex.: `//evil.com`, `https://x`); Login: campos rotulados, botão desabilitado durante a requisição, Enter envia; i18n: `pt-BR_and_en-US_have_identical_key_sets`; casca: botão esconder valores tem `aria-pressed`.
- [ ] **Step 2:** `npm test -- src/real` → FAIL.
- [ ] **Step 3:** Implementar; `boot.ts` mescla as mensagens (`mergeLocaleMessage`) e chama `session.bootstrap()` antes de montar o roteador.
- [ ] **Step 4:** `npm test`, `npm run build:real` → verdes.
- [ ] **Step 5: Commit** `feat(web): login, sessão e casca do modo real`.

### Task 7: Contas e categorias

**Files:**
- Create: `src/real/stores/accounts.ts`, `src/real/stores/categories.ts`, `src/real/pages/Accounts.vue`, `src/real/components/AccountForm.vue`, `src/real/components/CheckpointForm.vue`, `src/real/components/CategorySelect.vue`
- Modify: textos em `src/real/i18n/*.json`
- Test: `src/real/stores/accounts.test.ts`, `src/real/pages/Accounts.test.ts`, `src/real/components/CategorySelect.test.ts`

**Interfaces:**
- Produces: `useAccountsStore()` (`items`, `active` (não arquivadas), `archived`, `load()`, `create`, `rename`, `setArchived`, `addCheckpoint`); `useCategoriesStore()` (`items`, `load()`, `forAmount(amount: string): Category[]` — positivo ⇒ Receita, negativo ⇒ Despesa, decidido só pelo **sinal do texto**, sem converter para número); `<CategorySelect v-model="string|null" :amount="string" />`.
- Tela Contas: lista com saldo (cartão negativo com rótulo "Dívida"), criar (nome, tipo `Checking|CreditCard|Wallet`, instituição, identificador externo opcional), renomear/arquivar, seção "Arquivadas" com desarquivar, "registrar saldo" (data + valor, texto explica que vale ao fim do dia).

- [ ] **Step 1: Write failing tests** — `forAmount_uses_sign_of_the_string` (`"-0.01"`, `"0.00"`, `"+5"` inválido tratado), `archived_accounts_move_to_archived_section`, `create_account_trims_and_validates_name_1_to_80`, `checkpoint_form_rejects_invalid_money_and_sends_string`, `card_balance_renders_with_debt_label_and_masks_when_hidden`, erros da API exibidos traduzidos sem vazar valores.
- [ ] **Step 2:** `npm test -- src/real` → FAIL.
- [ ] **Step 3:** Implementar.
- [ ] **Step 4:** `npm test`, `npm run build:real` → verdes.
- [ ] **Step 5: Commit** `feat(web): contas, saldos, checkpoints e seletor de categorias`.

### Task 8: Lançamentos e fila de pendentes

**Files:**
- Create: `src/real/stores/transactions.ts`, `src/real/pages/Transactions.vue`, `src/real/pages/Pending.vue`, `src/real/components/TransactionForm.vue`, `src/real/components/TransactionTable.vue`, `src/real/components/Pagination.vue`
- Test: `src/real/stores/transactions.test.ts`, `src/real/pages/Transactions.test.ts`, `src/real/pages/Pending.test.ts`

**Interfaces:**
- Consumes: Tasks 4, 5, 7.
- Produces: `useTransactionsStore()` (`items`, `page`, `size`, `total`, `filters {accountId?, month?, categoryId?, state?}`, `load()`, `create`, `update`, `remove`, `pendingCount` via `list({state:"PendingClassification", size:1}).total`); Transações: filtros, paginação, criar manual (conta ativa, data, valor com sinal via `parseMoneyInput`, descrição 1–300, categoria opcional), editar (inclui `state` e `categoryId`; **envia `categoryId` sempre**, `null` para limpar), apagar (importado: texto explica que fica oculto); Pendentes: lista só `PendingClassification`, classificar inline (estado + categoria) com atualização otimista e rollback em erro.

- [ ] **Step 1: Write failing tests** — `update_always_sends_categoryId_null_to_clear`; `filters_reset_page_to_1`; `page_beyond_range_shows_empty_state_not_error`; `create_rejects_comma_thousand_formats_before_sending`; `pending_inline_classify_updates_list_and_count`; `rollback_on_api_error`; `imported_delete_copy_mentions_hidden`; `archived_accounts_not_offered_in_form_but_present_in_filter`; `amounts_render_with_sign_and_mask_when_hidden`.
- [ ] **Step 2:** `npm test -- src/real` → FAIL.
- [ ] **Step 3:** Implementar.
- [ ] **Step 4:** `npm test`, `npm run build:real` → verdes.
- [ ] **Step 5: Commit** `feat(web): lançamentos, edição, reclassificação e fila de pendentes`.

### Task 9: Importar — prévia em abas, confirmar e desfazer

**Files:**
- Create: `src/real/stores/imports.ts`, `src/real/pages/Import.vue`, `src/real/components/ImportUpload.vue`, `src/real/components/ImportPreview.vue` (abas Novos · Pendentes · Já existentes · Avisos), `src/real/components/ImportHistory.vue`
- Test: `src/real/stores/imports.test.ts`, `src/real/components/ImportPreview.test.ts`, `src/real/pages/Import.test.ts`

**Interfaces:**
- Consumes: Task 2 (`overrides`), Tasks 4, 5, 7.
- Produces: `useImportsStore()` (`preview`, `overrides: Map<sourceKey, {state?, categoryId?}>`, `upload(accountId, file)`, `setOverride`, `confirm(ack: boolean)`, `undo(id)`, `history`); `<ImportPreview>` com abas navegáveis por teclado (`role="tablist"`, setas), aba Pendentes com seletor de estado/categoria por linha (`CategorySelect`), avisos `blocking` exigindo caixa "entendo e quero confirmar" para habilitar Confirmar, contagem "Confirmar N novos" (N vem do servidor), expiração da prévia (24 h) com botão de reenviar; histórico com estado, período, contagens e **desfazer** com confirmação que mostra `editedCount` e o aviso de que linhas puladas por outro lote também são removidas.
- Seleção de conta: só `Checking` para `.ofx` de conta e `CreditCard` para extrato de cartão — como o formato do arquivo só é conhecido pelo servidor, a tela lista contas ativas dos dois tipos e traduz o erro `account_type_mismatch` com instrução clara.
- Códigos de erro tratados e traduzidos: `not_ofx`, `empty`, `too_large`, `malformed`, `forbidden_dtd`, `unsupported_multiple_statements`, `account_type_mismatch`, `already_imported`, `already_confirmed`, `already_undone`, `account_archived`, `unsupported_amount_precision`, `unsupported_line`, `invalid_override`, e `410` (prévia expirada).

- [ ] **Step 1: Write failing tests** — `Upload_sends_account_and_file_and_shows_tabs_with_counts`; `Pending_tab_override_is_sent_on_confirm_only_for_new_lines`; `Blocking_warning_requires_acknowledgement`; `Confirm_422_invalid_override_stays_on_preview_with_error`; `Expired_preview_410_offers_resend`; `Each_known_error_code_has_a_translation_in_both_languages`; `Tabs_keyboard_navigation`; `Undo_confirmation_shows_editedCount_and_cross_batch_warning`; `Oversized_or_non_ofx_file_rejected_client_side_before_upload` (5 MB, extensão `.ofx`); `Descriptions_are_rendered_as_text_never_html` (memo com `<img onerror>` não vira DOM); `Amounts_masked_when_hidden`.
- [ ] **Step 2:** `npm test -- src/real` → FAIL.
- [ ] **Step 3:** Implementar.
- [ ] **Step 4:** `npm test`, `npm run build:real` → verdes.
- [ ] **Step 5: Commit** `feat(web): importação com prévia em abas, classificação, confirmar e desfazer`.

### Task 10: Visão geral

**Files:**
- Create: `src/real/stores/summary.ts`, `src/real/pages/Overview.vue`, `src/real/components/AccountStrip.vue`, `src/real/components/PendingNotice.vue`, `src/real/components/SummaryCards.vue`, `src/real/components/MonthPicker.vue`, `src/real/components/SixMonthChart.vue`, `src/real/components/CategoryChart.vue`, `src/real/components/RecentTransactions.vue`
- Test: `src/real/stores/summary.test.ts`, `src/real/pages/Overview.test.ts`, `src/real/components/charts.test.ts`

**Interfaces:**
- Consumes: Tasks 4, 5, 7, 8.
- Produces: `useSummaryStore()` (`month: "YYYY-MM"`, `current: Summary|null`, `history: Array<{month, summary|null, error?}>` com 6 chamadas paralelas ao resumo; falha parcial = lacuna + `retryMonth(month)`); Overview na ordem: faixa de contas ativas (cartão como dívida, "+ Adicionar conta" leva a Contas) → aviso global de pendentes (contagem, link para `/pending`; some quando 0) → cartões Receitas/Despesas/Resultado do mês (valores do servidor, sem percentuais) → gráfico 6 meses → despesas por categoria (`byCategory` do mês, nomes do servidor, "Sem categoria" quando nulo) → últimos lançamentos (5, mais recentes). Seletor de mês anterior/próximo (não passa do mês atual).
- Gráficos: Chart.js registrado só em `src/real`, valores via `toPlot`, tooltips/eixos via `chartMoney` (mascarados quando ocultos); estado vazio com atalho para Importar.

- [ ] **Step 1: Write failing tests** — `Six_parallel_calls_with_partial_failure_shows_gap_and_retry`; `Month_navigation_stops_at_current_month`; `Pending_notice_hidden_when_zero_and_links_when_positive`; `Cards_show_server_values_and_no_percent_change`; `Charts_mask_ticks_and_tooltips_when_hidden` (callbacks do Chart.js); `Uncategorized_label_when_category_null`; `Empty_state_links_to_import`; `Archived_accounts_not_in_strip`; `Document_title_has_no_values`.
- [ ] **Step 2:** `npm test -- src/real` → FAIL.
- [ ] **Step 3:** Implementar.
- [ ] **Step 4:** `npm test`, `npm run build`, `npm run build:real`, `node scripts/check-bundles.mjs` → verdes.
- [ ] **Step 5: Commit** `feat(web): Visão geral do modo real`.

### Task 11: Fechamento — documentação e verificação ponta a ponta

**Files:**
- Modify: `README.md` (modo real: como rodar `dev:real` + backend, `build:real`, `VITE_APP_MODE`, esconder valores, regenerar tipos e snapshot), `docs/adr/` (novo `0004-modo-de-build-e-camada-de-api-do-front.md`)
- Test: `src/real/e2e-smoke.test.ts` (fluxo integrado com `ApiClient` simulado: login → conta → importar com override → confirmar → resumo → desfazer)

- [ ] **Step 1: Write failing tests** — `e2e-smoke`: roda o fluxo acima nas stores/páginas reais com um `ApiClient` simulado coerente e verifica estados finais e que nenhum valor aparece quando "esconder" está ligado ao longo do fluxo.
- [ ] **Step 2:** `npm test -- src/real/e2e-smoke.test.ts` → FAIL.
- [ ] **Step 3:** Ajustar o que o smoke revelar; escrever README e ADR.
- [ ] **Step 4:** Verificação: `npm test`; `dotnet test backend/FinanceDashboard.sln`; `npm run build && npm run build:real && node scripts/check-bundles.mjs`; `docker compose up -d --build` (stack pode já estar no ar; usar o `.env` existente, nunca imprimi-lo); `curl -s -o /dev/null -w "%{http_code}" http://127.0.0.1:8080/` e `/login` ⇒ 200 e o HTML servido referencia assets do modo real; `curl http://127.0.0.1:8080/api/health/ready` ⇒ Healthy. Não parar a stack.
- [ ] **Step 5: Commit** `docs(web): modo real, ADR 0004 e verificação da S1b`.
