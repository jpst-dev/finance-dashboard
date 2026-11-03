# Finance Dashboard v2 · S0 Fundação · Plano de implementação

> **Para agentes:** SUB-SKILL OBRIGATÓRIA: use superpowers:subagent-driven-development (recomendado) ou superpowers:executing-plans para executar este plano tarefa a tarefa. Os passos usam checkbox (`- [ ]`).

**Objetivo:** deixar de pé o esqueleto do backend (.NET 10 + PostgreSQL), com login por cookie, documentação da API, Docker Compose na mesma origem do front, CI e proteção contra vazamento de dados, sem ainda ter nenhuma entidade financeira.

**Arquitetura:** monólito modular em `backend/` (projetos Api, Infrastructure e Tests). O Compose sobe Postgres, API e um nginx que serve o front buildado e faz proxy de `/api/` para a API, então tudo fica na mesma origem. Autenticação por cookie de sessão do ASP.NET Core Identity, sem cadastro aberto.

**Tech Stack:** .NET 10, ASP.NET Core minimal APIs, EF Core + Npgsql, ASP.NET Core Identity, OpenAPI nativo + Scalar, xUnit + Testcontainers (PostgreSQL), Docker Compose, nginx, GitHub Actions, gitleaks.

**Spec:** `docs/superpowers/specs/2026-11-02-finance-v2-backend-design.md` (seções "Estrutura do repositório e topologia", "Backend", "Segurança de upload" só no que toca a S0, "Testes e entrega" e "Fatias de entrega · S0").

## Global Constraints
- Backend em `backend/` (nunca `api/`, a Vercel trataria como funções serverless).
- .NET 10; PostgreSQL; sem Hangfire; a imagem Docker da API tem `tzdata` (Debian/Ubuntu, **não** Alpine).
- Cookie de sessão `httpOnly`, mesma origem, proteção antiforgery; **sem cadastro aberto**; usuário inicial por variáveis de ambiente.
- Logs estruturados; **nunca** logar corpo de requisição, descrição, valores ou nomes; `EnableSensitiveDataLogging` desligado.
- Postgres publicado só em `127.0.0.1`; segredos em `.env` ignorado; o repositório traz `.env.example`; nenhuma senha ou chave em `docker-compose.yml`.
- `.gitignore` ignora `finance-samples/`, `private/`, `*.ofx`, `*.qfx`, `*.xlsx`, com exceção explícita para `backend/tests/fixtures/`; `.gitattributes` com `*.ofx -text`.
- Custo zero. Nenhum dado financeiro real em commit, fixture, log ou documento.
- Todas as rotas da API ficam sob o prefixo `/api`.

## Review Focus
Entradas e condições que o spec implica mas as tarefas não cobrem sozinhas, da mais provável para a menos:
1. E-mail com maiúsculas ou espaços nas pontas no login deve funcionar como o e-mail normalizado (testado na Tarefa 4).
2. Tentativas de login em excesso devem devolver 429, e e-mail inexistente deve devolver a mesma resposta 401 que senha errada, sem revelar qual usuário existe (Tarefa 4).
3. `SEED_USER_EMAIL` definido sem `SEED_USER_PASSWORD` (ou senha com menos de 12 caracteres) deve impedir a API de subir, com erro que não imprime a senha (Tarefa 5).
4. Banco fora do ar na subida: `/api/health` continua 200 e `/api/health/ready` devolve 503, sem derrubar o processo (Tarefa 3).
5. Cookie copiado antes do logout não pode continuar valendo depois do logout (Tarefa 4).

---

### Task 1: Higiene do repositório contra vazamento

**Files:**
- Modify: `.gitignore`
- Create: `.gitattributes`, `.env.example`, `.gitleaks.toml`, `.githooks/pre-commit`

