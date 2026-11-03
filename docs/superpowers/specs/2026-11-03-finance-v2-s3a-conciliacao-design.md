# S3(a) — Conciliação: pagamento de fatura e transferência entre contas próprias

Spec pai (vinculante): `2026-11-02-finance-v2-backend-design.md`, seção "Conciliação". Este documento detalha só a primeira parte da S3. S3(b) (Pix parcelado, aplicação/resgate) e S3(c) (regras de categoria, recorrências) ficam para depois.

## Intenção e por quê

Hoje as linhas que são "dinheiro mudando de lugar" (pagamento de fatura, Pix entre contas do próprio João) entram como `PendingClassification` pelo prefixo da descrição e ficam numa fila que ninguém resolve: fora do resumo mensal, mas contadas em "pendentes" e sem relação conhecida com a outra ponta. O João não consegue distinguir "Pix para mim mesmo" de "Pix para terceiro" na fila, e a fila cresce a cada importação.

A S3(a) liga as duas pontas de cada movimento interno (um **vínculo**), de forma automática quando não há dúvida e com sugestão quando há. Linhas com vínculo confirmado saem da fila e passam a ser *neutras*. O João vê o que foi ligado e pode desfazer.

Nota de honestidade: o resumo mensal já **não** dobrava essas linhas (pendentes ficam fora de receita/despesa). O ganho da S3(a) é: a fila de pendências passa a conter só o que realmente exige decisão, os movimentos internos ficam identificados e auditáveis, e a base fica pronta para S3(b)/(c).

## Decisões do João (brainstorm de 2026-11-03)
- Começar por pagamento de fatura + transferência entre contas próprias.
- Pares sem ambiguidade se confirmam **sozinhos**, com opção de desfazer.
- Caminho A: pareamento na confirmação da importação, com tabela de vínculos; pares ambíguos entram como `sugerido`.
- Parcial, a maior, rotativo e 1:N ficam como pendência (nunca viram gasto em silêncio).

## Objetivos
1. Tabela `Link` (par de lançamentos) com tipo e estado, vínculos criados na confirmação de cada importação e por um endpoint de reconciliação idempotente.
2. Pareamento exato 1:1: valor igual, sinais opostos, janela de datas, contas e tipos compatíveis.
3. Confirmação automática quando o par é inequívoco; `sugerido` quando há mais de um candidato.
4. Confirmar, rejeitar e desfazer vínculos pela API e pela tela de pendências.
5. Linha com vínculo confirmado sai da contagem de pendentes e é identificada como neutra na lista de lançamentos.
6. Desfazer uma importação remove os vínculos das linhas removidas; reimportar relê e religa.
7. O demo estático mostra a tela com exemplos fixos gerados pelo backend.

## Não-objetivos
- Pix para terceiros, boletos, aplicação/resgate, crédito consignado, estorno, Pix no crédito: continuam `PendingClassification` sem vínculo (S3(b)/(c)). Pix para terceiro sem par fica pendente como hoje.
- Casamento 1:N, pagamento parcial/a maior, rotativo, pagamento de fatura por outro banco.
- Vínculo criado à mão entre duas linhas escolhidas pelo usuário.
- Usar documento/nome da contraparte para casar (o OFX do Nubank mascara o documento).
- Qualquer mudança em saldos: vínculo não altera saldo de conta, checkpoint nem patrimônio.

## Anti-objetivos
- Um vínculo errado esconder um gasto real em silêncio. Por isso só casa o exato, o auto-confirmado é sempre desfazível e visível, e qualquer dúvida vira `sugerido`.
- Linha neutra "sumir": ela continua na lista de lançamentos e nos saldos.

## Restrições
- Dinheiro como `decimal` exato (`numeric(18,2)`), igualdade exata de valor, sem tolerância.
- Isolamento por usuário igual ao resto (`IUserOwned`, filtro global).
- Dados reais nunca em testes/fixtures; só sintéticos.
- Fixtures do demo continuam geradas pelo backend e checadas pela CI.

## Abordagens consideradas
- **A. Pareamento na confirmação + tabela de vínculos (escolhida pelo João).** Resultado certo logo após importar; vínculo tem estado e histórico (rejeitado não volta).
- B. Passo separado/job: resumo errado até rodar. Descartada.
- C. Pares calculados na leitura, sem persistir: sem onde guardar confirmado/rejeitado. Descartada.

## Design

### Modelo
`Link` (por usuário): `Id`, `UserId`, `Kind` (`InvoicePayment` | `OwnTransfer`), `State` (`Suggested` | `Confirmed` | `Rejected`), `OutTransactionId` (a linha de valor negativo), `InTransactionId` (a de valor positivo), `Origin` (`Auto` | `User`), `CreatedAt`, `DecidedAt?`. FKs para `Transactions` com `ON DELETE CASCADE` (desfazer lote usa `ExecuteDelete` e some com os vínculos). Índices: único `(OutTransactionId, InTransactionId)`; único parcial em `OutTransactionId` e em `InTransactionId` onde `State = 'Confirmed'` (uma linha tem no máximo um vínculo confirmado); índice por `(UserId, State)`.

