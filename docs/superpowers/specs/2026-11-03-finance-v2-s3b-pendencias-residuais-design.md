# S3(b) — Pendências residuais: Pix, boleto, Pix no crédito e "sem contrapartida"

Spec pai (vinculante): `2026-11-02-finance-v2-backend-design.md`. Continua a S3(a) (`2026-11-03-finance-v2-s3a-conciliacao-design.md`, ADR 0007). Fica de fora desta fatia: regras de categoria e recorrências (S3(c)), aplicação/resgate e crédito consignado (ficam como estão).

## Intenção e por quê

Depois de usar a S3(a) com dados reais, o João viu que a fila de Pendências ainda tem lixo que não precisa de decisão e casos que o pareamento não alcança:

1. **Pagamento de fatura sem extrato da conta**: a fatura de setembro traz o pagamento da fatura de agosto, mas o extrato da conta de agosto não foi importado. Não há com quem parear; a linha fica pendente para sempre.
2. **Boleto e Pix para terceiros** entram como pendentes por cautela da S2. Na prática são despesa/receita normais.
3. **Pix para a própria conta**: o único caso que exige atenção. No Itaú o nome vem cortado em 7 letras com sufixo `DD MM` e caixa variável (estrutura confirmada nas amostras: 13/13 linhas `PIX TRANSF`), então cadastrar o nome não serve; no Nubank vêm nome completo, CPF mascarado e banco.
4. **Pix no crédito**: `CREDITO LIBERAD PIX …` (+X) e `PIX CARTAO …` (−X), mesmo valor, mesma data, **mesma conta** (4/4 pares exatos nas amostras); o Pix cancelado traz `EST CRED LIB PIX …` (−X) no mesmo dia (2/2). Na conta o efeito líquido é zero; o gasto real é a cobrança que vem na fatura do cartão.

Objetivo do João: a fila só tem o que realmente exige uma decisão, e o Pix para si mesmo não infla receita nem despesa.

## Decisões do João (brainstorm de 2026-11-03)
- Boleto e Pix para terceiros entram como **Normal**.
- Pix para a própria conta: pareamento por valor/data (já existe) **mais** botão "é meu" com aprendizado do trecho do nome, sempre como **sugestão** (nunca automático).
- Pix no crédito: novo tipo de vínculo dentro da mesma conta, neutro.
- Caso sem contrapartida: **vínculo com um lado vazio** (opção A), neutralidade derivada de vínculo confirmado.
- Botão "Reconciliar pendências" na tela Pendências.
- Reclassificar as linhas já importadas que ficaram pendentes (sem tocar nas editadas pelo usuário).

## Objetivos
1. Itaú conta: `PIX TRANSF` e `PAG BOLETO` passam a `Normal`; Nubank conta: "Transferência enviada/recebida pelo Pix" passa a `Normal`. Continuam `Pendente`: pagamento de fatura (até casar), `CREDITO LIBERAD`/`PIX CARTAO`/`EST CRED` (até casar), `CREDITO CONSIGNADO`, `APLICACAO`, `RESGATE`, "Pix no crédito" do Nubank, desconhecidos.
2. Pareamento passa a considerar linhas com papel `Transfer` **em qualquer estado**; para `InvoiceAccountSide`, `InvoiceCardSide` e os papéis de Pix no crédito continua exigindo `Pendente` (quem muda uma dessas linhas para Normal está dizendo que ela conta, e a edição apaga os vínculos dela).
2b. **Auto-confirmação de `OwnTransfer` exige coerência de nome** (além de componente de um par e `|Δdata| ≤ 1`): o trecho do nome da contraparte de uma ponta tem que ser prefixo do da outra (normalizados, o mais curto com pelo menos 7 caracteres). Sem nome em alguma ponta, ou nomes incoerentes, o par entra `Suggested`. Isso evita que um pagamento e um recebimento de terceiros de mesmo valor (aluguel × freela) sumam como transferência.
3. **Neutralidade em qualquer estado**: linha com vínculo `Confirmed` sai de receita/despesa e da fila, seja `Normal` ou `Pendente`. Saldos continuam inalterados.
4. Novo tipo de vínculo `PixCredit`: `CREDITO LIBERAD` (+) ↔ `PIX CARTAO` (−) ou `EST CRED` (−), **mesma conta**, mesmo valor, `|Δdata| ≤ 1`.
5. `Link` com um lado vazio ("contrapartida fora do que foi importado"), tipos `OwnTransfer` e `InvoicePayment`.
6. Botão "é meu" (Pix) e "sem contrapartida" (pagamento de fatura) criam esse vínculo `Confirmed` (`Origin = User`). O "é meu" também grava um **apelido** (trecho normalizado do nome da contraparte, por banco). Linhas com papel `Transfer`, sem vínculo, cujo trecho bate com um apelido entram como sugestão de lado vazio (`Suggested`, `Origin = Auto`); confirmar ou rejeitar como qualquer sugestão.
7. Reclassificação idempotente das linhas importadas pendentes e não editadas cuja regra atual diz `Normal`, executada pela reconciliação (confirmação de importação e botão).
8. Demo e documentação refletem tudo isso.

