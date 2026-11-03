# S3(c) Regras de categoria Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Categorizar sozinho, por regras do usuário, linhas importadas sem categoria, sem nunca contrariar uma decisão do usuário.

**Architecture:** Entidade `CategoryRule` + `CategoryRuleMatcher` puro (Domain), `CategoryRuleService` (aplicação sob o lock por usuário, `ExecuteUpdate` guardado) chamado na confirmação da importação e sob demanda, endpoints `/api/category-rules`, página Regras e atalho na edição, demo com regras de exemplo.

**Tech Stack:** .NET 10, EF Core + Npgsql, xUnit + Testcontainers (`postgres:17.11-trixie`), Vue 3 + Pinia + vue-i18n + vitest, openapi-typescript.

**Spec:** `docs/superpowers/specs/2026-11-03-finance-v2-s3c-regras-de-categoria-design.md` (inclui "Respostas do builder check"). Contexto: S3(a)/(b) (`LinkService`, ADR 0007/0008).

## Global Constraints

- Branch `feat/v2-s3c-regras-de-categoria`. Commits terminam com as duas linhas: `Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>` e `Claude-Session: https://claude.ai/code/session_01LyReQVTVdSLwPd47UPx4Ls`.
- `Pattern`: normalizado por `NameKeys.Normalize`, 3–100 caracteres após normalizar, entrada bruta ≤ 200; casamento por **limite de palavra** em `NameKeys.Normalize(OriginalDescription)`; pontuação preservada; mais longo vence, empate pela regra mais antiga (`CreatedAt`, depois `Id`); `Expense` só em valor < 0, `Income` só em valor > 0, valor 0 nunca casa.
- Candidatas: linhas importadas (`BatchId` não nulo), visíveis, `Normal`, `CategoryId` nulo, `!EditedByUser`, sem vínculo `Confirmed`. Atribuição por `ExecuteUpdate` com o guarda repetido no `WHERE`; não marca `EditedByUser`.
- **Dados sintéticos apenas** em testes, fixtures, docs e briefings; usar nomes como "SUPERMERCADO EXEMPLO", "ASSINATURA FICTICIA". **Nunca ler nem imprimir `finance-samples/` ou `.env`**; nenhum trecho de memo real nos arquivos commitados.
- gitleaks via PowerShell antes de push (exe em `C:\Users\JPST\AppData\Local\Microsoft\WinGet\Packages\Gitleaks.Gitleaks_Microsoft.Winget.Source_8wekyb3d8bbwe\gitleaks.exe git --config .gitleaks.toml --log-opts "main..HEAD"`). Sem force-push em main, sem `--no-verify`, sem `docker compose down -v`.
- Isolamento por usuário (`IUserOwned` + filtro global + `HasQueryFilter`); 404 para recurso de outro usuário; escritas com `AntiforgeryFilter.ValidateAsync`.
- Comandos: `cd backend && dotnet test`; `npm test`, `npm run build`, `npm run build:real`, `node scripts/check-bundles.mjs`, `npm run api:types`. Snapshot OpenAPI: `UPDATE_OPENAPI_SNAPSHOT=1 dotnet test --filter OpenApiSnapshot`; fixtures do demo: `UPDATE_DEMO_FIXTURES=1 dotnet test --filter DemoFixtures` (nunca na CI). Migração: `cd backend && dotnet tool restore && dotnet ef migrations add S3cCategoryRules -p src/FinanceDashboard.Infrastructure -s src/FinanceDashboard.Api`.
- Cada commit deixa a suíte inteira verde (snapshot OpenAPI e fixtures do demo são atualizados na tarefa que muda contrato/semente). i18n com as mesmas chaves em `pt-BR.json` e `en-US.json`; contagens inteiras da API entram em `IntFields` (`src/real/api/types.ts`); novo código de erro em `KNOWN_ERROR_CODES`.

## Review Focus

1. Uma categoria que o usuário acabou de gravar (edição só de categoria, sem lock) nunca é sobrescrita pela aplicação (guarda no `WHERE`); linhas editadas e manuais nunca são candidatas. Task 2.
2. "BAR" não pega "BARBEARIA"; "MARIA" não pega "MARIANA"; "IFOOD *LOJA" não casa com "IFOOD LOJA". Task 1.
3. Excluir uma categoria durante a confirmação de uma importação não derruba a importação nem deixa categoria órfã. Task 2.
4. Estorno (valor positivo) não recebe categoria de despesa; valor 0 nunca casa. Task 1/2.
5. Regra com padrão duplicado em corrida devolve 409 `duplicate_rule`, nunca 500. Task 3.

