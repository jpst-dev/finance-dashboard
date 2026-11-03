# ADR 0005: Identidade por conteúdo e leitores do Itaú (OFX da conta e xlsx da fatura)

- Status: aceita
- Data: 2026-11-03

## Contexto
A S2 importa dois formatos do Itaú que, ao contrário do OFX do Nubank, **não têm um identificador confiável por lançamento**:

- O **OFX da conta** traz `FITID` numérico que é posicional (data + sequência dentro do arquivo): uma nova exportação do mesmo período pode renumerar as linhas. Além disso há grupos legítimos de lançamentos idênticos no mesmo dia (mesma data, valor e memo).
- O **xlsx da fatura** não tem identificador nenhum, nem de conta: só data, descrição, parcela, valor e final do cartão. A mesma fatura (aberta) é baixada várias vezes, crescendo, e cada download gera bytes diferentes (o zip leva carimbo de data/hora).
- Um xlsx é um zip com XML: pode carregar fórmulas, macros, links externos e bombas de descompressão.

A identidade da S1a ([ADR 0003](0003-importacao-idempotente-e-desfazer.md)) era conta + `SourceKey` (FITID) + `Ordinal`, mas o pipeline tratava o `SourceKey` como único por arquivo (`DistinctBy`, classificações da prévia indexadas só pela chave).

## Decisão
- **Chave de conteúdo.** Para o Itaú, `SourceKey` = SHA-256 hex minúsculo (64 caracteres) dos componentes normalizados unidos por `|`: conta = `itau-conta|data|valor|memo`; fatura = `itau-fatura|vencimento|data do arquivo|valor|descrição|parcela|final do cartão`. O hash cabe no limite da coluna e não guarda texto do extrato. A normalização de texto (maiúsculas invariantes, sem acentos após FormD, espaços colapsados, trim) é **congelada** e coberta por teste de contrato.
- **Ordinal = k-ésima ocorrência da chave no arquivo** (0..n−1). Linhas idênticas no mesmo dia viram (chave, 0), (chave, 1)…, então nenhuma é perdida e reimportar não duplica.
- **Identidade por par no pipeline inteiro.** Prévia, `overrides` (ganharam `ordinal`, padrão 0, compatível com o Nubank), `DistinctBy`, a consulta de "existentes" (quais pares existem, não quantas linhas) e o `INSERT … ON CONFLICT DO NOTHING` usam (chave, ordinal). A checagem "chave em outra conta" só vale para o Nubank (UUID), não para chaves de conteúdo.
- **Perfis de banco sobre um leitor OFX tolerante comum**, escolhidos pelo `BANKID` normalizado (260 = Nubank, 341 = Itaú, outro = `unsupported_bank`). O leitor é escolhido pelo **conteúdo**, nunca pela extensão: assinatura zip/CFB vai para o leitor da fatura; o resto é lido como OFX. Depois valida fonte × tipo da conta (`account_type_mismatch`).
- **Pré-validação própria do zip antes do Open XML SDK** (leitura SAX, valor em cache, nunca avaliar fórmula): CFB/criptografado = `encrypted_xlsx`/`not_xlsx`; até 200 entradas, 50 MB descompactados, razão ≤ 100 em partes ≥ 64 KiB (conferida também com contador ao ler); cabeçalho local e central precisam concordar (aceitando *data descriptor*, bit 3); Zip64, nomes com `..`/absolutos, `vbaProject.bin`, content type `macroEnabled`, `externalLinks/`, `connections.xml`, `embeddings/`, `activeX/` e relações `TargetMode="External"` são recusados; mídia e drawings nunca são lidos; DTD proibido. Leitor da planilha com limites de linhas (5000), colunas (64), tamanho de célula e de `sharedStrings`.
- **Layout da fatura por rótulo normalizado de cabeçalho**, nunca por posição; cabeçalho desconhecido ou ausente = `unsupported_layout`; célula inválida = `invalid_cell` (a resposta traz só o código; a linha fica só em `ImportFormatException.Line`, para diagnóstico e testes). Sinal invertido no leitor (cobrança positiva → negativa). Parcelas com número > 1 recebem `Date` = vencimento − 1 mês (a data da compra fica em `PurchaseDate`).
- **Conferências da fatura:** total declarado = −(soma das linhas que não são Pendente) ou `InvoiceTotalMismatch` (bloqueia, com override); **fatura com linhas faltando bloqueia sem override** (`InvoiceHasMissingLines`): se o escopo (conta, vencimento) tem linhas confirmadas e não excluídas cujo par não aparece no arquivo novo, a saída é desfazer o lote anterior e importar de novo. Nada é apagado ou substituído sozinho.