**Interfaces:**
- Produces: padrões de ignore e as variáveis de ambiente do `.env.example` que as tarefas 5 e 7 usam: `POSTGRES_PASSWORD`, `SEED_USER_EMAIL`, `SEED_USER_PASSWORD`.

- [ ] **Step 1: Verificar que os arquivos de dados hoje não estão protegidos**

```bash
mkdir -p backend/tests/fixtures && touch x.ofx y.xlsx backend/tests/fixtures/ok.ofx
git check-ignore -v x.ofx y.xlsx backend/tests/fixtures/ok.ofx
```
Esperado: nenhuma das três linhas é impressa (nada ignorado).

- [ ] **Step 2: Acrescentar a `.gitignore`** `*.ofx`, `*.qfx`, `*.xlsx` e, depois, `!backend/tests/fixtures/**`. Criar `.gitattributes` com `* text=auto`, `*.ofx -text`, `*.qfx -text`, `*.xlsx binary` e `backend/tests/fixtures/** -text`.

- [ ] **Step 3: Verificar**

```bash
git check-ignore -v x.ofx y.xlsx; git check-ignore -v backend/tests/fixtures/ok.ofx
```
Esperado: `x.ofx` e `y.xlsx` aparecem como ignorados; `ok.ofx` não aparece. Depois `rm x.ofx y.xlsx backend/tests/fixtures/ok.ofx`.

- [ ] **Step 4: Criar `.env.example`** com `POSTGRES_PASSWORD=change-me`, `SEED_USER_EMAIL=you@example.com`, `SEED_USER_PASSWORD=change-me-with-12-chars-or-more` e um comentário dizendo para copiar para `.env` (já ignorado por `.env*` no `.gitignore`; garantir que `.env.example` **não** seja ignorado com `!.env.example`).

- [ ] **Step 5: Criar `.gitleaks.toml`** estendendo as regras padrão (`[extend] useDefault = true`) com allowlist só para `.env.example`. Criar `.githooks/pre-commit` (sh) que roda `gitleaks protect --staged` se o binário existir e, se não existir, imprime um aviso e sai com 0. Registrar com `git config core.hooksPath .githooks`.

- [ ] **Step 6: Verificar o hook**: `echo "AWS_SECRET_ACCESS_KEY=AKIAIOSFODNN7EXAMPLEwJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKEY" > t.txt && git add t.txt && git commit -m x`. Esperado: o commit é bloqueado se o gitleaks estiver instalado, ou o aviso aparece se não estiver. Depois `git reset -q t.txt && rm t.txt`.

- [ ] **Step 7: Commit**

```bash
git add .gitignore .gitattributes .env.example .gitleaks.toml .githooks
git commit -m "chore: protege dados reais e segredos (gitignore, gitattributes, gitleaks)"
```

---

### Task 2: Solução .NET e health check de vida

**Files:**
- Create: `backend/FinanceDashboard.sln`, `backend/global.json`, `backend/Directory.Build.props`, `backend/.editorconfig`
- Create: `backend/src/FinanceDashboard.Api/` (webapi vazio, `Program.cs`), `backend/src/FinanceDashboard.Infrastructure/` (classlib), `backend/tests/FinanceDashboard.Tests/` (xUnit)
- Test: `backend/tests/FinanceDashboard.Tests/HealthTests.cs`

**Interfaces:**
- Produces: `public partial class Program` (acessível por `WebApplicationFactory<Program>`); rota `GET /api/health`; `Directory.Build.props` com `net10.0`, `Nullable=enable`, `TreatWarningsAsErrors=true`.

- [ ] **Step 1: Criar a solução e os três projetos** com `dotnet new` (`sln`, `webapi` minimal API sem controllers, `classlib`, `xunit`), referências Api → Infrastructure e Tests → Api, e `global.json` fixando o SDK 10 com `rollForward: latestFeature`. Pacote de teste: `Microsoft.AspNetCore.Mvc.Testing`.

- [ ] **Step 2: Escrever o teste que falha**

