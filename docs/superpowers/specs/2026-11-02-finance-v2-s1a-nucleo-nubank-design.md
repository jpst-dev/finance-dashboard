# Finance v2 — S1a: núcleo do backend + importador Nubank (design)

Pai: `2026-11-02-finance-v2-backend-design.md` (binding; este documento detalha a primeira metade da S1 e não o contradiz). S1b (interface) tem design próprio depois.

## Intenção
O João quer um lugar onde adicionar gastos e investimentos com facilidade e acompanhar. A S1a entrega o **servidor** que torna isso possível para contas e lançamentos: cadastrar contas, lançar à mão, importar o OFX do Nubank (conta e cartão) com prévia e desfazer, e consultar o resumo do mês calculado no servidor. É testável pela documentação da API (Scalar) antes de existir tela.

O banco principal do João é o Itaú (S2). O núcleo é desenhado já para ele, mas o importador do Itaú **não** entra aqui.

## Objetivos
- Contas (corrente, cartão de crédito, carteira) com natureza ativo/passivo.
- Categorias (lista padrão, editável) e lançamentos (CRUD manual e por importação).
- Checkpoints de saldo e saldo atual por conta.
- Resumo mensal (receitas, despesas, por categoria) calculado no servidor.
- Importador Nubank OFX (conta e cartão): enviar, prévia, confirmar, desfazer lote.
- Isolamento por usuário provado por teste de integração.

## Não-objetivos
Importador do Itaú, conciliação/vínculos (transferência, pagamento de fatura, Pix parcelado), regras de categorização automática, parcelas, recorrências, investimentos/cripto, patrimônio, qualquer tela, cliente TypeScript gerado, jobs em segundo plano.

## Anti-objetivos (falha mesmo se "funcionar")
- Gravar qualquer coisa antes da confirmação da prévia; duplicar lançamento em reimportação ou requisições concorrentes.
- Tratar pagamento de fatura ou transferência como despesa/receita **sem aviso**: até a S3, esses lançamentos entram marcados como **pendentes de classificação** e ficam **fora** do resumo mensal (nunca contam em silêncio).
- Logar descrição, valor, nome ou corpo de requisição; guardar o arquivo enviado.
- Aritmética de dinheiro em `double`.

## Restrições
- Valem todas as do pai: .NET 10, EF Core + Npgsql, dinheiro como string decimal na API e `numeric(18,2)`, datas `DateOnly` (data literal do OFX, sem UTC), cookie + antiforgery em todo endpoint que muda estado, nada de dados reais em git/logs/testes, fixtures sintéticas, Testcontainers.
- Importação: um arquivo por requisição, limite de 5 MB, validação por conteúdo; parser sem resolver entidades externas (DTD proibido); mensagens de erro citam linha e tipo, nunca conteúdo.
- `nginx` precisa de `client_max_body_size` compatível (herdado da lista de adiados da S0).

## Abordagens consideradas
A. **Módulos em camadas simples** (pasta por módulo com endpoints, entidades, serviços; EF Core direto; `Domain` só com regras reais). B. Domínio rico com repositórios. C. CQRS com MediatR.
**Escolhida: A** (João aceitou a recomendação): menos código para o mesmo resultado; a S3 pode extrair domínio mais rico quando a conciliação exigir. Também decidido: S1 dividida em S1a (backend) e S1b (interface); Nubank antes do Itaú, com modelo já preparado para o Itaú.

## Modelo (S1a)
Todas as entidades têm `UserId`; filtro global de consulta por usuário.
- **Conta**: nome, tipo (`Corrente`, `Cartao`, `Carteira`), natureza derivada (`Cartao` = passivo), instituição (texto livre), moeda fixa BRL, `identificadorExterno` opcional (conta/cartão do banco, para reconhecer o arquivo certo na importação), arquivada (bool).
- **Categoria**: nome, tipo (`Despesa`/`Receita`), padrão (bool). Lista padrão semeada por usuário na criação (nomes: escolha do construtor).
- **Lançamento**: conta, `data` (`DateOnly`), `dataCompra` e `dataFatura` (`DateOnly?`, usados na S2; no Nubank `dataFatura` fica nula), valor com sinal (convenção do pai: ativo entrada +/saída −; cartão cobrança −/pagamento +), descrição original, descrição normalizada (apenas trim/espaços), categoria opcional, **estado de classificação** (`Normal`, `PendenteClassificacao`), `origem` (`Manual`/`Importado`), `loteId` opcional, `chaveOrigem` e `ordinal` (identidade de importação), contraparte opcional (texto) e documento mascarado opcional (para a S3).
- **Checkpoint de saldo**: conta, data, valor, origem (`LedgerBal`/`Manual`), `loteId` opcional.
- **Lote de importação**: fonte (`NubankConta`, `NubankCartao`), conta, hash SHA-256 do arquivo, período (início/fim do arquivo), resumo (novos, ignorados, avisos), estado (`Previa`, `Confirmado`, `Desfeito`), criado em. O arquivo original não é guardado.

