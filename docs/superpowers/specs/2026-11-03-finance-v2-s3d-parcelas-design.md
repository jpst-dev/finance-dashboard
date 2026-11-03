# S3(d) Parcelas comprometidas — design

Continua a S3 (spec pai `2026-11-02-finance-v2-backend-design.md`: "Grupo de parcelas" e "quanto já está comprometido nos próximos meses"). Escopo escolhido pelo Claude em modo ausente (ver `docs/away-log.md`): **só parcelas**. Recorrências, juros do Pix parcelado e aplicação/resgate ficam para depois.

## Intenção e por quê
O app mostra o que sobra no mês, mas não o que já está amarrado nos meses seguintes por compras parceladas no cartão. O Itaú entrega "Parcela N de T" por linha; com isso dá para projetar as parcelas que ainda vão cair e mostrar quanto está comprometido por mês.

## Objetivos
1. Projetar as parcelas restantes das compras parceladas do cartão Itaú, a partir da **fatura mais recente importada de cada conta**.
2. Endpoint com o comprometido por mês e os grupos ativos.
3. Página **Parcelas** e um cartão "Comprometido nos próximos meses" na Visão geral.
4. Demo estático com exemplo, gerado pelo servidor real.

## Não-objetivos
- Entidade persistida, lançamentos "previstos" no banco, tabela nova, migração. **Decisão que substitui o spec pai** (que previa grupos persistidos e previstos que viram realizados): tudo é derivado das linhas importadas a cada consulta; vira ADR.
- Parcelas do Nubank (o OFX do cartão não traz parcelamento), recorrências/assinaturas, juros do Pix parcelado, renegociação, antecipação, edição manual de grupos.
- Entrar no resumo mensal, saldos ou categorias: a projeção é só uma visão; nada futuro vira lançamento.

## Fatos do código que o cálculo respeita
- No app **cobrança é valor negativo** (o leitor da fatura do Itaú inverte o sinal; pagamento e estorno ficam positivos). A parcela é uma linha com `Amount < 0`.
- Em linhas de fatura do Itaú, `StatementDate` é o **vencimento da fatura** (igual para todas as linhas dela) e `Date` das parcelas N>1 é o vencimento menos 1 mês (`ItauRules.InvoiceLineDate`); o resumo mensal agrupa por `Date`.
- A fatura do Itaú só contém parcelas já cobradas (nunca as futuras).

## Modelo (derivado, por linha)
- **Linha parcelada** = linha `Imported`, visível, de conta `CreditCard`, `StatementDate` não nulo, `Amount < 0`, `InstallmentNumber` e `InstallmentTotal` não nulos com `T ≥ 2` e `1 ≤ N ≤ T`. Linhas manuais não contam. Pendentes e neutras contam (o estado não importa).
- **Fatura-base da conta** = o maior `StatementDate` entre as linhas **importadas** da conta (`BasedOn`). Só entram na projeção as linhas parceladas da fatura-base (`StatementDate == BasedOn`): uma compra que sumiu da fatura mais recente (quitada, estornada, antecipada) não é projetada. O vencimento é por conta; mais de um vencimento por conta usa o maior (limitação).
- **Projeção por linha** (sem agrupar para calcular): para cada linha da fatura-base, as parcelas `N+1 … T`. A parcela `N+i` cai em `BasedOn.AddMonths(i - 1)` (sempre calculado a partir do vencimento-base, nunca em cadeia) e **é mostrada no mês dessa data, o mesmo mês em que ela vai aparecer no resumo mensal quando for importada**. Valor = módulo do valor atual da linha (respeita edição do usuário). Horizonte: até 36 parcelas contadas a partir da data-base.
- **"Hoje"**: só entram em `months`, `totalCommitted` e nos grupos as parcelas com data ≥ primeiro dia do mês corrente (`TimeProvider`, fuso America/Sao_Paulo). Parcelas de uma fatura-base antiga que já ficaram no passado não contam; a API devolve `BasedOn` por conta para a tela avisar quando a base está desatualizada.
- **Grupo para exibição**: linhas da fatura-base com mesma (conta, descrição normalizada com `NameKeys.Normalize`, `N`, `T`, valor) viram um grupo com `multiplicity = k`. Grupo com 0 parcelas restantes dentro da regra de "hoje" não aparece.
- `totalCommitted` = soma dos `months[].total` (dentro do horizonte); `remainingTotal` do grupo = soma das parcelas restantes do grupo dentro do horizonte e da regra de "hoje". As duas somas fecham entre si.
- Estorno de compra parcelada não cancela o grupo (o estorno tem sinal positivo e é ignorado); limitação documentada. Pix parcelado e parcelamento de fatura que tragam "Parcela N de T" entram como qualquer outra parcela.