```csharp
public class HealthTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task Health_returns_200_with_status_healthy()
    {
        var res = await factory.CreateClient().GetAsync("/api/health");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Contains("Healthy", await res.Content.ReadAsStringAsync());
    }
}
```

- [ ] **Step 3: Rodar e ver falhar**: `dotnet test backend/FinanceDashboard.sln --filter Health_returns_200` → FAIL (404).

- [ ] **Step 4: Implementar** `app.MapHealthChecks("/api/health", new() { Predicate = _ => false })` em `Program.cs` (liveness sem dependências) e declarar `public partial class Program;`. Remover o endpoint de exemplo do template.

- [ ] **Step 5: Rodar e ver passar**: mesmo comando → PASS. Rodar também `dotnet build backend/FinanceDashboard.sln` → sem warnings.

- [ ] **Step 6: Commit** (`git add backend && git commit -m "feat(backend): solução .NET 10 com health check de vida"`).

---

### Task 3: PostgreSQL, EF Core, Identity e readiness

**Files:**
- Create: `backend/src/FinanceDashboard.Infrastructure/AppUser.cs`, `AppDbContext.cs`, `InfrastructureServiceCollectionExtensions.cs`, `Migrations/` (gerada)
- Create: `backend/src/FinanceDashboard.Api/Health/TimeZoneHealthCheck.cs`
- Modify: `backend/src/FinanceDashboard.Api/Program.cs`
- Test: `backend/tests/FinanceDashboard.Tests/Infrastructure/PostgresFixture.cs`, `ApiFactory.cs`, `ReadinessTests.cs`, `MigrationTests.cs`

**Interfaces:**
- Produces:
  - `public class AppUser : IdentityUser`
  - `public class AppDbContext(DbContextOptions<AppDbContext> o) : IdentityDbContext<AppUser>(o)`
  - `public static IServiceCollection AddInfrastructure(this IServiceCollection s, IConfiguration c)`, lendo `ConnectionStrings:Default`
  - configuração `Database:MigrateOnStartup` (bool); quando verdadeira, a API aplica as migrations ao subir
  - `public sealed class TimeZoneHealthCheck : IHealthCheck`, Healthy quando `TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo")` resolve
  - rota `GET /api/health/ready` com os checks `database` e `timezone`
  - testes: `public sealed class PostgresFixture : IAsyncLifetime` (um container Postgres por execução, método `Task<string> CreateDatabaseAsync()` que cria um banco `test_{guid}` e devolve a connection string) e `public sealed class ApiFactory : WebApplicationFactory<Program>` com construtor `ApiFactory(string connectionString, Dictionary<string,string?>? extraConfig = null)`

- [ ] **Step 1: Escrever os testes que falham**

```csharp
[Fact] public async Task Ready_returns_200_when_database_is_up_and_timezone_resolves()
// GET /api/health/ready == 200 e o corpo contém "database" e "timezone", ambos "Healthy"

[Fact] public async Task Ready_returns_503_but_health_stays_200_when_database_is_down()
// connection string apontando para localhost:1 -> /api/health == 200 e /api/health/ready == 503

[Fact] public async Task Migrations_create_identity_tables_on_an_empty_database()
// com Database:MigrateOnStartup=true, uma consulta a "AspNetUsers" via AppDbContext retorna 0 linhas sem lançar exceção
```
Dependência de pacotes de teste: `Testcontainers.PostgreSql`.

- [ ] **Step 2: Rodar e ver falhar**: `dotnet test backend/FinanceDashboard.sln --filter "Ready_|Migrations_"` → FAIL (tipos inexistentes).

- [ ] **Step 3: Implementar** os tipos acima com Npgsql como provider; registrar `AddHealthChecks().AddDbContextCheck<AppDbContext>("database").AddCheck<TimeZoneHealthCheck>("timezone")` com `/api/health/ready` filtrando esses dois e devolvendo 503 quando algum falha; **não** chamar `EnableSensitiveDataLogging`. Gerar a migration inicial com `dotnet ef migrations add Initial` (projeto Infrastructure, startup Api) e aplicá-la em `Program.cs` quando `Database:MigrateOnStartup` for verdadeiro.

