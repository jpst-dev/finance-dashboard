# S3(d) Parcelas comprometidas — design

Continua a S3 (spec pai `2026-11-02-finance-v2-backend-design.md`: "Grupo de parcelas" e "quanto já está comprometido nos próximos meses"). Escopo escolhido pelo Claude em modo ausente (ver `docs/away-log.md`): **só parcelas**. Recorrências, juros do Pix parcelado e aplicação/resgate ficam para depois.

## Intenção e por quê
O app mostra o que sobra no mês, mas não o que já está amarrado nos meses seguintes por compras parceladas no cartão. O Itaú já entrega "Parcela N de T" por linha; com isso dá para projetar as parcelas que ainda vão cair e mostrar quanto está comprometido por mês.

## Objetivos
1. Agrupar as linhas parceladas do cartão em **grupos de parcelas** e projetar as parcelas futuras de cada grupo.
2. Endpoint que devolve o **comprometido por mês** e a lista de grupos ativos.
3. Página **Parcelas** (comprometido por mês + grupos) e um cartão "Comprometido nos próximos meses" na Visão geral.
4. Demo estático com exemplo, gerado pelo servidor real.

## Não-objetivos
- Entidade `InstallmentGroup` persistida, lançamentos "previstos" no banco, tabela nova, migração. Tudo é **derivado** das linhas importadas a cada consulta (volume pessoal pequeno).
- Parcelas do Nubank (o OFX do cartão não traz parcelamento), Pix parcelado e juros, recorrências/assinaturas, renegociação, antecipação de parcelas, edição manual de grupos.
- Entrar no resumo mensal, nos saldos ou nas categorias: a projeção é só uma visão; nada futuro vira lançamento.

## Modelo (derivado)
- Linha parcelada = linha importada, visível, de conta `CreditCard`, com `InstallmentNumber` e `InstallmentTotal` não nulos (`1 ≤ N ≤ T`, `T ≥ 2`), valor positivo (cobrança).
- **Chave do grupo**: `(AccountId, CardSuffix, descrição normalizada sem o sufixo de parcela, InstallmentTotal)`, com `NameKeys.Normalize` sobre `OriginalDescription`. O valor da parcela não entra na chave (a última parcela pode variar centavos).
- **Mês da fatura da linha** = mês de `StatementDate` (vencimento da fatura); se nulo, mês de `Date`.
- **Multiplicidade**: duas compras idênticas (mesma chave) na mesma fatura contam como `k` linhas; a projeção usa `k` da fatura mais recente do grupo.
- **Grupo ativo**: o grupo tem linha na **fatura mais recente importada da conta do cartão** (mesmo mês de fatura que o máximo entre as linhas da conta). Um grupo que sumiu da fatura mais recente (quitado, estornado, antecipado) não é projetado.
- **Parcelas restantes**: da parcela `N+1` até `T`, onde `N` é o maior número observado na fatura mais recente do grupo. A parcela `N+i` cai no mês da fatura mais recente + `i` meses, no mesmo dia do vencimento (limitado ao último dia do mês). Valor = valor da linha mais recente do grupo × `k`. A última parcela usa o mesmo valor (diferença de centavos fica registrada como limitação).
- Horizonte máximo 36 meses a partir do mês da fatura mais recente.

## Cálculo (puro, no Domain)
`InstallmentProjector.Project(IReadOnlyList<InstallmentLine> lines) -> InstallmentProjection` onde `InstallmentLine(Guid AccountId, string? CardSuffix, string NormalizedDescription, int Number, int Total, decimal Amount, DateOnly Date, DateOnly? StatementDate)`. Retorna `Groups` (`InstallmentGroupView(AccountId, CardSuffix, Description, Number, Total, Remaining, Amount, Multiplicity, EndsOn, RemainingTotal)`) e `Months` (`InstallmentMonth(Month: DateOnly primeiro dia, Total, Items)`), `TotalCommitted`. Determinístico e independente da ordem de entrada.

## API
- `GET /api/installments` → `{ totalCommitted, months: [{ month: "YYYY-MM", total, items: [{ description, cardSuffix, accountId, number, total, amount }] }], groups: [{ accountId, accountName, cardSuffix, description, number, total, remaining, amount, multiplicity, endsOn, remainingTotal }] }`; todos os campos obrigatórios (decimais como número, sufixo de cartão nulo permitido).
- Autenticado, só dados do usuário (query filters); sem escrita.
- A descrição devolvida é a `OriginalDescription` da linha mais recente do grupo (texto de terceiros: nunca como HTML no front).

## Front
- Página **Parcelas** (`/installments`, título `real.titles.installments`, item de menu): gráfico/tabela do comprometido por mês e lista de grupos (descrição, cartão, "N de T", restantes, valor, termina em). Estado vazio explicado ("Importe uma fatura do Itaú com compras parceladas").
- **Visão geral**: cartão "Comprometido nos próximos meses" com o total dos próximos 3 meses e link para a página; some se não há parcelas.
- Textos de terceiros sempre como texto; plurais corretos ("1 parcela restante" / "N parcelas restantes").

## Demo
`DemoSeed` já tem a parcela 3/3 de "Loja Exemplo"; ganha 2 grupos de exemplo com parcelas restantes (fatura mais recente com `N<T`), gerados pelo servidor real; `fixtures/installments.json`; o adaptador estático lista; sem escrita.

## Decisões minhas (Rulings)
- Derivar em vez de persistir grupos: sem migração, sem estado para ficar incoerente; custo: recalcula a cada consulta (ok para volume pessoal).
- Só grupos presentes na fatura mais recente: evita projetar compra já quitada/estornada; custo: se o usuário não importar a fatura mais recente de um cartão, os grupos dele somem da projeção até importar.
- Multiplicidade `k` por fatura mais recente: cobre compras idênticas; custo: duas compras idênticas com parcelas em momentos diferentes seriam confundidas (raro).
- Valor igual em todas as parcelas restantes: a última pode diferir centavos.
- Sem Nubank: o OFX não informa parcelas; extrair "N/T" da descrição fica para quando houver amostra.

## Testes
Domínio: projeção básica (3/10 ⇒ 7 restantes, meses corretos, dia do vencimento limitado ao fim do mês), grupo quitado (N=T) não projeta, grupo ausente da fatura mais recente não projeta, multiplicidade, duas contas/cartões separados, ordem de entrada irrelevante (`[Theory]` com seed), descrição normalizada e sufixo de parcela removido, horizonte de 36 meses. API: resposta, isolamento por usuário, 401, linhas ocultas/excluídas ignoradas, conta arquivada. Front: página, estado vazio, plural, XSS, cartão da Visão geral. Demo: fixtures e e2e sem chamada à API.

## O que fica para o construtor
Nomes exatos de arquivos/componentes, estilo do gráfico (reutilizar `charts`), textos i18n e como remover o sufixo "N/T" da descrição normalizada (o Itaú traz o parcelamento em coluna própria; só a descrição precisa ser comparável entre faturas).