## Consequências
- Reimportar o mesmo arquivo, outra exportação do mesmo período ou uma fatura que cresceu insere só o que falta; linhas idênticas legítimas são preservadas.
- **Normalização congelada:** mudar maiúsculas/acentos/espaços ou os componentes da chave muda todas as chaves e exige migração; o teste de contrato falha antes.
- Uma correção de texto no banco (ex.: o Itaú muda a grafia de um memo numa nova exportação) gera chave nova. Na conta corrente a linha aparece de novo como nova (o aviso `PossibleDuplicate`, não bloqueante, sinaliza valor igual, data ±1 dia e mesmo prefixo); na fatura, a linha antiga passa a faltar no arquivo novo, então `InvoiceHasMissingLines` bloqueia e exige desfazer o lote anterior e importar de novo.
- **Buracos de ordinal** são possíveis (desfazer um lote sobreposto) e são relatados como linhas novas na prévia; o `ON CONFLICT` continua sendo o árbitro.
- **O hash do arquivo não garante idempotência no xlsx** (o carimbo do zip muda a cada download); ele só barra o reenvio dos mesmos bytes (`already_imported`). A defesa real é a identidade por linha.
- Cartão do Itaú não gera checkpoint de saldo na S2; o total declarado e o resultado da conferência ficam no lote.

### Limitações conhecidas (das revisões)
- **OFX sem `BANKID` é tratado como cartão Nubank** (`CCACCTFROM` não tem `BANKID` no padrão), então um OFX de cartão do Itaú seria lido como Nubank; o caminho do cartão Itaú é o xlsx.
- **Escopo de `InvoiceHasMissingLines` = (conta, `StatementDate`).** Dois cartões diferentes importados na mesma conta `CreditCard` com o mesmo vencimento se bloqueiam (as linhas de um "faltam" no arquivo do outro). Mitigação futura: incluir `CardSuffix` no escopo, ou usar uma conta por cartão.
- **A checagem de linhas faltando no confirm** protege contra confirmar um arquivo incompleto *depois* de um mais completo, mas não contra confirmar uma exportação **mais antiga** depois de uma mais nova quando ambas as prévias foram abertas antes: a mais antiga, sendo subconjunto, não insere nada novo e o estado continua convergindo para a união.
- **Normalização de texto das chaves é congelada** (ver acima).
- **Hash de arquivo não garante idempotência para xlsx** (ver acima).
- Relações `TargetMode="External"` são recusadas por inteiro; se uma fatura real passar a trazer hiperlink de célula, o arquivo será recusado com `xlsx_unsafe_content` e a regra precisará ser relaxada só para o tipo `hyperlink` (sem nunca seguir o link). As faturas reais verificadas não têm relações externas.

## Alternativas consideradas
- **Contagem por chave** (guardar "n linhas com esta chave" em vez de pares): descartada; mente quando um lote sobreposto é desfeito e deixa buracos, e não permite override por linha.
- **Substituir o conjunto da fatura** a cada importação (apagar e regravar o escopo): descartada; apagaria em silêncio linhas editadas/categorizadas e esconderia linhas que sumiram do banco.
- **Usar o `FITID` do Itaú**: descartado; é posicional e muda entre exportações.
- **ClosedXML / EPPlus**: descartadas; ClosedXML traz motor de cálculo (avalia fórmulas), EPPlus 5+ tem licença comercial. Open XML SDK (MIT) em modo SAX, depois da pré-validação própria, lê só o necessário.