A neutralidade é **derivada**: linha neutra = tem vínculo `Confirmed`. Não há campo novo em `Transaction`; `State` continua o que o importador/usuário definiu.

### Candidatas e tipos
Candidata = linha visível (`!IsDeleted`), importada (com lote), em estado `PendingClassification`, sem vínculo `Confirmed`, cuja descrição normalizada tem um destes prefixos para a fonte do lote (`ImportBatch.Source`), usando as mesmas regras de normalização dos leitores (`SourceKeys.NormalizeText` / `NubankRules.Normalize`) numa classe única `PairingRules` (Infrastructure/Importing):

| Fonte | Prefixo | Papel |
|---|---|---|
| NubankChecking | "pagamento de fatura" | InvoicePayment, lado conta |
| NubankCard | "pagamento recebido" | InvoicePayment, lado cartão |
| NubankChecking | "transferencia enviada", "transferencia recebida" | OwnTransfer |
| ItauChecking | "FATURA PAGA" | InvoicePayment, lado conta |
| ItauCard | primeiro token `PAGAMENTO` com valor positivo (mesma regra de `ItauRules.ClassifyInvoice`) | InvoicePayment, lado cartão |
| ItauChecking | "PIX TRANSF" | OwnTransfer |

Combinações válidas: `InvoicePayment` = conta ↔ cartão **do mesmo banco** (NubankChecking↔NubankCard, ItauChecking↔ItauCard). `OwnTransfer` = duas contas `Checking` diferentes (bancos iguais ou diferentes). Linhas manuais não são candidatas.

### Matcher (função pura no Domínio, `LinkMatcher`)
Entrada: candidatas (id, conta, tipo de conta, banco, data, valor, papel), conjunto de pares já `Rejected`, e vínculos `Confirmed` existentes (para excluir as linhas). Pares possíveis: papéis compatíveis (acima), `Amount` de um = `-Amount` do outro (exato), `|Δdata| ≤ 3` dias, contas diferentes, par não rejeitado. Isso forma um grafo bipartido (saídas × entradas). Para cada componente conexo:
- componente com exatamente um par (a saída tem 1 candidata e a entrada tem 1 candidata) → vínculo `Confirmed`, `Origin = Auto`, **exceto `OwnTransfer` com `|Δdata| > 1`**, que entra `Suggested`: Pix é instantâneo, e valor+data não distinguem Pix para si mesmo de Pix para terceiro de mesmo valor, então só a coincidência de até 1 dia se confirma sozinha. A unicidade é medida no grafo inteiro de 3 dias (um terceiro candidato dentro da janela já torna o par ambíguo);
- caso contrário → um vínculo `Suggested` por par possível.

Determinismo: ordenação por data, depois id; mesma entrada dá a mesma saída. Mandar a mesma entrada duas vezes não duplica (o serviço ignora pares já existentes).

### Serviço (`LinkService`, Infrastructure)
- `ReconcileAsync(userId)`: carrega candidatas sem vínculo `Confirmed`, os `Rejected` e os `Suggested` existentes; **substitui os `Suggested`** pela saída do matcher (sugestões são recalculadas, nunca acumulam obsoletas) e insere os `Confirmed` novos. Roda sob `pg_advisory_xact_lock` por usuário, para duas confirmações simultâneas não pegarem a mesma linha (o índice único parcial é a segunda trava).
- Na `ConfirmAsync` da importação: chama `ReconcileAsync` dentro da mesma transação, depois de inserir linhas e checkpoint. O resultado da confirmação ganha `Linked` (confirmados novos) e `Suggested` (sugestões abertas).
- `ConfirmLink(id)`: só `Suggested`; torna `Confirmed` (falha com 409 se alguma ponta já tem outro `Confirmed`), remove os outros `Suggested` das mesmas pontas.
- `RejectLink(id)`: `Suggested` → `Rejected`.
- `UndoLink(id)`: `Confirmed` → `Rejected` (o matcher não recria; as linhas voltam à fila).
- Editar uma linha: alterar valor, data ou estado (para `Normal`) de uma linha com vínculo `Confirmed` ou `Suggested` apaga os vínculos dela (a premissa do par deixou de valer); ocultar/excluir a linha também. Depois, `ReconcileAsync`.

### API (OpenAPI, cliente TypeScript regenerado)
- `GET /api/links?state=Suggested|Confirmed|Rejected` → lista com as duas linhas resumidas (data, conta, descrição, valor) e kind/state/origin; paginado.
- `POST /api/links/{id}/confirm`, `POST /api/links/{id}/reject`, `DELETE /api/links/{id}` (desfazer confirmado). Todos com antiforgery, 404 para vínculo de outro usuário, 409 em conflito.
- `POST /api/links/reconcile` → `{ linked, suggested }` (idempotente).
- `TransactionResponse` ganha `Link?` (`id`, `kind`, `state`, `counterpartTransactionId`).
- `GET /api/summary`: `Pending.Count/Total` não contam linhas com vínculo `Confirmed`. Receita/despesa/saldos inalterados.

