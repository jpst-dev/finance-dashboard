# ADR 0001: .NET 10 + PostgreSQL como monólito modular

- Status: aceita
- Data: 2026-11-02

## Contexto
O front-end da v1 guarda tudo no `localStorage`. A v2 precisa de um backend para importar extratos de forma idempotente, conciliar lançamentos, calcular saldos e patrimônio no servidor e guardar snapshots diários de preço. Dois objetivos pesam igual: uso real (praticamente um único usuário) e portfólio, demonstrando backend com modelagem de dados, testes de integração, CI e Docker. O desenvolvedor trabalha com .NET/C# e REST, tem um só par de mãos e o custo mensal precisa ser zero.

## Decisão
- **ASP.NET Core (.NET 10) + EF Core + PostgreSQL**, com migrations versionadas.
- **Monólito modular**: um único processo. Os módulos (Identidade, Contas, Lançamentos, Importação, Conciliação, Categorização, Investimentos, Dados de mercado) são **pastas e namespaces**, não projetos separados. A solução em `backend/` tem poucos projetos (API, Infraestrutura e Testes; o Domínio entra na S1, quando houver a primeira entidade do usuário).
- Jobs em segundo plano com `BackgroundService`, sem Hangfire.
- O backend vive em `backend/`, nunca em `api/`: a Vercel trata `api/` como funções serverless.

## Consequências
- Um deploy, um banco, uma transação: conciliação e importação ficam consistentes sem coordenação distribuída.
- Fronteiras entre módulos dependem de disciplina (namespaces e revisão), não do compilador. Se um módulo crescer demais, dá para extrair um projeto depois.
- Postgres dá `numeric(18,2)` para dinheiro e tipos `date`, o que evita erro de ponto flutuante e o bug de fuso.
- Os testes de integração usam Postgres real (Testcontainers), então exigem Docker.
- O front Vue continua na raiz do repositório; a Vercel segue servindo só o demo.

## Alternativas consideradas
- **Node/TypeScript** (mesma linguagem do front): descartada. Compartilhar linguagem rende pouco aqui e não mostra a habilidade de backend que o projeto quer demonstrar, além de o autor ter mais experiência com .NET.
- **Microsserviços**: descartados. Custo operacional sem benefício para um usuário.
- **Um projeto por módulo**: descartado por ora (YAGNI); pastas e namespaces bastam.
- **Só trocar o `localStorage` por CRUD**: herdaria float, IDs com `Date.now()` e histórico falso, e não resolve a digitação manual.