- [ ] **Step 4: Rodar e ver passar** o mesmo filtro → PASS (requer Docker em execução). Rodar a suíte inteira: `dotnet test backend/FinanceDashboard.sln` → tudo verde.

- [ ] **Step 5: Commit** (`feat(backend): PostgreSQL, EF Core, Identity e readiness`).

---

### Task 4: Login por cookie, logout, antiforgery e limite de tentativas

**Files:**
- Create: `backend/src/FinanceDashboard.Api/Auth/AuthEndpoints.cs`, `Auth/LoginRequest.cs`
- Modify: `backend/src/FinanceDashboard.Api/Program.cs`
- Test: `backend/tests/FinanceDashboard.Tests/Auth/AuthTests.cs`, `Auth/AuthTestHelpers.cs`

**Interfaces:**
- Consumes: `ApiFactory`, `PostgresFixture` (Tarefa 3).
- Produces:
  - `public static IEndpointRouteBuilder MapAuth(this IEndpointRouteBuilder app)` registrando, todos sob `/api/auth`:
    - `GET /csrf`: grava o cookie de antiforgery e devolve `{ "token": string }`
    - `POST /login` com `public sealed record LoginRequest(string Email, string Password)`: 200 e cookie de sessão, ou 401 genérico
    - `POST /logout`: 204
    - `GET /me`: 200 `{ "email": string }` ou 401
  - cabeçalho antiforgery `X-XSRF-TOKEN`; política de rate limit `"login"` (5 por minuto por IP)
  - helper de teste `static Task<(HttpClient client, string cookie)> LoginAsync(ApiFactory f, string email, string password)` e `static Task<AppUser> CreateUserAsync(ApiFactory f, string email, string password)`

- [ ] **Step 1: Escrever os testes que falham**, todos contra Postgres real:

```csharp
Login_with_valid_credentials_sets_httponly_cookie_and_me_returns_email
  // 200; Set-Cookie contém "httponly" e "samesite=lax"; GET /api/auth/me com o cookie == 200 e email == o do usuário
Login_normalizes_email_case_and_surrounding_spaces
  // login com "  USER@Example.COM " para usuário "user@example.com" == 200
Login_returns_same_401_for_unknown_email_and_wrong_password
  // status e corpo idênticos nos dois casos
Login_is_rate_limited_after_five_attempts_in_a_minute
  // 5 tentativas com senha errada -> 401; a 6ª == 429
Post_without_antiforgery_token_is_rejected
  // POST /api/auth/login sem X-XSRF-TOKEN == 400
Me_without_cookie_returns_401
Old_cookie_stops_working_after_logout
  // guarda o cookie, faz logout, GET /me com o cookie guardado == 401
Failed_login_does_not_log_email_or_password
  // captura os logs (ILoggerProvider de teste) e afirma que nenhuma entrada contém o e-mail nem a senha enviados
```

- [ ] **Step 2: Rodar e ver falhar**: `dotnet test backend/FinanceDashboard.sln --filter Auth` → FAIL.

- [ ] **Step 3: Implementar.** `AddIdentityCore<AppUser>` com `AddSignInManager` e `AddEntityFrameworkStores<AppDbContext>`, senha mínima de 12 caracteres, esquema de cookie `HttpOnly`, `SameSite=Lax`, `SecurePolicy=SameAsRequest` (o navegador aceita `Secure` em `http://localhost`, mas o `TestServer` roda em http), expiração deslizante de 7 dias, e `SecurityStampValidatorOptions.ValidationInterval = TimeSpan.Zero`. O logout chama `SignOutAsync` e `UserManager.UpdateSecurityStampAsync` (isso invalida cookies copiados; como o uso é de um único usuário, derrubar as demais sessões é aceitável). Login e logout exigem antiforgery; `AddRateLimiter` com a política `"login"`. A resposta 401 é idêntica para e-mail desconhecido e senha errada, executando o hash de senha nos dois caminhos. Não registrar nenhum campo do `LoginRequest` em log.

