# S3(d) Parcelas comprometidas Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Projetar as parcelas restantes das compras parceladas do cartão Itaú e mostrar o comprometido por mês (página Parcelas + cartão na Visão geral), sem persistir nada.

**Architecture:** `InstallmentProjector` puro (Domain) alimentado por um serviço de leitura (Infrastructure) que seleciona as linhas parceladas importadas; endpoint `GET /api/installments`; store, página e cartão no front; demo com fixtures geradas pelo servidor real.

**Tech Stack:** .NET 10, EF Core + Npgsql, xUnit + Testcontainers (`postgres:17.11-trixie`), Vue 3 + Pinia + vue-i18n + vitest, openapi-typescript.

**Spec:** `docs/superpowers/specs/2026-11-03-finance-v2-s3d-parcelas-design.md` (inclui "Fatos do código", "Decisões minhas").

## Global Constraints

- Branch `feat/v2-s3d-parcelas`. Commits terminam com as duas linhas: `Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>` e `Claude-Session: https://claude.ai/code/session_01LyReQVTVdSLwPd47UPx4Ls`.
- Cobrança = `Amount < 0`; a API devolve **módulos positivos como string decimal** (conversor de decimais e padrão OpenAPI do projeto; no front, dinheiro nunca vira `number` fora dos gráficos: `toPlot`). Inteiros da API entram em `IntFields` (`src/real/api/types.ts`).
- Fatura-base por conta = maior `StatementDate` entre linhas **importadas** da conta de cartão; só linhas parceladas dessa fatura entram. Parcela `N+i` cai em `BasedOn.AddMonths(i - 1)` (sempre a partir da base) e aparece no **mês dessa data**. "Hoje" (`TimeProvider`, America/Sao_Paulo): só parcelas com data ≥ primeiro dia do mês corrente. Horizonte 36 parcelas. `T ≥ 2`, `1 ≤ N ≤ T`. Contas arquivadas incluídas; linhas ocultas/excluídas e manuais fora.
- **Dados sintéticos apenas**; nunca ler nem imprimir `finance-samples/` ou `.env`; nada de memo real em arquivos commitados.
- gitleaks via PowerShell antes de push (exe em `C:\Users\JPST\AppData\Local\Microsoft\WinGet\Packages\Gitleaks.Gitleaks_Microsoft.Winget.Source_8wekyb3d8bbwe\gitleaks.exe git --config .gitleaks.toml --log-opts "main..HEAD"`). Sem force-push em main, sem `--no-verify`, sem `docker compose down -v`.
- Comandos: `cd backend && dotnet test`; `npm test`, `npm run build`, `npm run build:real`, `node scripts/check-bundles.mjs`, `npm run api:types` (**sempre depois de qualquer mudança no OpenAPI**; a CI compara `schema.d.ts`). Snapshot OpenAPI: `UPDATE_OPENAPI_SNAPSHOT=1 dotnet test --filter OpenApiSnapshot`; fixtures do demo: `UPDATE_DEMO_FIXTURES=1 dotnet test --filter DemoFixtures` (nunca na CI).
- Cada commit deixa a suíte inteira verde. i18n com as mesmas chaves em `pt-BR.json` e `en-US.json` (plurais nenhuma | 1 | N via `t(key, {n}, n)`); `real.nav.installments` e `real.titles.installments`.

## Review Focus

1. Sinal e mês: uma parcela de −R$ 100,00 na fatura-base é projetada como 100,00 positivo no mês em que a parcela cai no resumo (vencimento−1+i), nunca como estorno e nunca no mês do vencimento. Task 1.
2. Fatura-base desatualizada ou parcela de fatura antiga: não aparece como comprometida no passado; a API informa `basedOn` para a tela avisar. Task 1/2.
3. Uma linha manual ou de pagamento no cartão com data futura não vira "fatura mais recente" e não zera os grupos. Task 2.
4. Vencimento dia 31 não arrasta para 28 nas parcelas seguintes. Task 1.
5. Duas compras na mesma loja em estágios diferentes (2/10 e 5/10) projetam 8 + 5 parcelas, não 5×2. Task 1.

---

## File Structure

- Domain: `InstallmentProjector.cs` (puro) + records.
- Infrastructure: `Installments/InstallmentService.cs` (seleciona linhas, chama o projetor).
- API: `Installments/InstallmentEndpoints.cs`, `Program.cs`, `openapi/v1.json`.
- Front: `src/real/api/*`, `stores/installments.ts`, `pages/Installments.vue`, `components/CommittedCard.vue`, `pages/Overview.vue`, `router.ts`, `components/AppShell.vue`, i18n, `src/demo-static/*`.
- Docs: ADR `0010-parcelas-derivadas.md`, README, `docs/away-log.md`.

