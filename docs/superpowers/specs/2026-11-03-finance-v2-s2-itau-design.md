# Finance v2 — S2: importadores do Itaú (design)

Pai: `2026-11-02-finance-v2-backend-design.md` (binding). Esta fatia estende o importador da S1a (`2026-11-02-finance-v2-s1a-nucleo-nubank-design.md`) e a tela Importar da S1b (`2026-11-02-finance-v2-s1b-interface-design.md`). Feita em **modo ausente** (`docs/away-mode.md`): as decisões abaixo foram tomadas pelo Claude com a recomendação de dois conselheiros independentes (enfoques "simplicidade" e "correção/segurança") e estão no `docs/away-log.md`.

## Intenção
O banco principal do João é o Itaú. O app só serve ao uso diário quando o extrato da conta e a fatura do cartão do Itaú entram sem digitação, sem duplicar nada e sem contar em silêncio como despesa o que é pagamento ou transferência. A S2 entrega os dois leitores (conta em OFX, fatura em xlsx), a identidade de importação adequada aos formatos que não têm ID confiável, as conferências e a tela de importação aceitando os dois formatos.

## Fatos observados nos arquivos reais (somente estrutura; nenhum dado entra no repositório)
- **Itaú conta (OFX):** SGML, `CHARSET:1252`, folhas sem tag de fechamento (agregados fechados), `BANKID` 0341, `TRNTYPE` CREDIT/DEBIT, `FITID` numérico de 11 dígitos que começa com `AAMMDD` mais sequência (único no arquivo, mas posicional), `CHECKNUM` repete o FITID, `MEMO` curto em maiúsculas, `DTPOSTED`/`DTSTART`/`DTEND`/`DTASOF` com sufixo de fuso (`[-3:BRT]`), `LEDGERBAL` presente. Há **grupos de lançamentos idênticos no mesmo dia** (mesma data, valor e memo; até 4). Prefixos de memo vistos (os dois primeiros termos): `PIX QRS` (débito), `PIX TRANSF` (débito e crédito), `PIX CARTAO` (débito), `REND PAGO` (crédito), `CREDITO LIBERAD` (crédito), `CREDITO CONSIGNADO` (crédito), `FATURA PAGA` (débito), `REMUNERACAO SALARIO` (crédito), `EST CRED` (débito), `PAG BOLETO` (débito).
- **Itaú fatura (xlsx):** uma planilha, zip com logo (`xl/media`) e `drawing`; blocos: cabeçalho do titular (ignorar), linha "Cartão | Valor | Vencimento" seguida de uma linha com produto, **total da fatura (número)** e **vencimento (serial do Excel)**, linha "Lançamentos", linha de cabeçalho de colunas (Data, Lançamento, parcelamento, Valor, tipo de titular, nome, tipo do cartão, final do cartão) e uma linha por lançamento; rodapé com **célula de total por fórmula** e textos. Cobrança **positiva**; pagamento da fatura anterior e estornos **negativos**; a linha de pagamento aparece na lista, com data igual ao vencimento anterior (≈31 dias antes). **Em duas faturas reais, total declarado = soma das linhas que não são pagamento (inclui estornos e encargos)**. **Parcelas vêm com a data da compra original** (de 14 a 290 dias antes do vencimento), enquanto compras à vista ficam 24–38 dias antes.

## Objetivos
- Leitor do **OFX do Itaú** (perfil de banco sobre um leitor OFX tolerante comum, detectado pelo `BANKID` normalizado: 260 = Nubank, 341 = Itaú; outro = `unsupported_bank`).
- Leitor do **xlsx da fatura do Itaú** (detectado pelo conteúdo, não pela extensão), com todas as verificações de segurança de zip/xlsx.
- Identidade por **chave de conteúdo + ordinal** (multiconjunto) para Itaú conta e fatura; refatorar o pipeline para a identidade ser o par (chave, ordinal) de ponta a ponta.
- Conferências e avisos novos (lista em "Conferências").
- Metadados por lançamento: parcela (número/total), final e tipo do cartão, data da compra e data da fatura.
- Tela Importar aceita `.ofx` e `.xlsx`; prévia mostra parcela/cartão; textos novos pt-BR e en-US.
- Testes com fixtures **sintéticas** geradas por código; suíte local opcional contra `finance-samples/` (ignorada na CI, sem imprimir conteúdo).