- [ ] **Step 4: Rodar e ver passar**: mesmo filtro → PASS; depois a suíte inteira.

- [ ] **Step 5: Commit** (`feat(backend): login por cookie com antiforgery e rate limit`).

---

### Task 5: Usuário inicial por variáveis de ambiente

**Files:**
- Create: `backend/src/FinanceDashboard.Api/Auth/SeedUser.cs`
- Modify: `backend/src/FinanceDashboard.Api/Program.cs`
- Test: `backend/tests/FinanceDashboard.Tests/Auth/SeedUserTests.cs`

**Interfaces:**
- Consumes: `ApiFactory` com `extraConfig`, `LoginAsync` (Tarefas 3 e 4).
- Produces: `public static class SeedUser { public static Task EnsureAsync(IServiceProvider sp, IConfiguration config, CancellationToken ct = default); }` lendo `SEED_USER_EMAIL` e `SEED_USER_PASSWORD`; chamada em `Program.cs` depois das migrations.

- [ ] **Step 1: Escrever os testes que falham**

```csharp
Seed_creates_the_user_once_and_login_works          // sobe a API duas vezes sobre o mesmo banco; existe exatamente 1 usuário; login == 200
Seed_with_email_but_without_password_prevents_startup // lança InvalidOperationException; a mensagem não contém o e-mail
Seed_with_password_shorter_than_12_chars_prevents_startup // lança; a mensagem não contém a senha
Seed_does_nothing_when_neither_variable_is_set      // sobe normalmente, 0 usuários
```

- [ ] **Step 2: Rodar e ver falhar**: `dotnet test backend/FinanceDashboard.sln --filter Seed` → FAIL.

- [ ] **Step 3: Implementar** `EnsureAsync`: idempotente (não recria nem altera a senha de usuário já existente) e fail-fast, com erros que descrevem o problema sem repetir o valor das variáveis.

- [ ] **Step 4: Rodar e ver passar**, depois a suíte inteira.

- [ ] **Step 5: Commit** (`feat(backend): usuário inicial via variáveis de ambiente`).

---

### Task 6: OpenAPI e documentação da API

**Files:**
- Modify: `backend/src/FinanceDashboard.Api/Program.cs`
- Test: `backend/tests/FinanceDashboard.Tests/OpenApiTests.cs`

**Interfaces:**
- Produces: documento OpenAPI em `GET /api/openapi/v1.json` e UI Scalar em `GET /api/docs`.

- [ ] **Step 1: Escrever o teste que falha**

```csharp
[Fact] public async Task OpenApi_document_lists_health_and_auth_routes()
// GET /api/openapi/v1.json == 200, JSON com paths contendo "/api/health", "/api/auth/login" e "/api/auth/me"
[Fact] public async Task Docs_page_is_served() // GET /api/docs == 200
```

- [ ] **Step 2: Rodar e ver falhar** (`--filter OpenApi|Docs_page`) → FAIL.

- [ ] **Step 3: Implementar** com `AddOpenApi()`, `MapOpenApi("/api/openapi/{documentName}.json")` e o pacote `Scalar.AspNetCore` em `/api/docs`. Dar nome e resumo aos endpoints existentes (`WithName`, `WithSummary`).

- [ ] **Step 4: Rodar e ver passar**, depois a suíte inteira.

- [ ] **Step 5: Commit** (`feat(backend): OpenAPI e Scalar`).

---

### Task 7: Docker Compose na mesma origem