---

## File Structure

- Domain: `CategoryRule.cs`, `CategoryRuleMatcher.cs` (puro).
- Infrastructure: `AppDbContext.cs` (mapeamento + filtro), migração `S3cCategoryRules`, `Categorization/CategoryRuleService.cs`, extração do helper de lock (`Linking/UserLock.cs` ou equivalente) usado por `LinkService` e `CategoryRuleService`, `Importing/ImportService.cs` (`Categorized`).
- API: `CategoryRules/CategoryRuleEndpoints.cs`, `Program.cs`, `Importing` (resultado), `openapi/v1.json`.
- Front: `src/real/api/*`, `stores/categoryRules.ts`, `pages/Rules.vue`, `router.ts`, `AppShell.vue`, `pages/Transactions.vue` (atalho), `pages/Import.vue`, i18n, `errorCode.ts`, `src/demo-static/*`.
- Docs: ADR `0009-regras-de-categoria.md`, README, `docs/away-log.md`.

---

### Task 1: Domínio — `CategoryRule` e `CategoryRuleMatcher`

**Files:**
- Create: `backend/src/FinanceDashboard.Domain/CategoryRule.cs`, `CategoryRuleMatcher.cs`
- Test: `backend/tests/FinanceDashboard.Tests/Domain/CategoryRuleMatcherTests.cs`

**Interfaces:**
- Produces:
  - `class CategoryRule : IUserOwned { Guid Id; string UserId; string Pattern; Guid CategoryId; DateTimeOffset CreatedAt; }`
  - `record RuleView(Guid Id, string Pattern, Guid CategoryId, CategoryKind Kind, DateTimeOffset CreatedAt)`
  - `static class CategoryRuleMatcher { public static bool Matches(string normalizedDescription, string pattern); public static Guid? Winner(string originalDescription, decimal amount, IReadOnlyList<RuleView> rules); public static string? NormalizePattern(string? raw); public static string? SuggestPattern(string originalDescription); }`
  - `NormalizePattern`: `NameKeys.Normalize`, devolve `null` se vazio ou fora de 3–100 caracteres. `SuggestPattern`: remove documentos mascarados (sequências com `*`), datas `DD/MM` ou `DD MM` finais e números finais, preserva pontuação, depois `NormalizePattern`.
  - `Winner`: descarta regras de sinal incompatível, valor 0 ⇒ `null`; casa por limite de palavra; mais longo vence, empate `CreatedAt` e `Id`.

- [ ] **Step 1: Testes (falhando)**: `Matches_requires_word_boundaries` (BAR × BARBEARIA falso; MARIA × MARIANA falso; "SUPERMERCADO EXEMPLO" × "COMPRA SUPERMERCADO EXEMPLO 12" verdadeiro), `Matches_keeps_punctuation` ("IFOOD *LOJA" × "IFOOD LOJA" falso), `Winner_longest_pattern_wins`, `Winner_tie_prefers_oldest_then_id`, `Winner_ignores_kind_incompatible_with_sign` (Income em valor negativo; Expense em estorno), `Winner_zero_amount_matches_nothing`, `NormalizePattern_bounds` (2 e 101 caracteres ⇒ null; espaços e acento normalizados), `SuggestPattern_strips_masked_documents_dates_and_trailing_numbers`, e `Property_winner_is_deterministic_under_rule_order` (`[Theory]` com `seed`, embaralha a lista de regras).
- [ ] **Step 2: Rodar** `cd backend && dotnet test --filter CategoryRuleMatcherTests` → falha por tipos ausentes.
- [ ] **Step 3: Implementar** as assinaturas acima.
- [ ] **Step 4: Rodar** `dotnet test` completo → verde.
- [ ] **Step 5: Commit** `feat(domain): regra de categoria e matcher`.

---

### Task 2: Persistência e `CategoryRuleService`