### Interface (real e demo)
- Página Pendências (`Pending.vue`) ganha, acima da lista atual: **Sugestões** (pares com Confirmar/Rejeitar) e **Vinculados** (pares recentes com Desfazer). A lista atual passa a mostrar só pendentes sem vínculo.
- Tabela de lançamentos: selo "Transferência" / "Pagamento de fatura" nas linhas com vínculo `Confirmed`.
- Importar: o resultado da confirmação diz quantos pares foram vinculados e quantas sugestões ficaram.
- Demo: a semente sintética (`DemoSeed`) inclui um par confirmado de cada tipo e uma sugestão; o `StaticApi` serve `links` das fixtures (somente leitura; ações de escrita continuam recusadas com `demo_read_only`).
- i18n pt-BR/en-US; dinheiro sempre via `Money.vue`; textos de terceiros nunca como HTML.

### Testes
- Domínio: `LinkMatcher` com testes de exemplo e de propriedade (FsCheck, se já usado nos importadores; senão testes parametrizados): par único confirma; 2 saídas × 1 entrada sugere ambos; 2×2 idênticos sugere todos; fora da janela não casa; valor diferente não casa; mesma conta não casa; rejeitado não volta; determinismo; idempotência.
- Integração (Postgres real): pagamento de fatura Nubank e Itaú pela confirmação de duas importações em ordem diferente; transferência Itaú↔Nubank; desfazer lote remove vínculos; confirmar/rejeitar/desfazer; isolamento entre usuários; confirmações concorrentes sem duplicar vínculo; editar linha apaga vínculo; resumo não conta vinculadas; migração aplica sobre base com dados da S2.
- Front: stores e páginas (Sugestões, Vinculados, selo), e2e do demo sem nenhuma requisição a `/api`.
- Contrato: snapshot do OpenAPI e cliente regenerado conferidos pela CI; fixtures do demo conferidas byte a byte.

### Entregáveis de documentação
ADR 0007 (vínculos e neutralidade derivada, pareamento exato e janela), README (seção de conciliação), diário `away-log.md`.

## Decisões minhas (para o João conferir)
- Janela de **3 dias** (cobre fim de semana e feriado); constante única, fácil de mudar.
- **Sugestões recalculadas** a cada reconciliação em vez de acumuladas (evita sugestão obsoleta).
- `UndoLink` marca `Rejected` (sem memória do desfeito o matcher refaria o vínculo na próxima importação).
- Editar valor/data/estado apaga vínculos da linha.
- Mesmo banco exigido para fatura; pagamento de fatura feito de outro banco fica pendente.
- Na confirmação das duas pontas 2×2 idênticas, o João confirma manualmente (custo baixo, caso raro).

## Respostas do builder check
- **Falso positivo de Pix para terceiro:** tratado acima (auto-confirmação de `OwnTransfer` só com Δ ≤ 1 dia; senão `Suggested`). Custo se errado: um Pix a terceiro de valor igual a um recebimento no dia seguinte, sem outro candidato, some da fila como "Transferência"; fica visível em Vinculados e se desfaz com um clique. Alternativa mais conservadora (nunca auto-confirmar `OwnTransfer`) é mudar uma condição no matcher.
- **Reimportar fatura:** o código não apaga linhas ao reimportar (a identidade é a chave+ordinal; as ausentes só geram aviso). O único apagamento real é o desfazer de lote (cascata nos vínculos). Ocultar/editar linha passa por `TransactionEndpoints`; a invalidação de vínculos mora em `LinkService.InvalidateAsync(transactionId)`, chamada pelos endpoints de edição e exclusão, não espalhada.
- **Memória de `Rejected` perdida na cascata:** aceito. Desfazer um lote e reimportar cria linhas novas (ids novos), então a rejeição não tem como sobreviver sem chavear por SourceKey; vale a simplicidade. Registrado no ADR 0007.
- **Sinais:** conta de cartão do Nubank: "pagamento recebido" é positivo; Itaú: pagamento é positivo após a inversão do leitor; lado conta é negativo nos dois. O plano inclui um teste de contrato com fixtures sintéticas dos quatro leitores para travar isso.
- **Neutralidade e consumidores de `PendingClassification`:** só `MonthlySummaryCalculator` (contagem de pendentes) e o filtro `state=PendingClassification` da lista. Ambos passam a excluir linhas com vínculo `Confirmed` (predicado único em `QueryExtensions`, `WithoutConfirmedLink`). Saldos e totais de receita/despesa não mudam.

## O que fica para o construtor
Nomes de arquivos/classes, formato exato dos DTOs de lista, paginação padrão, textos de i18n, ordem das tarefas, estrutura das migrações (uma migração `S3aLinks`), e se o conjunto de prefixos mora numa tabela ou numa lista estática (lista estática; ver tabela acima).
