# Finance v2 — S1b: interface do modo real (design)

Pai: `2026-11-02-finance-v2-backend-design.md` (binding) e `2026-11-02-finance-v2-s1a-nucleo-nubank-design.md` (a API que esta fatia consome; já na `main`). Detalha a segunda metade da S1 e uma pequena preparação no backend.

## Intenção
O João quer um lugar onde adiciona gastos com facilidade e acompanha. A S1a entregou o servidor; a S1b entrega a **interface do uso real** (local, Docker Compose): entrar, ver o mês, ver contas e saldos, listar/lançar/editar/classificar lançamentos e importar o extrato do Nubank com prévia. A interface é desenhada uma vez, atrás de uma interface `api` com duas implementações futuras (HTTP agora; demo estático na S1c), para que o demo do portfólio mostre a mesma UI do uso real.

## Objetivos
- Tela de login e sessão por cookie; navegação do modo real: Visão geral, Lançamentos, Contas, Importar.
- **Visão geral** (opção B escolhida): faixa de contas com saldo (cartão aparece como dívida) → aviso de pendentes com atalho → Receitas / Despesas / Resultado do mês (seletor de mês) → gráfico dos últimos 6 meses e despesas por categoria → últimos lançamentos.
- **Contas**: listar com saldo, criar, renomear/arquivar, registrar checkpoint manual de saldo.
- **Lançamentos**: lista com filtros (conta, mês, categoria, estado) e paginação; criar manual; editar; apagar; **reclassificar** estado e categoria; fila de "Pendentes".
- **Importar**: escolher conta e arquivo → **prévia em abas** (Novos · Pendentes · Já existentes · Avisos; opção B escolhida) → classificar pendentes ali mesmo → confirmar → histórico de lotes com **desfazer** (aviso do que será removido).
- **Esconder valores**: botão global que troca todo valor monetário por "••••" (inclui gráficos, tooltips, rótulos de acessibilidade e título da página), lembrado por navegador.
- Português (pt-BR) como idioma principal e inglês (en-US) mantendo o seletor atual.

## Não-objetivos
Demo estático/adaptador de fixtures (S1c); metas, carteira cripto e investimentos no modo real (ficam escondidos até S4/v2.1); importador do Itaú; conciliação/vínculos; variações percentuais ("+12% vs. mês passado") no modo real; PWA/offline; criação de usuários pela UI (continua seed); edição de tipo de conta.

## Anti-objetivos
- Aritmética de dinheiro no navegador, ou dinheiro como `number` em estado/props (exceto o valor entregue ao Chart.js para desenhar).
- Token ou estado de sessão em `localStorage`/`sessionStorage` (só cookie `httpOnly`; o que se guarda no navegador é preferência de visualização, como "esconder valores" e idioma).
- Regressão do demo atual na Vercel: o site público continua exatamente como está até a S1c.
- Valor vazando com o modo "esconder" ligado (tooltip do gráfico, `aria-label`, `title`, texto de erro).
- Dado real em fixture, teste, snapshot ou captura de tela commitados.

## Restrições
- Stack existente: Vue 3 + TypeScript + Pinia + Vue Router + Tailwind + Chart.js + vue-i18n + Vitest; identidade Grafite + acento atual. Mesma origem da API (nginx), cookie + antiforgery (`X-XSRF-TOKEN`; o token é amarrado à identidade: buscar `/api/auth/csrf` de novo após o login e antes de mutações).
- Contrato da API da S1a: dinheiro como **string decimal**, datas `YYYY-MM-DD`, enums como string, erros de import como `{error: code}`, 404 para recurso alheio, `401` sem sessão.
- Tipos da API **gerados** do OpenAPI (nunca escritos à mão); a CI falha se o arquivo gerado divergir do commitado.
- **Modo de build:** `VITE_APP_MODE` com padrão `demo`. O build do Compose define `real` (argumento de build no `deploy/web.Dockerfile`); a Vercel não define nada e segue o demo. O modo real não importa o código das páginas legadas.
- Acessibilidade básica: foco visível, rótulos em formulários, abas navegáveis por teclado, contraste suficiente.
- Nenhuma dependência nova sem motivo; versões fixas.

