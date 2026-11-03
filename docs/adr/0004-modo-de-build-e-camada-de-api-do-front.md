# ADR 0004: Modo de build, camada de API tipada e dinheiro como texto no front

- Status: aceita
- Data: 2026-11-03

## Contexto
O front da v1 é uma demo estática (dados de exemplo no `localStorage`, publicada na Vercel). A v2 precisa de uma interface real, com login, dados do servidor e importação de extratos, servida pelo Compose na mesma origem da API (ADR 0002). As duas coisas vivem no mesmo repositório e a demo pública não pode quebrar, nem ganhar código de autenticação ou de chamadas à API que ela nunca usará. Além disso, o front lida com dinheiro: erros de ponto flutuante, valores aparecendo em lugares inesperados (título da página, atributos de acessibilidade) e vazamento de dados reais em logs ou mensagens de erro são riscos concretos.

## Decisão
- **Modo definido em tempo de build, não em tempo de execução.** `VITE_APP_MODE` é fixada pelo `vite.config.ts` via `define` (`demo` por padrão; só o valor exato `real` ativa o modo real). `src/main.ts` escolhe o ponto de entrada com essa constante, então o Rollup elimina o ramo do outro modo. `npm run build` gera `dist/` (demo, usado na Vercel) e `npm run build:real` gera `dist-real/` (usado pela imagem Docker do `web`). O script `scripts/check-bundles.mjs`, executado na CI, falha se `dist/` contiver o marcador `data-finance-real-mode` ou se `dist-real/` contiver código do demo (ex.: a chave `finance_dashboard_transactions`) ou não tiver o marcador.
- **Código do modo real isolado em `src/real/`**, com roteador, i18n, lojas Pinia e componentes próprios. O código legado não é alterado.
- **Tipos gerados do OpenAPI.** `backend/openapi/v1.json` é um snapshot versionado, verificado por teste do backend; `npm run api:types` gera `src/real/api/schema.d.ts` e os tipos de domínio (`src/real/api/types.ts`) são derivados dele, sem formato escrito à mão (exceto os corpos de autenticação, que o OpenAPI não descreve). A CI regenera os tipos e falha com `git diff --exit-code` se houver divergência (drift).
- **A interface `ApiClient` é a costura entre a interface e a origem dos dados.** Hoje existe a implementação HTTP (`createHttpApi`); as páginas e lojas só conhecem a interface, injetada por `setApi`/`getApi`. Na S1c, um adaptador estático (dados de exemplo no navegador) implementará a mesma interface e a mesma interface de usuário poderá ser usada na demo. Os testes usam implementações simuladas da mesma interface, inclusive o teste ponta a ponta (`src/real/e2e-smoke.test.ts`).
- **Dinheiro é sempre `string` decimal** (`"-1234.56"`), como o servidor envia. O front não faz aritmética: totais, saldos e resumos vêm calculados do servidor. Existe uma única fronteira de conversão para `number`, `toPlot`, usada só para desenhar gráficos. Entrada do usuário é validada e normalizada como texto.
- **Esconder valores.** O componente `Money` é o único ponto que formata dinheiro; com a preferência ligada ele mostra `••••` com `aria-label` genérico. Títulos de página nunca contêm valores. A preferência vive só no navegador (`fd.hideValues`), sem chamada à API. Um teste ponta a ponta varre texto, atributos e `document.title` em Visão geral, Lançamentos, Pendentes e Importar.
- **Antiforgery.** O token de `/api/auth/csrf` fica só em memória, é pedido sob demanda nas mutações e enviado em `X-XSRF-TOKEN`. Como ele é atrelado à identidade, é descartado após login, logout, 401 e 400 de antiforgery; o 400 sem corpo (antiforgery inválido) é repetido uma única vez com token novo, e falhas de rede nunca são repetidas, pois uma mutação pode ter chegado ao servidor. Um 401 em qualquer chamada leva ao login com `next` seguro.
- **Mensagens de erro por código.** A interface traduz o código estável de erro (`real.errors.<código>`); nunca exibe texto, valores ou descrições vindos do servidor.

## Consequências
- A demo na Vercel continua idêntica e não carrega código do modo real; o bundle real não carrega a demo. A garantia é automatizada (`check-bundles`), não depende de disciplina.
- Há dois builds para manter verdes na CI (`build` e `build:real`), e o modo não pode ser trocado sem recompilar: a imagem do `web` precisa ser reconstruída (`docker compose up -d --build web`) para refletir o front.
- Mudanças de contrato na API forçam regenerar o snapshot e os tipos no mesmo commit; o desencontro vira falha de CI, não erro em produção.
- A S1c poderá reaproveitar páginas e lojas trocando só o `ApiClient`; os custos são manter o contrato da interface estável e evitar que páginas dependam de detalhes HTTP.
- Como não há aritmética no navegador, qualquer cálculo novo de dinheiro deve ser feito no servidor; `toPlot` limita onde um `number` pode aparecer.
- Os inteiros do OpenAPI (`number | string`) são estreitados para `number` em um único lugar (`types.ts`), porque o servidor sempre os escreve como número.

## Alternativas consideradas
- **Flag em tempo de execução (um bundle só)**: descartada; o bundle da demo carregaria código de login e de API, o modo real carregaria a demo, e um erro de configuração poderia expor a tela errada em produção.
- **Refatorar o Dashboard atual para dois adaptadores (`localStorage` e HTTP)**: descartada nesta fatia; é uma reescrita grande da v1, com risco de regressão na demo, para telas que o modelo real mudou (contas, estados, pendentes, importação). A costura `ApiClient` deixa esse caminho aberto para a S1c sem pagar o custo agora.
- **Abandonar a demo**: descartada; ela é o cartão de visita público e funciona sem backend.
- **Tipos escritos à mão**: descartada; divergem do servidor sem aviso. Geração + verificação de drift na CI evita isso.
- **`number` para dinheiro**: descartada; erros de ponto flutuante e a tentação de recalcular no cliente. Texto decimal preserva exatamente o que o servidor calculou.