## Não-objetivos
Grupos de parcelas, projeção de parcelas futuras e tela "comprometido" (S3/S4); conciliação, vínculos, regras de categoria (S3); checkpoint do cartão e patrimônio (S3/S4); importador de PDF; substituição automática de fatura; qualquer dado real em fixtures; aviso que liga "FATURA PAGA" (conta) ao "PAGAMENTO" (fatura) (S3).

## Anti-objetivos (falha mesmo se "funcionar")
- Duplicar lançamento em reimportação, em exportações sobrepostas ou em corrida; **perder lançamento legítimo** (dois idênticos no mesmo dia viram um só).
- Contar em silêncio como despesa/receita: pagamento de fatura, transferência, crédito de empréstimo/Pix parcelado, estorno de crédito, aplicação/resgate.
- Avaliar fórmula, seguir link externo, abrir macro, estourar memória com zip malicioso.
- Datar parcela antiga no mês da compra original no resumo.
- Logar descrição, valor, nome, ACCTID ou trecho de arquivo; mensagens de erro citam linha/tipo, nunca conteúdo.

## Restrições
- Valem todas as do pai e da S1a (.NET 10, EF Core, `numeric(18,2)`, `DateOnly` literal do arquivo sem UTC, antiforgery, upload ≤ 5 MB, um arquivo por requisição, DTD proibido, fixtures sintéticas, `.gitattributes` com `*.ofx -text`, Testcontainers).
- Convenção de sinal normalizada no leitor: conta (entrada +/saída −); cartão (cobrança −/pagamento +). **O xlsx do Itaú é invertido no leitor** (cobrança positiva vira negativa; pagamento negativo vira positivo; estorno negativo vira positivo e é `Normal`).
- Registrar `CodePagesEncodingProvider` para CP1252; datas do OFX tomadas do texto (`AAAAMMDD…`), ignorando o sufixo de fuso; números com cultura invariante.
- Biblioteca xlsx: **DocumentFormat.OpenXml (MIT), leitura SAX**, após pré-validação própria do zip. Sem EPPlus 5+. O construtor pode trocar por ExcelDataReader (`CreateOpenXmlReader` explícito, nunca o detector automático) se justificar no relatório; ClosedXML é descartada (motor de cálculo).

## Abordagens consideradas
A. **Leitor OFX tolerante comum com perfis por banco + leitor xlsx separado, ambos atrás de uma interface de leitor** (escolhida): reaproveita o leitor da S1a, isola regras de identidade e classificação por perfil, a interface cobre OFX e xlsx. B. Um leitor completo por banco (duplica parsing e segurança). C. Normalizar tudo para CSV externo (exige passo manual). **Decisão: A.** Slicing: a S2 guarda os metadados de parcela mas **não** cria grupos/projeção (dependem de decisão de produto).

