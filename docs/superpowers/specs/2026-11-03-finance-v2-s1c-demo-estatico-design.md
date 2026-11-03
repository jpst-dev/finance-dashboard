# Finance v2 — S1c: demo estático com a nova interface (design)

Pai: `2026-11-02-finance-v2-backend-design.md` (binding; seção Front-end) e `2026-11-02-finance-v2-s1b-interface-design.md` (que prometeu esta fatia). ADR 0004 (modo de build e camada de API). Feita em **modo ausente** (`docs/away-mode.md`): o pai já decide os pontos centrais (adaptador estático, fixtures geradas pelo backend, somente leitura, legado apagado), então não houve conselheiros; as escolhas abaixo estão no `docs/away-log.md`.

## Intenção
O site público (Vercel) é o artefato mais visível do portfólio. Hoje ele mostra o app antigo (dados no `localStorage`, cálculo no navegador, metas e cripto). A S1c troca o demo pela **mesma interface do uso real**, alimentada por um adaptador estático atrás da interface `ApiClient`, com números calculados **pelo backend** e congelados em fixtures. O visitante vê Visão geral, Lançamentos, Pendentes, Contas e a tela Importar (com exemplo fixo) sem login e sem backend; o código legado é apagado.

## Objetivos
- Build `demo` (padrão; Vercel) inicia a interface real (`src/real`) com um `ApiClient` estático; build `real` segue idêntico.
- **Fixtures geradas pelo backend** a partir de uma semente sintética com **data de referência fixa**, cobrindo tudo que a interface consulta (contas e saldos, categorias, lançamentos, resumo por mês, histórico de importação, exemplo de prévia). Regerar é um comando explícito e a CI **falha se o commit divergir** do gerador (mesmo padrão do snapshot OpenAPI).
- **Somente leitura**: mutações rejeitam com erro tratado (`demo_read_only`), com mensagem traduzida; faixa visível "Demonstração com dados fictícios, somente leitura".
- Sem login no demo (sessão fixa de visitante); sem botão de sair.
- **Relógio do demo**: "hoje"/mês atual do demo = data de referência das fixtures (o seletor de mês e os padrões de filtro não dependem do dia real do visitante).
- Tela Importar no demo: escolher arquivo mostra uma **prévia de exemplo fixa** (o arquivo do visitante nunca é lido nem enviado); confirmar/desfazer → somente leitura.
- **Legado apagado**: páginas, componentes, lojas, serviços, semente e rota do app antigo; dependências que ficarem sem uso são removidas.
- Isolamento de bundles mantido: `dist/` (demo) não contém o cliente HTTP nem o marcador do modo real; `dist-real/` não contém fixtures nem o marcador do demo.

## Não-objetivos
Metas, carteira cripto e investimentos (voltam, se voltarem, na S4/v2.1); editar/criar no demo; PWA; tradução de dados das fixtures (nomes são os mesmos nos dois idiomas); qualquer dado real nas fixtures.

## Anti-objetivos
- Aritmética de dinheiro no navegador ou dinheiro como `number` (continua valendo; filtrar/paginar fixtures é seleção de dados, não cálculo).
- Dado real, nome real ou valor derivado de extrato real nas fixtures, nos testes ou no gerador.
- O demo chamar a rede (nenhum `fetch`/XHR em nenhuma tela do demo) ou ler o arquivo escolhido na tela Importar.
- Fixtures não determinísticas (ids, ordem, fim de linha, cultura) — a CI no Linux precisa reproduzir byte a byte.
- Regressão do modo real (Compose) ou do isolamento de bundles.
- Esconder valores deixar de funcionar no demo.

## Restrições
- Valem todas as do pai, S1a, S1b e S2 (dinheiro como string; tipos da API gerados; antiforgery só no HTTP; i18n pt-BR/en-US com chaves idênticas; sem dado real).
- `ApiClient` é a única costura: páginas e lojas não conhecem o adaptador. O adaptador estático vive fora de `src/real/` (por exemplo `src/demo-static/`), e `src/real/` só ganha os pontos de extensão mínimos (relógio, modo somente leitura/faixa, ocultar sair).
- Resumo por mês: o adaptador **só devolve o que foi gerado** (um mês fora das fixtures devolve o resumo vazio gerado para "mês sem movimento", também gerado pelo backend, nunca calculado no navegador). Resumo filtrado por conta, se a interface chegar a pedir, deve existir nas fixtures; se nenhuma tela pede, não é suportado e o adaptador falha alto em teste.
- Gerador no backend como teste xUnit com `UPDATE_DEMO_FIXTURES=1` para gravar e comparação nos demais casos; semeia por `DbContext` com **ids fixos** (nada de `Guid.NewGuid()`), chama os endpoints reais da API e grava JSON normalizado (LF, ordem estável, cultura invariante). Usuário de teste e e-mail sintéticos.
- Versões fixas; nenhuma dependência nova sem motivo.