**Files:**
- Modify: `backend/src/FinanceDashboard.Infrastructure/AppDbContext.cs`, `Linking/LinkService.cs` (extrair o helper do lock), `Importing/ImportService.cs`
- Create: migração `S3cCategoryRules`, `Categorization/CategoryRuleService.cs`, helper de lock compartilhado
- Test: `Persistence/CategoryRulePersistenceTests.cs`, `Categorization/CategoryRuleServiceTests.cs`, `Linking/ImportCategorizationTests.cs`

**Interfaces:**
- Consumes: `CategoryRuleMatcher` (Task 1).
- Produces:
  - `DbSet<CategoryRule> CategoryRules` com filtro global por usuário; FK para `Category` `ON DELETE CASCADE`; único `(UserId, Pattern)`; `Pattern` `varchar(100)`.
  - Helper compartilhado (mesma chave `pg_advisory_xact_lock(hashtextextended(userId, 0))`, reentrante, junta transação ambiente) usado pelo `LinkService` sem mudar o comportamento dele.
  - `CategoryRuleService(AppDbContext db, TimeProvider time)`: `Task<int> ApplyAsync(Guid? batchId = null, CancellationToken ct = default)` (aplica às candidatas do usuário; com `batchId` restringe ao lote; devolve quantas linhas mudaram), `Task<IReadOnlyList<RuleListItem>> ListAsync(ct)` com `RuleListItem(Guid Id, string Pattern, Guid CategoryId, string CategoryName, DateTimeOffset CreatedAt, int Matches)`, `Task<CreateRuleResult> CreateAsync(string pattern, Guid categoryId, ct)` (`CreateRuleResult(CreateRuleOutcome Outcome, RuleListItem? Rule)`; outcomes `Created, InvalidPattern, CategoryNotFound, Duplicate`; captura a violação de unicidade), `Task<bool> DeleteAsync(Guid id, ct)`.
  - `ConfirmResult` ganha `Categorized` (último parâmetro); `ImportService.ConfirmAsync` chama `ApplyAsync(batchId)` dentro da transação, depois da reconciliação; override de categoria do preview tem precedência (linha com categoria não é candidata).