Índice único `(conta, chave_origem, ordinal)` para lançamentos importados; índice único parcial no hash do lote **confirmado** por usuário (liberado ao desfazer).

## Comportamento
**Saldo da conta** = último checkpoint + soma dos lançamentos com `data` posterior ao checkpoint. Sem checkpoint: soma de todos os lançamentos. Cartão: saldo negativo representa dívida.

**Resumo mensal** (`mês`): receitas = soma dos lançamentos `Normal` com valor > 0 em contas que não são cartão; despesas = soma dos `Normal` com valor < 0 em qualquer conta; saldo do mês = receitas + despesas; quebra por categoria (sem categoria aparece como "Sem categoria"). Lançamentos `PendenteClassificacao` são **excluídos** e o resumo informa quantos e quanto somam. O recorte do mês é pelo campo `data`.

**Importador Nubank** — um leitor para os dois formatos:
- OFX de conta (`BANKMSGSRSV1`/`STMTRS`) e de cartão (`CREDITCARDMSGSRSV1`/`CCSTMTRS`); reconhece pelo conteúdo, não pela extensão.
- Cabeçalho SGML/OFX 1.02 com folhas **fechadas**; decodificação pelo `CHARSET`/`ENCODING` do cabeçalho (1252 ou UTF-8, BOM tolerado). Leitor tolerante (não usa parser XML estrito).
- `FITID` (UUID) é a `chaveOrigem` (`ordinal` 0). `DTPOSTED` → data literal. `TRNAMT` → valor, já na convenção (conta: entrada +; cartão do Nubank: cobrança −, pagamento +, igual ao pai). `MEMO` → descrição original.
- `LEDGERBAL` (`BALAMT`/`DTASOF`) → checkpoint na confirmação.
- Classificação na entrada: `MEMO` que começa com "Pagamento de fatura" (conta), "Pagamento recebido" (cartão), "Transferência enviada/recebida" e "Pix no Crédito" → `PendenteClassificacao`; compras no débito/crédito e demais → `Normal`, sem categoria. Regras são texto fixo simples em um lugar só, para a S3 substituir.
- Contraparte/documento: extraídos do `MEMO` de Pix quando o padrão bate; senão nulos.
- Conta de destino: o usuário escolhe a conta ao enviar; se `identificadorExterno` da conta existir e divergir do arquivo, a prévia avisa (bloqueia só com override explícito).
- Fluxo: `POST` enviar → prévia (linhas lidas, novas, já existentes pelo `FITID`, avisos, checkpoint proposto, período) **sem gravar lançamentos**; `POST` confirmar → grava em transação; `POST` desfazer lote → remove lançamentos e checkpoint do lote e libera o hash. Reenviar o mesmo hash confirmado: prévia informa "já importado". O servidor guarda a prévia no lote em estado `Previa` (só metadados + linhas lidas) com expiração; confirmar re-valida.
- Conferências (avisam, não bloqueiam): datas fora de `DTSTART`/`DTEND`, soma dos lançamentos vs. `LEDGERBAL` dado o checkpoint anterior (quando existir), lacuna/sobreposição de período com lotes anteriores da conta.
- Concorrência: duas confirmações simultâneas do mesmo arquivo não duplicam (índice único + transação); a perdedora recebe resposta de "já importado".

**API** (JSON, cookie + antiforgery, dinheiro como string): CRUD de contas e categorias; CRUD de lançamentos manuais (editar/excluir importado é permitido, com aviso no desfazer); consulta de lançamentos com filtros (conta, mês, categoria, estado) e paginação; saldo por conta; resumo mensal; checkpoints (criar manual, listar); importação (enviar, prévia, confirmar, desfazer, listar lotes). Detalhe de rotas e DTOs: construtor.

## Segurança e privacidade
Upload conforme "Segurança de upload" do pai. Nada do arquivo vai para logs; erros de parser citam linha/tipo. Descrições e contrapartes são texto de terceiros: a API as devolve como dado, nunca como HTML. Teste de integração de isolamento: usuário B não vê nem altera contas, lançamentos, lotes e checkpoints do usuário A (leitura, escrita e desfazer).