---

### Task 1: Domínio — `InstallmentProjector`

**Files:**
- Create: `backend/src/FinanceDashboard.Domain/InstallmentProjector.cs`
- Test: `backend/tests/FinanceDashboard.Tests/Domain/InstallmentProjectorTests.cs`

**Interfaces:**
- Produces:
  - `record InstallmentLine(Guid AccountId, string AccountName, string? CardSuffix, string Description, string NormalizedDescription, int Number, int Total, decimal Amount, DateOnly StatementDate)` (`Amount` negativo, já filtrado pela camada de aplicação).
  - `record AccountBase(Guid AccountId, string AccountName, DateOnly BasedOn)`; `record InstallmentItem(Guid AccountId, string Description, string? CardSuffix, int InstallmentNumber, int InstallmentTotal, decimal Amount)`; `record InstallmentMonth(DateOnly Month, decimal Total, IReadOnlyList<InstallmentItem> Items)`; `record InstallmentGroupView(Guid AccountId, string AccountName, string? CardSuffix, string Description, int InstallmentNumber, int InstallmentTotal, int Remaining, decimal Amount, int Multiplicity, DateOnly EndsOn, decimal RemainingTotal)`; `record InstallmentProjection(decimal TotalCommitted, IReadOnlyList<AccountBase> Accounts, IReadOnlyList<InstallmentMonth> Months, IReadOnlyList<InstallmentGroupView> Groups)`.
  - `static class InstallmentProjector { public const int MaxInstallments = 36; public static InstallmentProjection Project(IReadOnlyList<InstallmentLine> lines, DateOnly today); }` — recebe TODAS as linhas parceladas importadas (de qualquer fatura); o projetor escolhe a fatura-base por conta (maior `StatementDate`), projeta por linha da base, aplica "hoje", agrupa para exibição por (conta, `NormalizedDescription`, N, T, valor) com `Multiplicity`, e ordena (meses crescentes; itens por conta, descrição, cartão; grupos por `EndsOn` e descrição). Valores em módulo. `Amount >= 0` é ignorado defensivamente.

- [ ] **Step 1: Testes (falhando)**: `Project_three_of_ten_gives_seven_remaining_in_the_month_the_line_would_fall` (base 2026-10-10, parcela 3/10 de −100,00, hoje 2026-10-15 ⇒ itens em 2026-10 (data 10/10), 2026-11, …, 2027-04; 7 itens de 100,00 positivos; Review Focus 1), `Project_uses_AddMonths_from_base_without_drifting_day_31` (base 31/01 ⇒ 28/02 e 31/03; Review Focus 4), `Project_ignores_completed_group_N_equals_T`, `Project_only_latest_invoice_lines_count` (linha na fatura anterior que não está na base não projeta), `Project_cuts_installments_before_today_month` (base antiga ⇒ só parcelas ≥ mês corrente; `Accounts` ainda informa `BasedOn`; Review Focus 2), `Project_same_store_different_stages_projects_each_line` (2/10 e 5/10 ⇒ 8 + 5 itens, grupos separados; Review Focus 5), `Project_identical_lines_get_multiplicity` (duas linhas idênticas ⇒ 1 grupo `Multiplicity` 2, valores dobrados nos meses), `Project_separate_accounts_have_separate_bases`, `Project_ignores_non_negative_amounts`, `Project_horizon_is_36_installments`, `Project_groups_remaining_total_and_total_committed_agree` (soma de `Months` = `TotalCommitted` = soma dos `RemainingTotal`), `Project_empty_input_gives_empty_projection`, e `Property_projection_is_independent_of_input_order` (`[Theory]` com `seed`, embaralha as linhas).
- [ ] **Step 2: Rodar** `cd backend && dotnet test --filter InstallmentProjectorTests` → falha por tipos ausentes.
- [ ] **Step 3: Implementar** as assinaturas acima (`StatementDate.AddMonths(i - 1)` sempre a partir da base).
- [ ] **Step 4: Rodar** `dotnet test` completo → verde.
- [ ] **Step 5: Commit** `feat(domain): projeção de parcelas comprometidas`.

---

### Task 2: Serviço de leitura e API

**Files:**
- Create: `backend/src/FinanceDashboard.Infrastructure/Installments/InstallmentService.cs`, `backend/src/FinanceDashboard.Api/Installments/InstallmentEndpoints.cs`
- Modify: `Program.cs`, registro em `InfrastructureServiceCollectionExtensions.cs`, `backend/openapi/v1.json`, `src/real/api/schema.d.ts`, `src/real/api/types.ts`
- Test: `backend/tests/FinanceDashboard.Tests/Installments/InstallmentApiTests.cs`