## Não-objetivos
- Regras de categoria, recorrências, aplicação/resgate, crédito consignado, estorno de compra.
- Usar o CPF mascarado do Nubank (pista adicional possível no futuro).
- Apelido para Itaú por correspondência parcial (só igualdade do trecho normalizado de 7 letras).
- Cadastro prévio de nomes.

## Anti-objetivos
- Esconder um gasto real: apelido nunca confirma sozinho; "é meu" é ato explícito e desfazível; Pix de mesmo trecho que o João recusou (vínculo `Rejected`) não volta para essa linha.
- Duas fontes de verdade para neutralidade: continua sendo só vínculo `Confirmed`.

## Constraints
Dinheiro exato (`numeric(18,2)`), isolamento por usuário, dados sintéticos, antiforgery em toda escrita, nomes de terceiros nunca como HTML, fixtures do demo geradas pelo backend e conferidas pela CI. Imagem de teste `postgres:17.11-trixie`.

## Abordagens consideradas
- **A. Vínculo com lado vazio (escolhida)**: um conceito só (vínculo), neutralidade derivada, desfazer = apagar; aprendizado por tabela de apelidos. Custo: `OutTransactionId`/`InTransactionId` viram opcionais.
- B. Marca booleana na linha: simples, mas pode contradizer um vínculo e quebra "neutro é derivado".
- C. Novo estado `Neutro`: perde o rastro do par e duplica o que o vínculo diz.

## Design

### Classificação
`NubankRules.PendingPrefixes` perde "transferencia enviada/recebida"; `ItauRules.CheckingPrefixes` muda `PIX TRANSF` e `PAG BOLETO` para `Normal`. As demais entradas ficam. `PairingRules` continua dizendo o papel pelo texto, independentemente do estado.

### Papéis e pareamento (`LinkMatcher`)
- Novos papéis: `PixCreditCredit` (`CREDITO LIBERAD`, valor > 0) e `PixCreditDebit` (`PIX CARTAO` ou `EST CRED`, valor < 0), só para `ItauChecking`.
- Novo `LinkKind.PixCredit`. Regra de compatibilidade do tipo: um `PixCreditCredit` com um `PixCreditDebit`, **mesma conta** (a regra "contas diferentes" não vale para este tipo), `|Δdata| ≤ PixCreditWindowDays = 1`; auto-confirma com componente de um par, senão `Suggested`. Estornos repetidos no mesmo dia são tratados pelo grafo (vários pares de mesmo valor ⇒ sugestões).
- `InvoicePayment` não muda. `OwnTransfer` ganha a coerência de nome para auto-confirmar (objetivo 2b): `PairingCandidate` ganha `NameKey` (`string?`, trecho normalizado da contraparte, extraído por `PairingRules.CounterpartyKey`) e o matcher aplica o critério de prefixo.
- `PixCredit*` e papéis de fatura: candidatas só se `Pendente`. `Transfer`: qualquer estado. Linhas ocultas e com vínculo **de dois lados** `Confirmed` ficam fora.
- **Contrapartida que chega depois de um "é meu" / "sem contrapartida"**: a linha com vínculo de lado vazio `Confirmed` continua candidata. Se o matcher a pareia com outra linha: par `AutoConfirm` ⇒ o vínculo de lado vazio é apagado e o par é criado `Confirmed`; par `Suggested` ⇒ o vínculo de lado vazio permanece e o par entra como sugestão (confirmar o par apaga os vínculos de lado vazio das duas linhas). Assim a outra ponta nunca fica contando como receita sem aviso.