## Testes
Unitários do leitor com fixtures sintéticas geradas no formato real (conta e cartão; 1252 e UTF-8; com e sem BOM; Pix com e sem contraparte). Integração com Postgres (Testcontainers): fluxo completo enviar→prévia→confirmar→desfazer→reimportar; idempotência; concorrência; saldo com e sem checkpoint; resumo mensal excluindo pendentes; isolamento. Teste de propriedade para a idempotência (reimportar N vezes = mesmo estado). Suíte opcional local contra `finance-samples/` (ignorada na CI, sem imprimir descrições em falhas).

## Fica para o construtor
Nomes de pastas/rotas/DTOs, categorias padrão, regras exatas de texto para classificação pendente (dentro do definido acima), expiração da prévia, paginação, ajuste de índices/migrations, `client_max_body_size`.

## Decisões complementares (minhas, após revisão do plano por um construtor independente)
- **Checkpoint**: vale ao **fim do dia** (`DTASOF`); saldo = valor do checkpoint + lançamentos com `data` **estritamente posterior**. "Último" = maior data; empate pelo mais recente criado. A conferência "soma vs. LEDGERBAL" usa o checkpoint anterior.
- **Prévia**: linhas lidas ficam em coluna `jsonb` do lote (`Previa`); confirmar recebe só o id do lote (sem novo upload) e revalida contra o estado atual. Prévia expira em 24 h e é descartada de forma preguiçosa (ao enviar/listar); sem job. Reenviar o mesmo hash com prévia aberta substitui essa prévia.
- **Unicidade**: índice parcial do hash vale só para lotes `Confirmado`, por usuário (entre contas). Quem arbitra a corrida é o índice `(conta, chave_origem, ordinal)` dentro da transação; a perdedora recebe `409` "já importado".
- **FITID em outra conta**: se `FITID`s do arquivo já existem em **outra** conta do usuário, a prévia bloqueia com override explícito.
- **Fonte × conta**: `NubankCartao` só em conta `Cartao`; `NubankConta` só em `Corrente`. Divergência = `422`.
- **Classificação por texto**: prefixos comparados sem diferenciar maiúsculas/acentos. Qualquer "Transferência enviada/recebida" e "Pix no Crédito" vai para pendente (inclui Pix a terceiros; ver "Decisões do João" abaixo sobre reclassificar).
- **Arquivos estranhos**: mais de um extrato no arquivo = `422`; sem `LEDGERBAL` = importa sem checkpoint; período vazio é válido; não-OFX = `422` genérico.
- **Resumo**: mês no formato `2026-09`; mês só com pendentes devolve zeros e a contagem de pendentes; estorno em cartão (positivo `Normal`) reduz as despesas; receita = positivo `Normal` em conta que não é cartão.
- **Lista de lançamentos**: ordem `data` desc, `id` desc; paginação por `page`/`size` (padrão 50, máx. 200).
- **Categorias padrão**: semeadas para todo usuário, também para os já existentes (idempotente, no startup); renomeáveis e excluíveis (lançamentos ficam sem categoria).
- **Usuário corrente**: serviço escopado `ICurrentUser` lido das claims; seed, migrations e testes usam `IgnoreQueryFilters`. Cria-se o projeto `FinanceDashboard.Domain` pequeno (entidades e regras puras: sinal, classificação por texto, saldo, resumo); mapeamento EF fica na Infraestrutura.
- **Antiforgery no upload**: multipart passa pela mesma verificação (`X-XSRF-TOKEN`); o README documenta o fluxo com `curl` (o Scalar não obtém o token sozinho).

## Decisões do João (após a revisão)
- **Reclassificar**: o usuário pode editar estado (`Normal`/`PendenteClassificacao`) e categoria de qualquer lançamento, importado ou manual. "Pendente" é sugestão. Reimportar nunca altera uma edição do usuário (o lançamento existente, pela chave, é ignorado).
- **Apagar importado**: exclusão lógica. O lançamento fica oculto, fora de listas e de todos os cálculos, e **mantém a chave**, então reimportar não o recria. "Desfazer lote" remove de verdade (inclusive os ocultos do lote) e libera o hash.

## Decisões minhas para o João conferir
- Pagamento de fatura, "Pagamento recebido", transferências e "Pix no Crédito" entram como **pendentes de classificação e fora do resumo** até a S3 (evita contar pagamento de fatura como despesa).
- Resumo mensal por `data` do lançamento.
- Prévia fica guardada no servidor (metadados + linhas lidas), sem o arquivo.
- Conta é escolhida pelo usuário ao importar; `identificadorExterno` só valida.