**Interfaces:**
- Consumes: `InstallmentProjector` (Task 1), `TimeProvider`.
- Produces: `InstallmentService(AppDbContext db, TimeProvider time)` com `Task<InstallmentProjection> GetAsync(CancellationToken ct = default)`: seleciona linhas `Origin = Imported`, `Visible()`, conta `CreditCard` (arquivada incluída), `StatementDate != null`, `Amount < 0`, `InstallmentNumber`/`InstallmentTotal` não nulos com `T >= 2` e `1 <= N <= T`; `Description` = `NormalizedDescription` exibida; chave de normalização `NameKeys.Normalize(OriginalDescription)`; `today` = data atual em America/Sao_Paulo. HTTP: `GET /api/installments` → `InstallmentsResponse { totalCommitted: string, accounts: [{accountId, accountName, basedOn}], months: [{month: "YYYY-MM", total: string, items: [{accountId, description, cardSuffix, installmentNumber, installmentTotal, amount}]}], groups: [{accountId, accountName, cardSuffix, description, installmentNumber, installmentTotal, remaining, amount, multiplicity, endsOn, remainingTotal}] }`; dinheiro como string decimal no padrão do projeto; todos os campos obrigatórios (`cardSuffix` anulável); `.RequireAuthorization()`, `.WithName/.WithSummary/.Produces<T>()/.Produces(401)`.

- [ ] **Step 1: Testes (falhando)** (Postgres real, relógio fixo): resposta com parcelas projetadas e dinheiro como string; `Manual_and_payment_lines_do_not_define_the_base` (linha manual no cartão com data futura e linha positiva de pagamento não mudam a base; Review Focus 3); `Hidden_and_deleted_lines_are_ignored`; `Archived_account_is_included`; `Other_user_lines_are_not_returned` e isolamento; `401_without_session`; `Stale_base_cuts_past_months_and_reports_basedOn`; `Nubank_card_lines_without_installments_are_ignored`; estado vazio devolve listas vazias e `totalCommitted` "0.00".
- [ ] **Step 2: Rodar** `--filter InstallmentApiTests` → falha.
- [ ] **Step 3: Implementar** serviço, endpoint e DTOs; atualizar o snapshot (`UPDATE_OPENAPI_SNAPSHOT=1`), conferir o diff (aditivo), rodar sem a variável; `npm run api:types` e commitar `schema.d.ts`; acrescentar os campos inteiros (`installmentNumber`, `installmentTotal`, `remaining`, `multiplicity`) ao `IntFields` com tipos aninhados em `types.ts` como feito para `LinkPage`/`ImportPreview`; manter `npm run build:real` e `npm test` verdes (fixtures do demo regeneradas só se divergirem, revisando o diff).
- [ ] **Step 4: Rodar** `dotnet test` completo, `npm test`, `npm run build`, `npm run build:real` → verdes.
- [ ] **Step 5: Commit** `feat(api): endpoint de parcelas comprometidas`.

---

### Task 3: Front real

**Files:**
- Create: `src/real/stores/installments.ts`, `src/real/pages/Installments.vue`, `src/real/components/CommittedCard.vue`
- Modify: `src/real/api/{client.ts,http.ts}`, `router.ts`, `src/real/components/AppShell.vue`, `src/real/pages/Overview.vue`, i18n pt-BR/en-US, `src/demo-static/staticApi.ts` (método `installments.get()` devolvendo vazio por enquanto)
- Test: ao lado dos arquivos

**Interfaces:**
- Consumes: `GET /api/installments` (Task 2), `Money.vue`, `chartMoneyOptions`/`toPlot`.
- Produces: `ApiClient.installments.get(): Promise<Installments>`; `useInstallmentsStore()` com `data`, `loading`, `error`, `load()`; helper puro `nextMonthsTotal(data, today, n = 3)` (soma `months` do mês corrente + `n-1`); `CommittedCard` (some se o total é 0).

Comportamento: página `/installments` (`real.titles.installments`, item de menu `real.nav.installments`): barras do comprometido por mês, lista de grupos (descrição, conta/cartão, "N de T", restantes com plural, valor, termina em), aviso por conta quando `basedOn` é anterior ao mês atual ("baseado na fatura de MM/AAAA; importe a mais recente"), estado vazio explicado; Visão geral com `CommittedCard` (próximos 3 meses a partir do mês corrente, link para a página, fora do MonthPicker, valores escondidos respeitados, some quando 0). Textos de terceiros sempre como texto.