## Modelo
- `ImportSource`: acrescentar `ItauConta` (só conta `Corrente`) e `ItauCartao` (só conta `Cartao`), mesma validação fonte × tipo de conta da S1a (divergência = `422 account_type_mismatch`).
- **Lançamento**: acrescentar `InstallmentNumber` e `InstallmentTotal` (`int?`), `CardSuffix` (`string?`, 4 caracteres), `CardKind` (enum string nulo: `Physical`, `Virtual`, `VirtualRecurring`, `Other`). `DataCompra` e `DataFatura` (já existem) passam a ser preenchidas na fatura. Migration única da S2.
- **SourceKey** do Itaú = SHA-256 hex (64 chars) de componentes normalizados (para caber em `MaxSourceKey` e não vazar texto): conta = `itau-conta|AAAA-MM-DD|valor|memo`; fatura = `itau-fatura|AAAA-MM-DD(vencimento)|AAAA-MM-DD(data do arquivo)|valor|descrição|parcela|final do cartão`. Normalização de texto **congelada** (maiúsculas, sem acentos, espaços colapsados, trim) e coberta por teste de contrato; mudá-la exige migração.
- **Ordinal** = k-ésima ocorrência da chave no arquivo (0..n−1), calculado depois de qualquer filtro do leitor.
- **Lote**: o resumo guarda, para fatura, `declaredTotal`, `dueDate` e a conferência; para conta, o `LEDGERBAL` lido.

## Identidade e prévia (refatoração obrigatória do pipeline da S1a)
A S1a assume chave única por linha: `DistinctBy(FitId)` no confirm, classificações da prévia indexadas só por `SourceKey`, e a checagem "chave em outra conta". Na S2:
- A identidade da linha é **(SourceKey, Ordinal)** na prévia, nos `overrides` (o item ganha `ordinal`, padrão 0 — compatível com o Nubank, que tem sempre 0), no `DistinctBy` e na lista de "existentes".
- "Existente" = o par (chave, ordinal) já existe na conta (inclusive excluído logicamente); a prévia consulta **quais pares existem**, não quantas linhas (evita mentir quando um lote sobreposto foi desfeito e deixou buracos). O `INSERT … ON CONFLICT DO NOTHING` continua sendo o árbitro; ordenação determinística por (chave, ordinal).
- A checagem "chave existente em outra conta" vale para o Nubank (UUID) e **não** para chaves de conteúdo do Itaú.
- Reimportar o mesmo arquivo é no-op (todas existem); exportações sobrepostas inserem só o que falta. O hash do arquivo não é garantia para xlsx (timestamps do zip mudam); a identidade por linha é a defesa real.
- **Aviso `PossibleDuplicate`** (não bloqueia): linha nova com existente na mesma conta, **mesmo valor, data ±1 dia e mesmo prefixo de memo/descrição (primeiros 12 caracteres normalizados)**, mas chave diferente.

## Itaú conta (OFX)
- Cabeçalho `ENCODING`/`CHARSET:1252`; folhas sem fechamento; limites de campo/profundidade do leitor tolerante; um único `STMTRS`; datas tomadas do texto.
- `LEDGERBAL` → checkpoint **somente se nenhuma linha do arquivo tiver data posterior ao `DTASOF`**; senão aviso `LedgerBalanceBeforeLastLine` e sem checkpoint.
- Validação de `ACCTID`/agência contra o `externalIdentifier` da conta escolhida, igual ao Nubank (divergência = aviso bloqueante com override).
- **Classificação por texto** (normalizado, prefixo, fonte única de regras no perfil): **Pendente** = `FATURA PAGA`, `PIX TRANSF` (entrada e saída), `PIX CARTAO`, `PAG BOLETO`, `CREDITO LIBERAD`, `EST CRED`, `CREDITO CONSIGNADO`, `APLICACAO`, `RESGATE`. **Normal** = o restante (inclui `PIX QRS`, `REND PAGO`, `REMUNERACAO SALARIO`), com o aviso não bloqueante `UnknownLineKinds` com a contagem de linhas sem regra conhecida. Racional: o erro silencioso caro é a **despesa em dobro entre conta e cartão** (boleto/Pix pagando fatura, Pix no cartão) ou **receita falsa** (crédito de empréstimo/Pix parcelado). O usuário reclassifica com um clique (S1b); a S3 substitui as regras. Isto mantém o padrão da S1a (desconhecido = Normal), em vez de "pendências" do pai.