## Abordagens consideradas
A. **Telas novas ao lado; demo intocado; costura `api`** (escolhida pelo João, que delegou a escolha): páginas e stores novos para o modo real atrás de uma interface `api`; páginas legadas só no modo demo; adaptador estático entra na S1c e então o legado é apagado. B. Refatorar o Dashboard atual (674 linhas, mistura metas/carteira) para dois adaptadores já agora: desenho final, mas maior risco para o demo e fatia maior. C. Abandonar o demo e usar capturas/GIF: perde o artefato mais visível do portfólio.
**Decisão:** A, porque entrega o uso real sem arriscar o site público e prepara o demo que mostra o backend.

## Backend: preparação (pequena, primeira tarefa)
1. **OpenAPI fiel ao contrato**: decimais documentados como `string` com o padrão `^-?[0-9]{1,16}(\.[0-9]{1,2})?$` (hoje saem como `number`); enums como string. Um teste de snapshot compara `/api/openapi/v1.json` com `backend/openapi/v1.json` commitado (atualizado por comando explícito) e falha se divergir.
2. **Classificar na prévia**: `POST /api/imports/{id}/confirm` aceita `overrides` opcional `[{ sourceKey, state?, categoryId? }]`, aplicado às linhas **novas** do lote antes de gravar (linhas existentes continuam intocadas); `sourceKey` desconhecido ou categoria de outro usuário → `422`. Sem `overrides`, comportamento atual.
3. **Pendentes**: `GET /api/transactions?state=PendingClassification` já existe; garantir ordenação e paginação estáveis (sem mudança prevista).

## Camada de API do front
- Interface `api` (contas, categorias, lançamentos, checkpoints, resumo, importações, auth) com tipos derivados do esquema gerado; implementação HTTP com `fetch`: cookie incluído, token antiforgery obtido sob demanda e renovado após login/logout, `401` → sessão expirada (volta ao login preservando a rota), erros `{error}` mapeados para mensagens traduzidas, `400/422/409` tratados por tela.
- Stores Pinia finos por módulo (sessão, contas, lançamentos, resumo, importação, preferências) consumindo a interface; nenhum cálculo monetário.
- Dinheiro: um módulo de formatação recebe string e devolve texto (pt-BR/BRL); o componente/diretiva de exibição respeita "esconder valores". Entradas de formulário validam o formato aceito pela API antes de enviar e enviam string.
- Gráfico dos 6 meses = 6 chamadas paralelas ao resumo mensal; despesas por categoria = `byCategory` do mês (resolvidas por nome vindo do servidor).

## Telas e comportamento
- **Login/sessão:** e-mail e senha; erro genérico igual ao do servidor; ao entrar, vai para a Visão geral; sair invalida a sessão; rota protegida sem sessão → login.
- **Importar:** conta (só contas compatíveis com a fonte; cartão ↔ conta cartão), arquivo `.ofx` (um por vez, até 5 MB); mostra erro traduzido por `error` (`not_ofx`, `empty`, `too_large`, `malformed`, `forbidden_dtd`, `unsupported_multiple_statements`, `account_type_mismatch`, …). Prévia expira em 24 h (mostrar o aviso e permitir reenviar). Avisos `blocking` exigem marcar "entendo e quero confirmar". Confirmar mostra quantos foram criados/pulados. Histórico de lotes com estado, período e contagens; **desfazer** com confirmação que informa `editedCount` e que linhas que outro lote deixou de importar por já existirem também serão removidas.
- **Lançamentos/Pendentes:** valor com sinal exibido com cor e sinal; contas cartão com sinal explicado (cobrança negativa). Editar usa a mesma regra para importados e manuais; "apagar" de importado esconde (exclusão lógica) e o texto diz isso.
- **Contas:** saldo atual; "registrar saldo" cria checkpoint (data e valor) com explicação de que vale ao fim do dia.
- **Estados vazios, carregando e erro** em todas as telas; botões desabilitados durante requisições; sem recarregar a página para refletir mudanças.

