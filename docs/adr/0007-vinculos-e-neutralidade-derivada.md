# ADR 0007: Vínculos entre lançamentos e neutralidade derivada

- Status: aceita
- Data: 2026-11-03

## Contexto
Pagamento de fatura (saída na conta, entrada no cartão) e transferência entre contas próprias (saída numa conta, entrada na outra) aparecem como duas linhas `PendingClassification`. Elas ficam fora do resumo, mas poluem a fila de pendências e não dizem que são o mesmo movimento. A S3(a) precisa ligar as duas pontas sem mexer no que o resumo e os saldos já calculam.

## Decisão
- **`Link` é uma tabela**: `OutTransactionId` (valor negativo), `InTransactionId` (positivo), `Kind` (`InvoicePayment`, `OwnTransfer`), `State` (`Suggested`, `Confirmed`, `Rejected`) e `Origin` (`Auto`, `User`). Índice único no par (Out, In) e **um índice único parcial por coluna** (`State = 'Confirmed'`), de modo que uma linha tem no máximo um vínculo confirmado em cada coluna. A invariante entre colunas (a mesma linha não ser Out de um e In de outro) é garantida pela regra de sinal (Out < 0, In > 0) e pelo `LinkService`, que checa as duas colunas antes de confirmar. As FKs para `Transaction` são **cascade**, porque desfazer lote usa `ExecuteDelete`.
- **Neutralidade derivada**: uma linha é neutra se tem vínculo `Confirmed`. Não há campo novo em `Transaction` e o `State` não muda. A fila de pendências e o `PendingSummary` excluem linhas com vínculo confirmado (predicado único `WithoutConfirmedLink`); saldos e receitas/despesas não mudam.
- **Pareamento exato 1:1** (`LinkMatcher`): contas diferentes, valores opostos iguais, datas a até `WindowDays = 3`, papéis compatíveis (fatura só no mesmo banco: conta e cartão do Nubank, ou do Itaú; transferência só entre contas correntes). Só lê linhas `PendingClassification`, visíveis e sem vínculo confirmado.
- **Ambiguidade medida no grafo inteiro**: o par só é confirmado sozinho se seu componente conexo (no grafo de candidatos da janela de 3 dias) tem uma única aresta. `OwnTransfer` só confirma sozinho com |Δdata| ≤ `AutoTransferDays = 1`; acima disso fica `Suggested`.
- **Sugestões recalculadas** a cada reconciliação (as `Suggested` são apagadas e refeitas); `Confirmed` e `Rejected` são preservados.
- **Reconciliação dentro da transação da confirmação da importação**, sob `pg_advisory_xact_lock` por usuário; reconciliar, confirmar, rejeitar, desfazer vínculo e invalidar usam o mesmo lock, então não se intercalam. **Exceção:** desfazer um lote de importação (`ImportService.UndoAsync`) não toma esse lock. Consequência, muito rara num uso de um só usuário: editar/confirmar ao mesmo tempo que um desfazer de lote pode falhar por deadlock ou violação de FK; a transação é revertida e a API responde 500, sem perda de dados (basta repetir).
- **Desfazer vínculo marca `Rejected`**: o matcher nunca recria o par.
- **Editar valor, data, ou estado para `Normal`, e ocultar a linha, apagam os vínculos `Suggested` e `Confirmed` dela** e reconciliam de novo (`LinkService.InvalidateAsync`). Os `Rejected` sobrevivem à edição, para um par rejeitado não voltar depois de o usuário ajustar a linha. Excluir uma linha manual apaga por cascata.

## Consequências
- O resumo e os saldos continuam como estavam; o ganho é a fila limpa e os movimentos internos identificados (tela Vinculados).
- **A memória de `Rejected` se perde ao desfazer um lote**: reimportar cria linhas com ids novos.

### Limitações conhecidas
- Sem 1:N, sem pagamento parcial ou maior que a fatura.
- Fatura paga por conta de outro banco continua pendente.
- Pix para terceiro de valor igual a um recebimento em até 1 dia, sem outro candidato, pode ser confirmado sozinho como transferência (visível em Vinculados, desfazível com um clique).
- Editar uma linha de `Normal` para `PendingClassification` não dispara reconciliação (só a próxima importação ou `POST /api/links/reconcile`).
- Depois de uma edição que apaga vínculos, o mesmo par pode ser sugerido de novo.

## Alternativas consideradas
- Campo `IsInternal` em `Transaction`: duplica o fato e pode divergir do vínculo.
- Nunca auto-confirmar `OwnTransfer`: mais conservador, mas a fila continuaria cheia; fica como ajuste de uma condição no matcher.