## Itaú fatura (xlsx)
- **Pré-validação do zip (antes de qualquer parser):** assinatura `PK` (contêiner OLE/CFB, xlsx criptografado ou `.xls` = erro claro `not_xlsx`/`encrypted_xlsx`); no máximo ~200 entradas; descompactado total ≤ ~50 MB e razão de compressão por entrada ≤ 100, verificados também lendo com contador; nomes de entrada com `..`/caminho absoluto rejeitados; rejeitar `vbaProject.bin`, content type `macroEnabled`, `externalLinks/`, `connections.xml`, `embeddings/`, `activeX/` e relações com `TargetMode="External"`; mídia/drawings nunca lidos.
- **Leitura:** primeira planilha visível; limites de linhas, colunas, tamanho de célula e de `sharedStrings`; XML com `DtdProcessing.Prohibit` e `XmlResolver = null`; **valor em cache** das células, nunca avaliar; **fórmula só é aceita na célula de total do rodapé** (ignorada); fórmula em célula de lançamento = rejeitar; honrar `date1904` e o ano bissexto fantasma de 1900 do Excel; número como texto ("R$ 1.234,56") = erro, não chute; converter para `decimal` com 2 casas e rejeitar NaN/infinito. Colunas localizadas pelo **nome do cabeçalho normalizado** (sem acento/caixa), não por posição; obrigatórias: data, lançamento, valor; opcionais: parcelamento, tipo do cartão, final do cartão. Texto de cabeçalho não reconhecido = `unsupported_layout`.
- **Escopo da fatura** = (conta-cartão, vencimento). O vencimento e o total declarado vêm do bloco de cabeçalho.
- **Datas:** `DataCompra` = data da linha; `DataFatura` = vencimento. `Date` (usada pelo resumo) = data da linha para compras à vista e parcela 1; para **parcelas com número > 1**, `Date` = vencimento menos 1 mês (heurística: cai no meio do ciclo da fatura; a data da compra original fica em `DataCompra`). A S3/S4 pode rever a competência; guardar ambas permite isso sem migração de dados.
- **Classificação:** descrição normalizada que começa com o **token inteiro** `PAGAMENTO` **e** valor que vira positivo na convenção normalizada = **Pendente** (pagamento da fatura anterior); `SALDO ANTERIOR`/rotativo, se existir como linha = Pendente; todo o resto, incluindo estornos, encargos, IOF, juros e anuidade = Normal.
- **Cartão:** final do cartão e tipo (físico/virtual/virtual recorrente) por linha. Aviso `UnknownCardSuffix` (não bloqueia) quando a conta já tem linhas de fatura importadas e o arquivo traz um final que ela nunca viu; na primeira importação nada é checado.
- **Sem checkpoint** para o cartão do Itaú na S2 (o total da fatura é a dívida no fechamento; sem a data de fechamento qualquer checkpoint corromperia o saldo); o saldo do cartão fica como soma das linhas importadas até a S3/S4. O total declarado e o resultado da conferência ficam no lote.
- **Reimportar:** arquivo idêntico = no-op; superconjunto (fatura aberta que cresceu) insere só as novas; se existirem no escopo linhas confirmadas que **não aparecem** no arquivo novo, aviso **bloqueante** `InvoiceHasMissingLines` com override explícito **desligado**: a resposta orienta "desfaça o lote anterior desta fatura e importe de novo" (o desfazer já mostra `editedCount`). Nunca grava duplicado em silêncio nem apaga sozinho linhas editadas.

## Conferências (código → severidade)
| Código | Severidade |
|---|---|
| `InvoiceTotalMismatch` (total declarado ≠ soma das linhas que não são pagamento, estornos e encargos inclusos) | **Bloqueia**, override com `acknowledgeBlockingWarnings` (regra validada em duas faturas reais) |
| `InvoiceHasMissingLines` (ver acima) | Bloqueia, sem override: resolve-se desfazendo o lote anterior |
| `AccountIdMismatch` (ACCTID/agência × `externalIdentifier`) | Bloqueia, override |
| `LedgerBalanceMismatch` (saldo final do OFX × calculado, quando há checkpoint anterior) | Avisa |
| `LedgerBalanceBeforeLastLine` | Avisa |
| `PeriodGapOrOverlap`, `DatesOutsidePeriod` (já existem no Nubank) | Avisa |
| `PossibleDuplicate`, `UnknownLineKinds`, `UnknownCardSuffix` | Avisa |
Os avisos são traduzidos no front pelo `code`; a mensagem do servidor nunca é exibida.

