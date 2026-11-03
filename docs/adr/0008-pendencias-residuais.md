# ADR 0008: Pendências residuais — Pix e boleto como Normal, vínculos de lado vazio, apelidos

- Status: aceita
- Data: 2026-11-03

## Contexto
Depois da S3(a), a fila de Pendências ainda guardava Pix para terceiros e boletos (que são gastos ou receitas normais), Pix para a própria conta, Pix no crédito (`CREDITO LIBERAD` + `PIX CARTAO`/`EST CRED`) e pagamentos de fatura sem a outra ponta importada (extrato do mês anterior ausente, conta fora do app).

## Decisão
- **Pix e boleto entram como `Normal`** (Itaú `PIX TRANSF`, `PAG BOLETO`; Nubank "Transferência enviada/recebida"). Continuam pendentes: pagamento de fatura, `PIX CARTAO`, `CREDITO LIBERAD`, `EST CRED`, "pix no crédito" e demais classes da S3(a).
- **Neutralidade em qualquer estado**: uma linha `Normal` ou `Pendente` com vínculo `Confirmed` (de um ou dois lados) sai de receita, despesa, categorias e fila; saldos não mudam.
- **Pareamento**: linhas com papel `Transfer` são candidatas em qualquer estado; papéis de fatura e de Pix no crédito só enquanto `Pendente` (quem põe uma delas em `Normal` está dizendo que ela conta, e a edição apaga os vínculos).
- **Coerência de nome para auto-confirmar `OwnTransfer`**: além de par único e |Δdata| ≤ 1, o trecho do nome da contraparte tem que ser prefixo do outro (normalizados, o mais curto com ≥ 7 caracteres). Sem nome ou com nomes incoerentes o par é só sugestão. Protege contra aluguel × freela de mesmo valor virarem "transferência".
- **`PixCredit`**: novo `LinkKind`, mesma conta, janela de 1 dia; fecha `CREDITO LIBERAD` (+) com `PIX CARTAO` ou `EST CRED` (−).
- **Vínculo de lado vazio** (`Out` ou `In` nulos, `External` na API): "É meu" (`OwnTransfer`) e "Sem contrapartida" (`InvoicePayment`, só linha pendente) criam vínculo `Confirmed`/`User`. Se a contraparte real chegar depois: par `AutoConfirm` apaga o vínculo vazio e vira o vínculo da linha; par `Suggested` convive com ele e confirmar o par apaga os vazios. Editar valor/data preserva o vínculo `User` de lado vazio; ocultar apaga tudo.
- **Apelidos (`OwnAlias`)**: "É meu" grava o trecho normalizado (≥ 7 caracteres) do nome. Linhas `Transfer` cujo nome coincide (prefixo mútuo, entre bancos) recebem **sugestão** de lado vazio, nunca confirmação automática. Não há tela para apagar apelidos.
- **Reclassificação**: `POST /api/links/reconcile` (botão "Reconciliar pendências") também move para `Normal` linhas importadas, pendentes e não editadas que a regra atual classifica como Normal; é idempotente e nunca toca linha editada ou manual.
- **Rebaixamento de pares legados**: a cada reconcile, pares `Confirmed`/`Auto`/`OwnTransfer` (confirmados pela regra antiga da S3(a), sem checar nomes) cujas contrapartes não coincidem são apagados e recriados como `Suggested` pelo fluxo normal; as duas linhas voltam a contar. Vínculos de origem `User` nunca são tocados; depois do rebaixamento nada mais casa, então é idempotente.
- **Marcação não aplicável** responde 422 `not_applicable` (a spec dizia 409; 422 segue a convenção dos imports). 409 fica para linha que já tem vínculo `Confirmed`.
- **Override do preview** (estado escolhido diferente da regra) grava `EditedByUser = true`, para a reclassificação não desfazer a escolha.

## Consequências
- Um par de Pix para si mesmo, com nomes coerentes e até 1 dia de diferença, some de receita/despesa sozinho (desfazível).
- Pix no crédito e "sem contrapartida" saem da fila sem registro do gasto até a fatura do cartão ser importada; para contar antes, ponha as linhas em `Normal`.
- Colisão de apelido (mesmo início de nome) gera sugestão, não ação.
- Linhas com override de estado no import contam como "editadas" no aviso de desfazer lote, e a futura S3(c) deve lembrar disso.

- Limitação conhecida: editar o valor de modo que o sinal inverta mantém o vínculo `User` de lado vazio no lado antigo (sem efeito em dinheiro).
- O upgrade automático para par apaga o vínculo vazio do usuário; desfazer o par depois deixa a linha contada, sem a marca anterior.
- A primeira importação após o deploy reclassifica linhas Pix pendentes antigas sem informar a contagem.
- "Reconciliar pendências" sempre informa as sugestões recalculadas (não apenas as novas).

## Alternativas consideradas
- Campo ou estado novo "interna" em `Transaction`: duplica o fato do vínculo.
- Aprender apelido automaticamente: erra com homônimos; só sugestão com clique.
- Auto-confirmar `OwnTransfer` só por valor/data (S3(a)): arriscado agora que Pix é `Normal` por padrão.