## Testes e entrega
- Vitest: cliente HTTP (antiforgery, renovação após login, `401`, mapeamento de erros), formatação/máscara de dinheiro, stores com a interface `api` simulada, validação de formulários, abas da prévia e fluxo de classificação com `@vue/test-utils`.
- Backend: teste do snapshot OpenAPI e dos `overrides` do confirm (RED/GREEN, isolamento entre usuários, linha existente não alterada, `sourceKey` inválido).
- CI: o job do front gera os tipos e falha se `git diff` não estiver limpo; o build do front roda nos dois modos (`demo` e `real`).
- Verificação manual registrada: Compose no ar, fluxo completo no navegador com arquivo sintético (login → conta → importar → classificar → confirmar → ver resumo → desfazer), sem dados reais.
- Documentação: README do modo real (como rodar, `VITE_APP_MODE`, esconder valores) e atualização do ADR/README de arquitetura conforme necessário.

## Fica para o construtor
Biblioteca de geração de tipos (openapi-typescript ou equivalente) e de cliente, estrutura de pastas do código novo, componentes e design fino das telas dentro da identidade existente, tratamento de formulários, sintaxe do snapshot e do comando de atualização, textos das mensagens, paginação visual, estratégia de teste de componentes.

## Decisões complementares (minhas, após revisão por um construtor independente)
- **Categorias:** já nascem semeadas no backend (10 de despesa e 3 de receita, por usuário, no startup); a S1b usa apenas **seletores**, sem tela de gerenciamento. O seletor oferece só categorias do tipo compatível com o sinal do valor (positivo → Receita, negativo → Despesa); o servidor não impõe essa regra. Criar/renomear categorias pela UI fica para depois.
- **`overrides` do confirm:** `state` e `categoryId` são independentes e opcionais (uma linha pode ficar pendente com categoria, ou `Normal` sem categoria); sem regra de tipo no servidor. `sourceKey` inexistente na prévia ou categoria de outro usuário → `422 {error: "invalid_override"}` sem gravar nada; a resposta de sucesso continua `{batchId, created, skipped}`. Em `422`, a tela permanece na prévia e mostra o erro.
- **Aviso de pendentes:** global (todos os meses), via `GET /api/transactions?state=PendingClassification` (usa `total` da resposta) — é uma fila de trabalho. O cartão de pendentes do mês continua vindo de `pending {count,total}` do resumo. Valor monetário global dos pendentes não é exibido.
- **Sinal e contas:** saldo de cartão aparece negativo com o rótulo "Dívida"; lançamentos exibem o valor com sinal como vem da API. Os totais da Visão geral são os do servidor (todas as contas); não há filtro por conta na Visão geral (existe em Lançamentos).
- **Contas arquivadas:** somem da faixa da Visão geral, do seletor de conta da importação e do formulário de novo lançamento; aparecem em filtros, no histórico e numa seção "Arquivadas" da tela Contas (com opção de desarquivar).
- **Gráfico de 6 meses:** falha parcial — o mês que falhar aparece como lacuna com indicação e botão de tentar de novo; as demais barras são exibidas. Histórico vazio mostra estado vazio com atalho para importar.

## Decisões minhas para o João conferir
- Demo atual permanece até a S1c; o demo estático novo substitui e o legado é apagado nessa fatia.
- Padrão de build é `demo`; só o Compose liga `real` (evita o site público cair em login).
- Classificação **dentro da prévia** exige uma pequena mudança no backend (`overrides`); a alternativa seria classificar só depois de confirmar.
- Sem variações percentuais no modo real até o servidor fornecê-las.
- Inglês (en-US) mantido, pt-BR é a referência.
- "Esconder valores" é preferência do navegador (não da conta).