## API e front
- `POST /api/imports` aceita OFX e xlsx (limite 5 MB, nginx 6 MB). Novos códigos de erro: `unsupported_bank`, `not_xlsx`, `encrypted_xlsx`, `xlsx_limits_exceeded`, `xlsx_unsafe_content`, `unsupported_layout`, `invalid_cell`. Linha da prévia ganha `ordinal`, `installmentNumber`, `installmentTotal`, `cardSuffix`, `cardKind`, `purchaseDate`; `overrides` ganham `ordinal` (padrão 0). OpenAPI e snapshot regenerados.
- Tela Importar: `accept=".ofx,.xlsx"`, checagem de extensão `.ofx`/`.xlsx`; texto de ajuda por tipo de conta (cartão ↔ fatura); prévia mostra "Parcela X de Y", tipo e final do cartão (final mascarado como "•••• 1234" só em texto); avisos e erros novos com tradução pt-BR/en-US (teste de paridade e de cobertura de todos os códigos continuam valendo); valores continuam respeitando "esconder valores".
- Aviso de UX no desfazer (já existente) passa a lembrar que linhas **excluídas** por você continuam ocupando a chave e não voltam ao reimportar.

## Segurança e privacidade
Tudo de "Segurança de upload" do pai e da S1a, mais as verificações de zip/xlsx acima. Logs nunca contêm nome de arquivo, descrição, valores, ACCTID ou conteúdo de célula; testes de log em nível Trace para os dois leitores. Fixtures sintéticas geradas por código (incluindo SGML sem fechamento, xlsx com logo, fórmula no rodapé, `date1904`, e casos hostis: zip bomb, macro, link externo, xlsx criptografado, número como texto, fórmula em célula de lançamento). A suíte local contra `finance-samples/` nunca imprime descrições nem valores em asserts.

## Testes
Unitários dos dois leitores e das regras (inversão de sinal nos dois sentidos, classificação, datas de parcela, escopo, `date1904`); propriedade: reimportar N vezes e importar exportações sobrepostas = mesmo estado, e nenhuma linha idêntica se perde; integração com Postgres: fluxo completo (prévia → confirmar com overrides → desfazer → reimportar), multiconjunto com buracos de ordinal, corrida de confirmações, fatura reimportada (idêntica, superconjunto, com linha faltando), isolamento entre usuários; front: aceita `.xlsx`, mostra parcela/cartão, traduz todos os códigos novos, hide-values nos dados novos; e2e do front atualizado.

## Fica para o construtor
Estrutura de pastas e nomes (interface do leitor, perfis), tamanho exato dos limites do zip/planilha, mensagens e chaves i18n, desenho exato das colunas novas na prévia, formato do resumo do lote, detalhes do leitor SAX, ordem das tarefas e migrações.

## Decisões minhas (modo ausente) — o João revisa ao voltar
1. Unknown no Itaú conta = Normal + contador (padrão da S1a), em vez de "pendências" do pai. 2. `PIX CARTAO` e `PAG BOLETO` pendentes por prudência (despesa em dobro), contra a proposta inicial de Normal. 3. Sem checkpoint para o cartão; conferência do total **bloqueia** (validada em duas faturas reais). 4. Parcela > 1 datada em "vencimento − 1 mês" no resumo, com a data original guardada. 5. Fatura reimportada com linhas faltando bloqueia e exige desfazer o lote anterior (sem substituição automática). 6. Biblioteca xlsx: Open XML SDK (SAX). 7. Grupos/projeção de parcelas ficam para a S3/S4.