**Files:**
- Create: `backend/Dockerfile`, `deploy/web.Dockerfile`, `deploy/nginx.conf`, `docker-compose.yml`, `.dockerignore`
- Modify: `vite.config.ts` (proxy de desenvolvimento)

**Interfaces:**
- Consumes: variáveis do `.env.example` (Tarefa 1); `/api/health/ready` (Tarefa 3).
- Produces: serviços `db` (postgres:17, volume nomeado `pgdata`, porta `127.0.0.1:5432`), `api` (sem porta publicada, `Database__MigrateOnStartup=true`, `ConnectionStrings__Default` montada a partir de `POSTGRES_PASSWORD`) e `web` (nginx em `127.0.0.1:8080`, serve o `dist` do front e faz proxy de `/api/` para `api:8080`, com fallback `try_files $uri /index.html`). Em desenvolvimento, `vite.config.ts` aponta `/api` para `http://localhost:5080`.

- [ ] **Step 1: Escrever os Dockerfiles.** A API em multi-stage com `mcr.microsoft.com/dotnet/sdk:10.0` e `aspnet:10.0` (Debian, com `tzdata`), executando como usuário não root. O web em multi-stage: `node:24` roda `npm ci && npm run build` e copia `dist/` para `nginx:alpine` com `deploy/nginx.conf`. Healthchecks: `pg_isready` no `db`; `api` só sobe depois de `db` saudável.

- [ ] **Step 2: Escrever `docker-compose.yml`** sem nenhum valor secreto literal (tudo vem do `.env`). Copiar `.env.example` para `.env` e preencher com valores locais.

- [ ] **Step 3: Subir e verificar**

```bash
docker compose up -d --build
curl -s http://127.0.0.1:8080/api/health/ready
curl -sI http://127.0.0.1:8080/ | head -1
curl -sI http://127.0.0.1:8080/portfolio | head -1
```
Esperado: o primeiro devolve JSON com `database` e `timezone` Healthy (o `timezone` Healthy prova que o `tzdata` existe na imagem); o segundo e o terceiro devolvem `HTTP/1.1 200` (o terceiro é o fallback da SPA).

- [ ] **Step 4: Verificar o login ponta a ponta pela mesma origem**: obter o token em `GET /api/auth/csrf` guardando o cookie (`curl -c jar`), fazer `POST /api/auth/login` com `X-XSRF-TOKEN` e o usuário do `.env`, e depois `GET /api/auth/me -b jar`. Esperado: 200 e o e-mail do usuário.

- [ ] **Step 5: Verificar que o Postgres só escuta em localhost**: `docker compose port db 5432` → `127.0.0.1:5432`.

- [ ] **Step 6: Proxy do Vite.** Adicionar `server.proxy` em `vite.config.ts`; verificar `npm run build` e `npm test` (devem continuar passando) e, com a API local no ar, `npm run dev` + `curl http://localhost:5173/api/health` → 200.

- [ ] **Step 7: Commit** (`feat: Docker Compose com Postgres, API e front na mesma origem`).

---

### Task 8: Backup e restauração do Postgres

**Files:**
- Create: `scripts/backup-db.ps1`, `scripts/restore-db.ps1`, `backups/.gitignore` (conteúdo `*` e `!.gitignore`)

**Interfaces:**
- Consumes: serviço `db` do Compose (Tarefa 7).
- Produces: `scripts/backup-db.ps1` gera `backups/finance-<yyyyMMdd-HHmmss>.sql.gz`; `scripts/restore-db.ps1 -File <caminho>` restaura em um banco novo `finance_restore`.

- [ ] **Step 1: Implementar os dois scripts** com `docker compose exec -T db pg_dump` e `psql`, sem imprimir senhas.

- [ ] **Step 2: Verificar o ciclo**: com o usuário inicial já criado na Tarefa 7, rodar o backup (o arquivo existe e tem mais de 0 bytes), rodar o restore e conferir `docker compose exec -T db psql -U postgres -d finance_restore -tAc 'select count(*) from "AspNetUsers"'` → `1`. Depois apagar o banco `finance_restore`.

