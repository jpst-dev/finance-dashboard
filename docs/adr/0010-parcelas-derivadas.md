# ADR 0010: Parcelas comprometidas derivadas

- Status: aceita
- Data: 2026-11-03

## Contexto
O app mostra o que sobra no mês, mas não o que já está amarrado nos próximos por compras parceladas no cartão Itaú. O Itaú entrega "Parcela N de T" em cada linha da fatura, que só traz parcelas já cobradas. A S3(d) projeta as que faltam.

## Decisão
- **Derivar em vez de persistir**: nada de entidade "grupo de parcelas", tabela, migração ou lançamento "previsto". Tudo é calculado a cada consulta (`GET /api/installments`) a partir das linhas importadas. **Isto substitui o desenho do spec pai** (grupos persistidos e previstos que viram realizados): sem estado que possa ficar incoerente com os lançamentos, sem rotina de conciliação previsto/realizado, e a projeção nunca entra em resumo, saldo ou categoria.
- **Fatura-base por conta**: o maior `StatementDate` entre as linhas **importadas, visíveis e de cobrança (`Amount < 0`)** das contas de cartão, parceladas ou não; linhas positivas (pagamentos) nunca definem a base. Só as linhas parceladas dessa fatura (`StatementDate == base`, `Amount < 0`, `T ≥ 2`) são projetadas; compra que sumiu da fatura mais recente (quitada, estornada, antecipada) deixa de ser projetada.
- **Projeção por linha**: a parcela `N+i` cai em `base.AddMonths(i - 1)`, sempre a partir do vencimento-base (31 vira 28/29 sem arrastar), até 36 parcelas. Linhas iguais (conta, descrição normalizada, N, T, valor) viram um grupo com multiplicidade.
- **Mês de exibição = mês em que a linha cai no resumo mensal** (o resumo agrupa por `Date`, que nas parcelas N>1 é vencimento menos 1 mês), não o mês do vencimento da fatura.
- **"Hoje" corta o passado**: só parcelas com data a partir do primeiro dia do mês corrente (America/Sao_Paulo) entram nos meses, no total e nos grupos. Com a fatura-base antiga o comprometido some até importar a mais recente; a API devolve `basedOn` por conta e a tela avisa quando o mês da base é anterior ao mês atual.
- **Estorno não cancela**: estorno tem sinal positivo e é ignorado; a parcela continua projetada se estiver na fatura-base.
- **Um vencimento por conta**: se houver mais de um, vale o maior.
- **Nubank não tem parcelas**: o OFX do cartão não traz parcelamento. Pix parcelado e parcelamento de fatura com "Parcela N de T" entram como qualquer outra parcela.
- **API**: dinheiro como string decimal em módulo; contas arquivadas incluídas (a dívida continua); linhas manuais, ocultas e excluídas ignoradas. **Demo**: `installments.json` gerado pelo servidor real sob o relógio 2026-10-15; somente leitura.

## Consequências
- Recalcula a cada consulta (poucas linhas por conta; sem cache).
- Sem histórico de grupos nem de "o que foi previsto"; trocar de fatura-base muda a projeção.
- Quem pensa pelo vencimento vê cada parcela no mês anterior ao do vencimento.
- Limitações: estorno não cancela o grupo; um vencimento por conta; sem antecipação, renegociação ou edição manual; juros do Pix parcelado, recorrências e aplicação/resgate ficam fora.

## Alternativas consideradas
- Persistir grupos e previstos (spec pai): mais fiel a um "livro", mas exige migração, vínculo previsto/realizado e correção quando a fatura diverge.
- Mostrar pelo mês do vencimento: coerente com a fatura, mas a parcela "de dezembro" apareceria no resumo de novembro.
- Fatura-base global (não por conta): erra quando as contas importam em meses diferentes.
