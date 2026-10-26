# Finance Dashboard: identidade "Grafite + lima" (design curto)

## Intenção
Dar ao Finance Dashboard uma identidade visual no nível do DealsGamer e do CineMatch, com o **mínimo de esforço**: o João quer guardar energia para o Veritas Fidei. O projeto é uma vitrine do portfólio, então quem abre precisa ver um dashboard preenchido e bonito, não telas vazias.

## Objetivos
- Direção **A · Grafite + lima** (mock aprovado em `2026-10-26-finance-direcoes-mock.html`): fundo grafite `#0a0b0d`, cartões `#121418`, bordas `#1f232a`, acento lima `#c6f432`, perdas em coral `#ff6b6b`, texto `#f2f4f7`/`#8b93a1`, Inter e números em JetBrains Mono.
- Menu lateral no computador e barra no topo com menu no celular; páginas Visão geral (Dashboard) e Portfólio.
- **Dados de exemplo na primeira visita** (transações dos últimos 6 meses, metas e investimentos em cripto) e um botão "Limpar dados" que zera tudo.
- Publicar em `finance.jteixeira.dev` e atualizar o card no portfólio.

## Fora do escopo
- Reescrever a lógica, trocar bibliotecas (Pinia, Axios, Chart.js, vue-i18n continuam) ou mexer nas regras de cálculo.
- Tema claro: o app passa a ser só escuro (o botão de tema sai).
- Corrigir a dependência do CoinGecko via corsproxy.io.

## Abordagens
1. **Reskin pelo tema (escolhida):** remapear no `tailwind.config` as paletas `gray`, `blue`, `green`, `red` e `indigo` usadas nos componentes para as cores da identidade, forçar o modo escuro e redesenhar à mão só o que aparece primeiro (estrutura do app, cartões de resumo, gráficos). Barato e cobre os ~20 componentes de uma vez. Custo: algum componente secundário pode ficar com contraste estranho, que corrijo pontualmente.
2. **Redesenho componente a componente:** resultado mais fino, mas é reescrever o markup de 5.000 linhas. Não combina com "só o essencial".
3. **Recomeçar com outra stack:** fora de questão pelo mesmo motivo.

## Decisões minhas (para conferir)
- Dados de exemplo entram só quando não há nada salvo no navegador; "Limpar dados" pede confirmação e não recria o exemplo depois (há também "Restaurar exemplo").
- Nome no app: **Finance Dashboard** com o "F" lima como logo (o "Finança" do mock era só um rascunho).
- Testes: Vitest só para a geração dos dados de exemplo (determinística e dentro dos últimos 6 meses). O resto é visual e eu verifico com capturas no computador e no celular.