- [ ] **Step 3: Commit** (`feat: scripts de backup e restauração do Postgres`).

---

### Task 9: CI no GitHub Actions

**Files:**
- Create: `.github/workflows/ci.yml`

**Interfaces:**
- Consumes: `npm test` e `npm run build` (front); `dotnet test` (backend, usa Docker do runner para o Testcontainers).

- [ ] **Step 1: Escrever o workflow** com três jobs em `ubuntu-latest`: `backend` (setup-dotnet 10, `dotnet build` e `dotnet test backend/FinanceDashboard.sln`), `frontend` (setup-node 24, `npm ci`, `npm test`, `npm run build`) e `secrets` (checkout com `fetch-depth: 0` e `gitleaks/gitleaks-action`). Disparo em `push` e `pull_request`.

- [ ] **Step 2: Validar localmente** que o YAML é válido: `python -I -c "import yaml,sys; yaml.safe_load(open('.github/workflows/ci.yml'))"` (instalar PyYAML antes se faltar) → sem erro.

- [ ] **Step 3: Commit.** O push e a conferência da execução (`gh run watch`) **só acontecem com a confirmação do João**, porque envia o código ao GitHub. Esperado após o push: os três jobs verdes.

---

### Task 10: README e decisões de arquitetura (ADRs)

**Files:**
- Modify: `README.md` (nova seção "v2 · Backend")
- Create: `docs/adr/0001-dotnet-postgres-monolito-modular.md`, `docs/adr/0002-cookie-de-sessao-na-mesma-origem.md`

- [ ] **Step 1: Escrever a seção do README**: o que é a v2, diagrama simples (texto/Mermaid) navegador → nginx → API → Postgres, e os comandos `cp .env.example .env`, `docker compose up -d --build`, a URL `http://127.0.0.1:8080`, `/api/docs`, `dotnet test backend/FinanceDashboard.sln` e os scripts de backup. Dizer claramente que o site público na Vercel segue como demo.

- [ ] **Step 2: Escrever os dois ADRs** (contexto, decisão, consequências), curtos, a partir do spec: por que .NET + Postgres em monólito modular, e por que cookie de sessão na mesma origem em vez de JWT.

- [ ] **Step 3: Verificar** que os links relativos do README e a lista de comandos funcionam seguindo o texto do zero (`docker compose down -v && docker compose up -d --build`, depois `curl http://127.0.0.1:8080/api/health/ready` → Healthy).

- [ ] **Step 4: Commit** (`docs: README da v2 e ADRs da fundação`).

---

## Auto-revisão
- **Cobertura do spec (S0):** solução .NET, Compose com Postgres/API/proxy, health check, CI, autenticação por cookie, `.gitignore`/`.gitattributes`/gitleaks, `.env.example`, backup, `tzdata`, Scalar/OpenAPI, rate limit, logs sem dados, usuário inicial, README/ADRs → Tarefas 1 a 10. O cliente OpenAPI gerado e o teste de isolamento por `UserId` (filtro global) ficam para a S1, quando existir a primeira entidade do usuário; o projeto Domain também entra na S1 (YAGNI).
- **Consistência de tipos:** `ApiFactory`, `PostgresFixture`, `LoginAsync`/`CreateUserAsync`, `AppUser`, `AppDbContext`, `AddInfrastructure` e as rotas `/api/health`, `/api/health/ready`, `/api/auth/*`, `/api/openapi/v1.json`, `/api/docs` têm os mesmos nomes em todas as tarefas.
- **Review Focus:** itens 1 e 2 e 5 estão na Tarefa 4, o 3 na Tarefa 5 e o 4 na Tarefa 3.
- **Pendência de execução:** o Docker Desktop precisa estar rodando para a Tarefa 3 em diante.