### Vínculo com lado vazio
- `Link.OutTransactionId` e `InTransactionId` passam a `Guid?`; exatamente um deles é nulo nos vínculos de lado vazio (o lado preenchido segue o sinal: saída = valor negativo, entrada = positivo). `LinkKind` para estes: `OwnTransfer` ou `InvoicePayment` (nunca `PixCredit`). Migração `S3bOpenLinks` só altera as duas colunas para anuláveis, troca o índice único `(Out, In)` por um que ignora nulos e adiciona índices únicos parciais para evitar dois vínculos `Confirmed` de lado vazio na mesma linha (já cobertos pelos índices parciais por coluna) e dois vínculos abertos (Suggested) iguais.
- Nova tabela `OwnAliases`: `Id`, `UserId`, `Fragment` (texto normalizado, ≥ 7 caracteres), `CreatedAt`. Único `(UserId, Fragment)`. Não há coluna de banco: o apelido vale entre bancos.
- Trecho do nome: Itaú = memo da linha `PIX TRANSF` sem o prefixo e sem o sufixo `\s\d{2}\s\d{2}$`, normalizado (maiúsculas, sem acento, espaços colapsados), exatamente o que sobra (7 caracteres); Nubank = nome da contraparte já extraído por `NubankRules.ExtractCounterparty`, normalizado. Se o trecho ficar vazio ou tiver menos de 7 caracteres, a linha não gera apelido. **Coincidência de apelido** = um dos dois textos é prefixo do outro (o mais curto com ≥ 7 caracteres), por exemplo `MARIA L` (Itaú) e `MARIA LUISA SANTOS` (Nubank). A colisão com outra pessoa de mesmo início é aceita porque só gera sugestão.
- `LinkService`:
  - `MarkOwnAsync(transactionId, ct)`: linha com papel `Transfer` e sem vínculo `Confirmed`; cria vínculo de lado vazio `Confirmed` (`OwnTransfer`, `User`), grava apelido (idempotente), apaga sugestões abertas da linha. Se a linha tinha um `Rejected` de lado vazio, ele é apagado.
  - `MarkNoCounterpartAsync(transactionId, ct)`: linha com papel de fatura, sem vínculo `Confirmed`; cria vínculo de lado vazio `Confirmed` (`InvoicePayment`, `User`), sem apelido.
  - `ReconcileAsync`: depois do pareamento, para cada linha candidata `Transfer` ainda sem vínculo `Confirmed`, sem sugestão aberta e sem `Rejected` de lado vazio dela, se o trecho do nome coincide com um apelido ⇒ cria vínculo de lado vazio `Suggested` (`Origin = Auto`). O pareamento tem prioridade (um par `Suggested` ou `Confirmed` impede a sugestão de lado vazio).
  - `ConfirmAsync`/`RejectAsync`/`UndoAsync` valem também para vínculos de lado vazio. Desfazer (`DELETE`) de um de lado vazio `Confirmed` o **marca `Rejected`** (como hoje) e o `Reconcile` não o recria para aquela linha.
  - O apelido não é apagado quando o João desfaz um vínculo (só deixa de sugerir aquela linha). Não há tela para apagar apelidos nesta fatia (anotado como pendência).
  - `InvalidateAsync`: editar **valor ou data** apaga os vínculos de par `Suggested`/`Confirmed` da linha e as sugestões automáticas de lado vazio, mas **preserva** vínculos de lado vazio `Confirmed` de origem `User` (o "é meu"/"sem contrapartida" fala da identidade da contraparte, não do valor). Ocultar/excluir a linha apaga todos os vínculos dela. Mudar o estado para `Normal` de uma linha com papel de fatura ou de Pix no crédito apaga os vínculos dela (opt-out); para linhas `Transfer` o estado não invalida nada.
- Reclassificação: dentro de `ReconcileAsync`, antes do pareamento, linhas importadas (`Origin = Imported`, com lote), não editadas (`!EditedByUser`), `Pendente`, cuja regra atual (`ClassifyChecking`/`ClassifyInvoice`/`NubankRules.Classify`, conforme `ImportBatch.Source`) devolve `Normal` passam a `Normal`. A resposta ganha `Reclassified`.

### API
- `GET /api/links` e `LinkResponse`: `Out`/`In` anuláveis; novo campo `External: bool` (true quando um lado é vazio) e `LinkKind` inclui `PixCredit`.
- `POST /api/transactions/{id}/own-transfer` → `LinkResponse` (cria o vínculo `Confirmed` de lado vazio e o apelido). `POST /api/transactions/{id}/no-counterpart` → `LinkResponse`. Ambos com antiforgery; 404 se a linha não é do usuário; 422 `not_applicable` se a linha não tem papel adequado (convenção dos imports); 409 se já tem vínculo `Confirmed`.
- `POST /api/links/reconcile` → `{ linked, suggested, reclassified }`.
- `TransactionResponse` ganha `Pairing: "OwnTransfer" | "InvoicePayment" | "PixCredit" | null` (o que a linha pode virar) calculado por `PairingRules`; o front usa para mostrar "é meu" / "sem contrapartida".
- Resumo mensal e lista de pendentes: a exclusão de linhas com vínculo `Confirmed` vale para **qualquer estado** (resumo: nem receita nem despesa; fila: nem aparece).