## Cálculo (puro, no Domain)
`InstallmentProjector.Project(IReadOnlyList<InstallmentLine> lines, DateOnly today) -> InstallmentProjection`, onde `InstallmentLine(Guid AccountId, string AccountName, string? CardSuffix, string Description, string NormalizedDescription, int Number, int Total, decimal Amount, DateOnly StatementDate)` (a camada de aplicação já filtrou conforme "Linha parcelada"). Retorna `Accounts` (`AccountBase(AccountId, AccountName, BasedOn)`), `Groups` (`InstallmentGroupView(AccountId, AccountName, CardSuffix, Description, InstallmentNumber, InstallmentTotal, Remaining, Amount, Multiplicity, EndsOn, RemainingTotal)`), `Months` (`InstallmentMonth(Month: DateOnly primeiro dia, Total, Items)` com `InstallmentItem(AccountId, Description, CardSuffix, InstallmentNumber, InstallmentTotal, Amount)`) e `TotalCommitted`. Determinístico e independente da ordem de entrada; ordenação: meses crescentes; itens por conta, descrição, cartão; grupos por `EndsOn`, depois descrição.

## API
- `GET /api/installments` → `{ totalCommitted, accounts: [{ accountId, accountName, basedOn }], months: [{ month: "YYYY-MM", total, items: [{ accountId, description, cardSuffix, installmentNumber, installmentTotal, amount }] }], groups: [{ accountId, accountName, cardSuffix, description, installmentNumber, installmentTotal, remaining, amount, multiplicity, endsOn, remainingTotal }] }`.
- **Dinheiro como string decimal** (convenção do projeto: conversor de decimais e padrão OpenAPI); `amount` e `total` em módulo (positivos); inteiros precisam entrar em `IntFields` no front; todos os campos obrigatórios (`cardSuffix` anulável).
- Autenticado, só dados do usuário (query filters); contas arquivadas **incluídas** (a dívida continua); linhas ocultas/excluídas ignoradas. A descrição é a descrição exibida no app (`Description`, que o usuário pode ter renomeado).
- Sem escrita.

## Front
- Página **Parcelas** (`/installments`, `real.titles.installments`, `real.nav.installments`, item de menu): barras do comprometido por mês (`chartMoneyOptions`/`toPlot`) e lista de grupos (descrição, conta/cartão, "N de T", restantes, valor, termina em). Aviso por conta quando `basedOn` é anterior ao mês passado ("baseado na fatura de MM/AAAA; importe a mais recente"). Estado vazio explicado. Valores via `Money.vue`, respeitando "esconder valores". Texto de terceiros sempre como texto.
- **Visão geral**: cartão "Comprometido nos próximos meses" com o total do mês corrente + 2 seguintes e link para a página; fica fora do MonthPicker; some se `totalCommitted` for 0; store próprio com loading/erro.
- Plurais (nenhuma | 1 | N) em pt-BR e en-US ("1 parcela restante" / "N parcelas restantes").

## Demo
`DemoSeed` ganha linhas de fatura Itaú com `StatementDate` (o demo atual não preenche) e 2 compras parceladas com parcelas restantes na fatura-base, geradas pelo servidor real e fixas sob o relógio 2026-10-15; atenção ao `CardSuffix` consistente entre as parcelas, ao total declarado da fatura e às asserções do seed. `fixtures/installments.json`, `fixtures.ts`, `staticApi.installments`, interface em `http.ts`, `fixtures.test.ts` e `e2e-demo.test.ts` cobertos. Sem escrita.

## Decisões minhas (Rulings)
- Derivar em vez de persistir: sem migração e sem estado que possa ficar incoerente; custo: recalcula a cada consulta e substitui o desenho do spec pai (ADR).
- Mês de exibição = mês em que a parcela cai no resumo (não o do vencimento): evita a "comprometido em dezembro, aparece em novembro". Custo: quem pensa pelo vencimento da fatura vê o mês anterior ao vencimento.
- Projetar por linha da fatura-base: elimina a multiplicidade e a identidade de grupo entre faturas; custo: sem histórico de grupos.
- "Hoje" corta parcelas passadas; custo: com a fatura-base antiga o comprometido some até importar a mais recente (a tela avisa).
- Estorno de compra parcelada não cancela; Pix parcelado entra se trouxer N/T; um vencimento por conta.
- Contas arquivadas incluídas.

## Testes
Domínio: 3/10 ⇒ 7 restantes com meses corretos e `AddMonths` a partir da base (31 ⇒ 28/29 sem arrastar), parcela N=T não projeta, grupo fora da fatura-base não projeta, sinal (apenas `Amount < 0` entra), "hoje" corta o passado, multiplicidade k, contas separadas, vencimentos diferentes por conta, horizonte de 36, ordem de entrada irrelevante (`[Theory]` com seed), soma dos meses = `totalCommitted` e dos grupos. API: resposta, strings decimais, isolamento por usuário, 401, linhas manuais/ocultas ignoradas, conta arquivada incluída, relógio fixo. Front: página, aviso de base antiga, estado vazio, plural, XSS, cartão da Visão geral, valores escondidos. Demo: fixtures e e2e sem chamada à API.

## O que fica para o construtor
Nomes de arquivos/componentes, estilo do gráfico, textos i18n, o filtro SQL das linhas parceladas e a ADR (curta, `docs/adr/0010-parcelas-derivadas.md`).
