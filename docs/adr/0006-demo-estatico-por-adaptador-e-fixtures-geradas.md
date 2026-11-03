# ADR 0006: Demo estático por adaptador atrás de `ApiClient` e fixtures geradas pelo backend

- Status: aceita
- Data: 2026-11-03

## Contexto
O site público (Vercel) é o artefato mais visível do projeto. Até a S1b ele mostrava o app antigo: dados de exemplo no `localStorage`, cálculo de dinheiro no navegador, metas e carteira de criptomoedas. A interface do uso real (`src/real/`) já existia, com a costura `ApiClient` (ADR 0004), mas o demo continuava sendo outro programa, com cerca de cinco mil linhas de código legado que não provavam nada sobre o backend e duplicavam regras (resumo, categorias) que o servidor já calcula. O demo precisa funcionar sem backend, sem login e sem nenhuma chamada de rede, e não pode ganhar dado real.

## Decisão
- **Adaptador estático atrás de `ApiClient`.** `src/demo-static/` implementa a mesma interface `ApiClient` lendo JSON (`createStaticApi`). Páginas e lojas não conhecem o adaptador. O demo inicia a interface real (`src/real`) com esse adaptador em `src/demo-static/boot.ts`; o `src/main.ts` escolhe entre `src/real/boot.ts` e o boot do demo pelo modo de build (ADR 0004).
- **Fixtures geradas pelo backend.** Um teste xUnit semeia uma base sintética por `DbContext` com ids fixos (nada de `Guid.NewGuid()`), usa um relógio falso (`FakeTimeProvider`) em 2026-10-15, chama os endpoints reais da API e grava o JSON normalizado (LF, ordem estável, cultura invariante) em `src/demo-static/fixtures/`. Os números do demo são os que o servidor calcula; o navegador só seleciona (filtrar e paginar a lista gerada), nunca calcula dinheiro. Regerar é explícito: `UPDATE_DEMO_FIXTURES=1 dotnet test backend/FinanceDashboard.sln --filter "FullyQualifiedName~Committed_fixtures_match_generator"` (recusado na CI).
- **Deriva detectada na CI.** Sem a variável, o mesmo teste regenera em memória e falha, com o nome do arquivo, se o commit divergir (mesmo padrão do snapshot do OpenAPI). Por isso o gerador precisa ser byte a byte reproduzível no Linux.
- **Somente leitura.** `create`, `update`, `delete`, confirmar e desfazer importação, login e logout rejeitam com o código `demo_read_only`, traduzido pela interface; uma faixa permanente diz "Demonstração com dados fictícios, somente leitura". O visitante tem sessão fixa (`auth.me()`), sem login e sem botão de sair. A tela Importar mostra sempre uma prévia de exemplo e o histórico gerado; o arquivo escolhido nunca é lido nem enviado.
- **`AppConfig` é o único conhecimento do demo dentro de `src/real/`.** Um objeto (`readOnly`, `hideLogout`, relógio `fixedToday`) fornecido por provide/inject pelo boot do demo; o boot real não fornece nada e os padrões preservam o comportamento atual. "Hoje" e o mês atual passam por um ponto único, então o demo fica fixo em 2026-10 (fixtures de 2026-05 a 2026-10; outros meses mostram "sem movimento" gerado pelo backend). Os marcadores do demo (`data-finance-demo-mode`, `finance-demo-fixtures`) ficam só no pacote do demo, e o do modo real só em `src/real/boot.ts`.
- **Isolamento de bundles verificado.** `scripts/check-bundles.mjs` exige o marcador do demo em `dist/` e proíbe nele o marcador do modo real e o cliente HTTP; em `dist-real/` exige o marcador real e proíbe o marcador do demo e as fixtures. `src/sources.test.ts` guarda os imports (`src/real/` não importa fora de si, mesmo pelo alias `@/`; `src/demo-static/` não importa o boot real nem o cliente HTTP).
- **Legado apagado, com tag.** A tag anotada `demo-legado` foi criada e enviada antes da exclusão (páginas, componentes, lojas, serviços, semente, rota e i18n do app antigo). As dependências que ficaram sem uso saíram (`axios`, `lodash`, `vue-toastification`). Metas, cripto e investimentos deixam o site público até a S4/v2.1.

## Consequências
- Uma interface só para demo e uso real: o que o visitante vê é o que o app faz, com números vindos do backend, e o demo exercita o contrato da API a cada regeneração.
- Mudar o contrato da API, um endpoint, a semente ou a data de referência obriga a regenerar e commitar as fixtures no mesmo PR; esquecer vira falha de CI, não demo desatualizado.
- O resumo por conta (filtro de conta no resumo) não existe nas fixtures: nenhuma tela o pede hoje, e o adaptador falha alto se alguém pedir. Se uma tela passar a pedir, é preciso gerar essas fixtures.
- O site público perde metas, carteira de criptomoedas e investimentos até a S4/v2.1; o código está na tag `demo-legado` caso seja preciso consultá-lo.
- O demo fica congelado em 2026-10-15; mudar a data de referência é regenerar as fixtures.
- Mais um teste de backend e mais um conjunto de testes no front (adaptador, fixtures, e2e do demo) para manter.

## Alternativas consideradas
- **B. Manter o demo antigo e só maquiar**: descartada; não prova o backend, mantém o código legado e as regras duplicadas, e o site público continuaria mostrando outro produto.
- **C. Fixtures escritas à mão em TypeScript**: descartada; reintroduz cálculo e duplicação no front e sai de sincronia com o servidor sem aviso.
- **D. Subir a API real na Vercel**: descartada; fora do escopo (S5), com custo e superfície de segurança que um demo público não precisa.