### Interface
- Lançamentos: linhas com `Pairing = OwnTransfer` e sem vínculo mostram ação "É meu"; linhas pendentes com `Pairing = InvoicePayment` mostram "Sem contrapartida" (Pendências e Lançamentos). Selos: "Transferência", "Pagamento de fatura", "Pix no crédito"; vínculo de lado vazio mostra "Transferência (conta fora do app)" ou "Pagamento de fatura (sem extrato)".
- Pendências: botão "Reconciliar pendências" (mostra quantas linhas ligou/sugeriu/reclassificou). Sugestões de lado vazio aparecem como cartão de uma linha só com Confirmar/Rejeitar.
- Demo: semente com um Pix para si mesmo de lado vazio confirmado, um par `PixCredit`, uma sugestão por apelido e um pagamento de fatura sem contrapartida; ações de escrita continuam recusadas com `demo_read_only`.

### Testes
- Domínio: `LinkMatcher` com `PixCredit` (mesma conta, estorno, vários pares, Δ), `OwnTransfer` com linhas Normais.
- Regras: tabela de classificação nova (Itaú e Nubank), `PairingRules` com `CREDITO LIBERAD`, `PIX CARTAO`, `EST CRED`, extração do trecho do nome (7 letras com sufixo; caixa e acentos), contrato de sinais.
- Integração (Postgres real): reclassificação (editadas intocadas; idempotente); neutralidade de linhas Normais vinculadas no resumo; `own-transfer` cria vínculo/apelido; sugestão por apelido só como `Suggested`; rejeitar impede a volta; `no-counterpart`; `PixCredit` pela importação em duas ordens; desfazer lote apaga vínculos; migração sobre base com dados da S3(a); concorrência de `own-transfer` sobre a mesma linha (um `Confirmed`); isolamento entre usuários.
- Front: store, ações "É meu"/"Sem contrapartida", botão Reconciliar, selos; e2e do demo.

## Decisões minhas (para o João conferir)
- `PixCreditWindowDays = 1` (nas amostras todos os pares têm Δ = 0).
- Apelido por prefixo mútuo (≥ 7 caracteres), entre bancos, sempre como sugestão.
- Coerência de nome para auto-confirmar `OwnTransfer`; sem nome ⇒ sugestão (custo: pares legítimos sem nome extraível exigem um clique).
- Papéis de fatura e de Pix no crédito só candidatos quando `Pendente`; `Transfer` em qualquer estado.
- Editar valor/data preserva o "é meu" do usuário.
- "Sem contrapartida" não cria apelido.
- `DELETE` de vínculo de lado vazio marca `Rejected` (a linha não volta a ser sugerida).
- Reclassificar nunca toca linhas editadas ou manuais.
- Não há tela para gerenciar apelidos nesta fatia.

## Respostas do builder check
- **Pagamento e recebimento de terceiros de mesmo valor** (aluguel × freela no mesmo dia): resolvido pelo objetivo 2b (coerência de nome); sem coerência, só sugestão.
- **Contrapartida que chega depois do "é meu"**: resolvido pela regra "continua candidata" acima (e o apelido agora vale entre bancos).
- **Pix no crédito marcado como Normal**: os papéis `PixCredit*` seguem a regra da fatura (só `Pendente`); mudar para Normal apaga o vínculo e não é recriado (opt-out).
- **Sequência do Pix no crédito cancelado**: nas amostras é `CREDITO LIBERAD` (+X) e `EST CRED` (−X) no mesmo dia, sem `PIX CARTAO` (2/2); o par simples cobre. Se algum dia vierem as três linhas, o grafo gera sugestões e o João resolve.
- **Pix no crédito sem a fatura do cartão importada**: o par some da fila sem registro do gasto até a fatura ser importada. É a consequência assumida da spec pai; documentada no ADR. O João pode optar por contar mudando as linhas para `Normal`.
- **Edição de valor/data de vínculo `User` de lado vazio**: preservado (acima).

## O que fica para o construtor
Nomes de arquivos/classes, formato exato dos DTOs de lista, textos de i18n, estrutura exata dos índices da migração `S3bOpenLinks`, ordem das tarefas.