- [ ] **Step 1: Testes (falhando)** (Postgres real): persistência (`Pattern_unique_per_user`, `Rule_cascades_when_category_deleted`, `Rules_isolated_between_users`, migração sobre base da S3(b) com dados); serviço (`Apply_assigns_winner_to_uncategorized_imported_normal_lines`; `Apply_never_overwrites_existing_category`; `Apply_skips_edited_manual_hidden_pending_and_confirmed_linked_lines`; `Apply_does_not_mark_edited_by_user`; `Apply_is_idempotent`; `Apply_guard_keeps_category_written_concurrently` — grava a categoria por outro contexto entre a leitura das candidatas e o update e confirma que ela fica (Review Focus 1); `Apply_ignores_expense_rule_on_refund`; `Apply_survives_category_deleted_concurrently` (Review Focus 3); `List_matches_counts_candidates_without_precedence`; `Create_outcomes`; `Create_duplicate_race_returns_Duplicate` (duas criações simultâneas, Review Focus 5); `Delete_does_not_undo_assigned_categories`; import (`Import_confirm_categorizes_new_lines_and_reports_count`; `Import_preview_override_category_wins_over_rule`; `Import_without_rules_reports_zero`).
- [ ] **Step 2: Rodar** `--filter "CategoryRule|ImportCategorization"` → falha.
- [ ] **Step 3: Implementar** mapeamento + migração (gerar com o comando do cabeçalho; `Up` só cria a tabela e o índice único), o helper de lock, o serviço (normalizar cada candidata uma vez; `ExecuteUpdate` com o guarda no `WHERE`; travar as categorias das regras com `FOR KEY SHARE` como `LockCategoriesAsync` do import) e a chamada no import.
- [ ] **Step 4: Rodar** `dotnet test` completo → verde (o snapshot OpenAPI só muda se `ConfirmResult` for exposto; se a suíte exigir, atualizar agora com `UPDATE_OPENAPI_SNAPSHOT=1` e conferir o diff).
- [ ] **Step 5: Commit** `feat(categorization): regras de categoria e aplicação no import`.

---

### Task 3: API

**Files:**
- Create: `backend/src/FinanceDashboard.Api/CategoryRules/CategoryRuleEndpoints.cs`
- Modify: `Program.cs`, resposta de confirmação da importação (`Categorized`), `backend/openapi/v1.json`
- Test: `backend/tests/FinanceDashboard.Tests/CategoryRules/CategoryRuleApiTests.cs`

**Interfaces:**
- Consumes: `CategoryRuleService` (Task 2).
- Produces (HTTP): `GET /api/category-rules` → `[CategoryRuleResponse { id, pattern, categoryId, categoryName, createdAt, matches }]` (todos obrigatórios); `POST /api/category-rules` `{ pattern, categoryId }` → 201; 400 padrão inválido; 404 categoria inexistente; 409 `{ error: "duplicate_rule" }`; `DELETE /api/category-rules/{id}` → 204/404; `POST /api/category-rules/apply` → `{ applied }`; a resposta de confirmação da importação inclui `categorized`.

- [ ] **Step 1: Testes (falhando)**: CRUD completo; `Other_user_gets_404_on_delete`; `Requests_without_antiforgery_are_rejected`; `401_without_session`; `Create_invalid_pattern_400` (2 e 101 caracteres, só espaços, > 200 bruto); `Create_unknown_category_404`; `Create_duplicate_409_duplicate_rule` (padrão equivalente após normalizar); `List_matches_reflect_candidates`; `Apply_endpoint_returns_applied_and_summary_by_category_changes` (a linha sai de "sem categoria" para a categoria; receita/despesa/saldo iguais); `Import_confirm_reports_categorized`.
- [ ] **Step 2: Rodar** `--filter CategoryRuleApiTests` → falha.
- [ ] **Step 3: Implementar** endpoints/DTOs (`.Produces` de 400/401/404/409), registrar no `Program.cs`, atualizar o snapshot (`UPDATE_OPENAPI_SNAPSHOT=1`), conferir o diff (aditivo) e rodar sem a variável; `npm run api:types` e commitar `src/real/api/schema.d.ts` (o front deve continuar compilando: `npm run build:real`). Se as fixtures do demo divergirem só por `categorized`, regenerar com `UPDATE_DEMO_FIXTURES=1` e revisar.
- [ ] **Step 4: Rodar** `dotnet test` completo, `npm test`, `npm run build:real` → verdes.
- [ ] **Step 5: Commit** `feat(api): endpoints de regras de categoria`.

---

### Task 4: Front real

**Files:**
- Modify: `src/real/api/{types.ts,client.ts,http.ts}`, `src/real/stores/errorCode.ts`, `router.ts`, `src/real/components/AppShell.vue`, `pages/{Transactions,Import}.vue`, i18n pt-BR/en-US, `src/demo-static/staticApi.ts` (stubs `readOnly()`)
- Create: `src/real/stores/categoryRules.ts`, `src/real/pages/Rules.vue`
- Test: ao lado dos arquivos

**Interfaces:**
- Consumes: endpoints da Task 3.
- Produces: `ApiClient.categoryRules.list()/create({pattern, categoryId})/remove(id)/apply()`; `useCategoryRulesStore()` com `rules`, `busy`, `lastApplied`, `load()`, `create()`, `remove()`, `apply()` (recarrega regras, lançamentos e resumo) e `suggested: { pattern, categoryId } | null` para o atalho; `duplicate_rule` em `KNOWN_ERROR_CODES`; `matches`, `applied`, `categorized` em `IntFields`.

Comportamento: página **Regras** (rota `/rules`, título `real.titles.rules`, item de menu) com tabela padrão/categoria/linhas que pega (com dica de que o total pode somar mais que o aplicado), formulário (padrão + `CategorySelect` sem filtro de sinal), exclusão com confirmação, botão "Aplicar regras" desabilitado em voo com resumo "N linhas categorizadas" (plural `real.plural.categorized`: nenhuma | 1 | {n}); em Lançamentos, depois de salvar uma edição com categoria, aviso visível "Sempre categorizar «TRECHO» como CATEGORIA" cujo botão leva a `/rules` com a sugestão da store (trecho de `SuggestPattern` equivalente no front, a partir de `originalDescription`, sem datas/números finais/documentos mascarados, editável); resultado da importação mostra "N categorizadas" quando > 0; erros 404/409/400 mapeados; textos de terceiros como texto (nunca HTML).

- [ ] **Step 1: Testes (falhando)**: store (load, create com `duplicate_rule`, remove, apply recarrega e mostra o resumo, busy impede duplo clique); página (tabela, formulário, confirmação de exclusão, plurais 0/1/2 em pt-BR e en-US, XSS com `<b>` literal no padrão); atalho na edição (aparece só se salvou categoria, usa `originalDescription`, remove datas/números/documentos mascarados, leva a sugestão); resultado da importação; menu/rota.
- [ ] **Step 2: Rodar** `npm test` → falha.
- [ ] **Step 3: Implementar** client/http/store/página/rota/menu/i18n/atalho; stubs `readOnly()` no adaptador estático (listagem pelo demo na Task 5).
- [ ] **Step 4: Rodar** `npm test`, `npm run build`, `npm run build:real`, `node scripts/check-bundles.mjs` → verdes.
- [ ] **Step 5: Commit** `feat(front): página Regras e atalho para criar regra`.

---

### Task 5: Demo, docs e fechamento

**Files:**
- Modify: `backend/tests/FinanceDashboard.Tests/DemoFixtures/{DemoSeed.cs,DemoFixtureGenerator.cs,DemoFixturesTests.cs}`, `src/demo-static/{fixtures.ts,staticApi.ts}` + testes, fixtures regeneradas; ADR novo `docs/adr/0009-regras-de-categoria.md`; `README.md`; `docs/away-log.md`

**Interfaces:**
- Produces: `fixtures/rules.json` gerado pelo servidor real; `staticApi.categoryRules.list` lê dele; criar/excluir/aplicar via `readOnly()`.

A semente ganha 2–3 regras (ex.: "SUPERMERCADO EXEMPLO" → Alimentação, "ASSINATURA FICTICIA" → Assinaturas) criadas pelo serviço real; algumas linhas importadas ficam sem categoria no template, e a regra é aplicada para parte delas, restando pelo menos uma regra com `matches > 0`; "Transferência Fictícia" continua sem categoria e fora de qualquer regra. Totais do resumo (receita/despesa/saldo) não mudam; só a distribuição por categoria.

- [ ] **Step 1: Testes**: fixtures contêm `rules.json` com regras e `matches`; `staticApi.categoryRules.list`; e2e do demo abre Regras sem chamada à API; `Committed_fixtures_match_generator` verde.
- [ ] **Step 2: Rodar** → falha.
- [ ] **Step 3: Implementar** semente/gerador/adaptador; regenerar com `UPDATE_DEMO_FIXTURES=1`, revisar o diff (somente categoria/regras sintéticas) e rodar sem a variável.
- [ ] **Step 4: ADR 0009** (curto): casamento por palavra e mais longo vence; só linhas importadas não editadas, sem categoria, `Normal` e sem vínculo `Confirmed`; `ExecuteUpdate` guardado; apagar regra/categoria; `EditedByUser` não marcado; limites (sem prévia no import, sem edição de regra, estornos). README: seção "Regras de categoria". Resumo de 5 linhas no `docs/away-log.md`.
- [ ] **Step 5: Verificação completa**: `cd backend && dotnet test`, `npm test`, `npm run build`, `npm run build:real`, `node scripts/check-bundles.mjs`, gitleaks. Commit `feat(demo): regras de exemplo; docs da S3(c)`.
- [ ] **Step 6: Revisão final do branch** com revisor da capacidade máxima, uma onda de correções, re-revisão escopada; push, PR (corpo pt-BR com atribuição), CI verde, merge, memória atualizada, workspace apagado, parar (S3(d)/S4 aguardam o João).

---

## Self-Review

- **Cobertura do spec:** modelo/matcher (T1), persistência/aplicação/lock/import (T2), API (T3), front incl. atalho e plurais (T4), demo/docs (T5). Não-objetivos sem tarefa.
- **Tipos consistentes:** `CategoryRule`, `RuleView`, `CategoryRuleMatcher.{Matches,Winner,NormalizePattern,SuggestPattern}`, `RuleListItem`, `CreateRuleOutcome`, `ConfirmResult.Categorized` iguais nas tarefas; `matches/applied/categorized` em `IntFields`.
- **Riscos:** T2 mexe no `LinkService` só para extrair o lock (comportamento igual, testes da S3 cobrem); `ConfirmResult` posicional muda construtores; limite de palavra deixa regras de pedaço de palavra sem efeito.
- **Proporção:** só assinaturas, regras e testes nomeados.