- [ ] **Step 1: Testes (falhando)**: store (load/erro), `nextMonthsTotal` (limites de mês, string decimal sem virar `number` fora do gráfico), página (barras e lista, aviso de base antiga, estado vazio, plural 0/1/2 em pt-BR e en-US, XSS com `<b>` literal, esconder valores), cartão (aparece/some, link), menu/rota/título.
- [ ] **Step 2: Rodar** `npm test` → falha.
- [ ] **Step 3: Implementar** client/http/store/página/cartão/rota/menu/i18n/stub do adaptador estático.
- [ ] **Step 4: Rodar** `npm test`, `npm run build`, `npm run build:real`, `node scripts/check-bundles.mjs` → verdes.
- [ ] **Step 5: Commit** `feat(front): página Parcelas e cartão de comprometido`.

---

### Task 4: Demo e docs

**Files:**
- Modify: `backend/tests/FinanceDashboard.Tests/DemoFixtures/{DemoSeed.cs,DemoFixtureGenerator.cs,DemoFixturesTests.cs}`, `src/demo-static/{fixtures.ts,staticApi.ts}` + testes, `src/real/api/http.ts` se a interface do demo exigir, fixtures regeneradas; ADR `docs/adr/0010-parcelas-derivadas.md`; `README.md`; `docs/away-log.md`

**Interfaces:**
- Produces: `fixtures/installments.json` gerado pelo servidor real; `staticApi.installments.get()` lê dele.

A semente: linhas de fatura Itaú do cartão (conta Itaú cartão) passam a ter `StatementDate` (vencimento) coerente com o lote; 2 compras parceladas com parcelas restantes na fatura-base (por exemplo 3/6 e 2/4, descrições sintéticas "Loja Eletrônicos Exemplo" e "Móveis Fictícios", mesmo `CardSuffix` fictício em todas as parcelas), mantendo `Date` das parcelas N>1 = vencimento−1 mês como o leitor real; totais declarados da fatura, resumos e asserções do seed ajustados e explicados; relógio fixo 2026-10-15.

- [ ] **Step 1: Testes**: `installments.json` contém grupos, meses e `accounts.basedOn`; `staticApi.installments.get`; e2e do demo abre Parcelas e Visão geral (cartão) sem chamada à API (padrão do `e2e-demo.test.ts`); `Committed_fixtures_match_generator` verde.
- [ ] **Step 2: Rodar** → falha.
- [ ] **Step 3: Implementar** semente/gerador/adaptador; regenerar com `UPDATE_DEMO_FIXTURES=1`, revisar o diff (somente sintético e esperado: parcelas, `installments.json`, possíveis mudanças em `summaries.json`/`transactions.json` explicadas), rodar sem a variável.
- [ ] **Step 4: Docs**: ADR 0010 curto (derivar em vez de persistir e por que substitui o spec pai; fatura-base por conta; mês de exibição = mês do resumo; "hoje" corta o passado; estorno não cancela; um vencimento por conta; Nubank sem parcelas; limitações), README (seção "Parcelas comprometidas"), resumo de 5 linhas no `docs/away-log.md`.
- [ ] **Step 5: Verificação completa**: `cd backend && dotnet test`, `npm test`, `npm run build`, `npm run build:real`, `node scripts/check-bundles.mjs`, `npm run api:types` sem diff. Commit `feat(demo): parcelas de exemplo; docs da S3(d)`.
- [ ] **Step 6 (controlador)**: revisão final do branch com revisor da capacidade máxima, uma onda de correções, re-revisão escopada, push, PR, CI verde, merge, memória atualizada, workspace apagado, parar (recorrências, juros do Pix parcelado, aplicação/resgate e S4 aguardam o João).

---

## Self-Review

- **Cobertura do spec:** projeção e regras (T1), seleção de linhas/API/contrato (T2), página e cartão (T3), demo/ADR/README (T4). Não-objetivos sem tarefa.
- **Tipos consistentes:** `InstallmentLine`, `InstallmentProjection`, `AccountBase`, `InstallmentMonth`, `InstallmentItem`, `InstallmentGroupView` iguais entre T1/T2; campos HTTP em camelCase iguais no front; `installmentNumber/installmentTotal` em vez de `number/total`.
- **Riscos:** semente do demo muda totais de fatura/resumo (tarefa 4 explica); fuso do "hoje"; dinheiro como string no front.
- **Proporção:** só assinaturas, regras e testes nomeados.