## Decisões complementares (após o builder check; do Claude, em modo ausente)
1. **"Linhas faltando" na fatura:** conta como faltando toda linha **confirmada e não excluída logicamente** do escopo (editadas inclusive) cuja chave+ordinal não aparece no arquivo novo. Linhas excluídas logicamente não contam (continuam ocupando a chave, então não voltam). Linhas de lotes desfeitos não existem mais.
2. **Aviso não sobrescrevível:** `ImportWarning` ganha o campo booleano `overridable` (padrão `true` para bloqueantes). `InvoiceHasMissingLines` é `overridable: false`; o `ConfirmAsync` recusa (`409 blocking_warnings`) mesmo com `acknowledgeBlockingWarnings`. No front, aviso bloqueante não sobrescrevível desabilita Confirmar e esconde a caixa "entendo e quero confirmar" (mesmo tratamento do "arquivo já importado"). OpenAPI e snapshot regenerados.
3. **Período do lote de fatura:** `PeriodStart`/`PeriodEnd` ficam **nulos** para fatura; `DatesOutsidePeriod` e lacuna/sobreposição de período **não se aplicam** a fatura (a data de parcelas é sintética).
4. **Formato do xlsx (observado):** cabeçalho de colunas literal na linha dos lançamentos: `Data`, `Lançamento`, `Parcelamento`, `Valor`, `Titularidade`, `Nome`, `Tipo do cartão`, `Número do cartão` (comparados normalizados: sem acento/caixa, espaços colapsados). Bloco de resumo: linha com `Cartão`, `Valor`, `Vencimento` e, na linha seguinte, produto, total (número) e vencimento (serial do Excel). Data da linha = serial numérico com estilo `dd/mm/yyyy` (sem data em texto; texto = `invalid_cell`). Parcelamento = `Parcela N de M` (regex sem caixa; vazio = à vista). Tipo do cartão: `Físico` → `Physical`, `Virtual` → `Virtual`, `Virtual recorrente` → `VirtualRecurring`, outro → `Other`. Número do cartão = `****NNNN` → `CardSuffix` = 4 dígitos. `Titularidade` e `Nome` **não são armazenados** (dado pessoal sem uso). A tabela de lançamentos termina na primeira linha após o cabeçalho cuja coluna `Data` não seja uma data (a linha "Subtotal" e o rodapé são ignorados). Cabeçalho obrigatório ausente = `unsupported_layout` (nunca adivinhar posição). Fixtures sintéticas reproduzem exatamente esses rótulos.
5. **`ACCTID` do Itaú:** número só de dígitos, sem `BRANCHID` no arquivo. O `externalIdentifier` da conta guarda esse número como o usuário digitou; a comparação remove tudo que não é dígito e zeros à esquerda dos dois lados. A fatura não tem identificador de conta: o único controle é `UnknownCardSuffix` (aviso).
6. **Vários blocos de resumo** no mesmo xlsx = `unsupported_layout`.
7. **Soma da conferência de total:** soma das linhas **não classificadas como Pendente** (ou seja, exclui pagamento da fatura anterior e `SALDO ANTERIOR`/rotativo; inclui estornos, encargos, IOF, juros).
8. **Escolha do leitor:** por conteúdo (OFX: `BANKID`; xlsx: assinatura zip + cabeçalhos). Depois valida fonte × tipo de conta: divergência = `422 account_type_mismatch`; banco não suportado = `unsupported_bank`; arquivo que não é OFX/xlsx = `not_ofx`/`not_xlsx`.
9. **`PossibleDuplicate`:** compara linha nova contra linhas **existentes da conta com chave diferente de todas as chaves do arquivo**; ordinais irmãos da mesma chave nunca se comparam entre si.
10. **Prévias em andamento no deploy:** o JSON da prévia sem `Ordinal` é lido com padrão 0 e metadados novos nulos; prévias expiram em 24 h de qualquer forma.