## Abordagens consideradas
A. **Adaptador estático atrás de `ApiClient` + fixtures geradas pelo backend** (escolhida; é o que o pai manda): uma interface só, números do servidor, drift detectado na CI. B. Manter o demo antigo e só maquiar: não prova o backend e mantém 5 mil linhas de código legado. C. Fixtures escritas à mão em TypeScript: reintroduz cálculo/duplicação e sai de sincronia com o servidor. D. Subir a API real na Vercel: fora do escopo (S5) e com custo/segurança.
**Decisão:** A.

## Decisões minhas (modo ausente) — o João revisa ao voltar
1. **Metas, cripto e investimentos somem do site público** até a S4/v2.1 (o pai manda apagar o legado e a S1b já anunciou). Desfazer: o legado continua no histórico do git (tag `demo-legado` criada antes da exclusão para facilitar).
2. Data de referência do demo: **2026-10-15** (mês de referência 2026-10), fixtures cobrindo 2026-05 a 2026-10; outros meses mostram "sem movimento".
3. Demo sem login (visitante fixo); sem botão de sair; faixa permanente de demonstração.
4. Importar no demo mostra prévia de exemplo e histórico de exemplo; o arquivo do visitante não é lido.
5. Filtrar e paginar lançamentos no adaptador (TypeScript) sobre a lista gerada é aceito (seleção, não cálculo).

## Fica para o construtor
Nomes e pastas do adaptador e das fixtures, formato exato do JSON, mecanismo do relógio e da faixa, como o gerador semeia e normaliza, remoção de dependências sem uso (`axios`, `lodash`, `vue-toastification`, etc., conferindo o que o código real ainda importa), migração das chaves de i18n legado ainda usadas pelo modo real, atualização do `check-bundles.mjs`, README e ADR (0006), testes do adaptador e e2e do demo.

## Decisões complementares (após o builder check; do Claude, em modo ausente)
1. **Fixtures de importação determinísticas:** o gerador semeia lotes e a prévia **por `DbContext` com ids fixos** e usa o `TimeProvider` falso do `ApiFactory` para `CreatedAt`/`ExpiresAt`; a prévia é o `ImportPreview` devolvido pela API real para esse lote. Sem mudar código de produção; se uma saída da API ainda tiver id aleatório, o gerador a normaliza por pós-processamento determinístico (mapa id→id fixo) e o teste de deriva cobre isso.
2. **Importar no demo:** `imports.preview(accountId, file)` ignora conta e arquivo e devolve sempre a mesma prévia de exemplo (conta corrente importável do seed); `imports.list()` devolve o histórico gerado (um lote confirmado e um em prévia); `imports.get(id)` devolve a prévia/resumo gerados para aquele lote, ou erro 404 tratado; `confirm`/`undo` → `demo_read_only`.
3. **Sessão:** `auth.me()` sempre devolve o visitante fixo; `login`/`logout` rejeitam com `demo_read_only` (inalcançáveis na UI). A rota `/login` continua no roteador compartilhado mas nunca é alcançada (guard com sessão sempre autenticada).
4. **Conhecimento do demo dentro de `src/real/`:** um objeto de configuração (`readOnly`, `hideLogout`, relógio) fornecido por provide/inject pelo boot do demo; o boot real não fornece nada (padrões = comportamento atual). Marcador do demo (`data-finance-demo-mode`) e das fixtures ficam só no pacote do demo; o marcador do modo real continua só em `src/real/boot.ts`. **Boots separados:** `src/real/boot.ts` e `src/demo-static/boot.ts`; `src/main.ts` escolhe.
5. **Relógio:** um único ponto (provedor de "hoje") usado por `stores/summary.ts` (mês inicial e `canGoNext`) e por `money/today.ts`; o demo fixa 2026-10-15.
6. **i18n legado:** conferir por busca se `src/real` usa alguma chave fora de `real.*`; se não, remover a mescla dos locales legados em `real/boot.ts` e `real/i18n/index.ts` (as chaves usadas migram para os JSON `real`); só então apagar `src/i18n`.
7. **`check-bundles.mjs`** passa a verificar: `dist/` contém o marcador do demo e **não** contém o marcador do modo real nem o cliente HTTP; `dist-real/` contém o marcador real e **não** contém o marcador do demo/fixtures.
8. Antes de apagar o legado, criar a tag `demo-legado` no commit atual da `main` e enviá-la (`git push origin demo-legado`).
