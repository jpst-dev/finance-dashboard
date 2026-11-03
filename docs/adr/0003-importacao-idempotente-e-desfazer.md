# ADR 0003: Importação idempotente, prévia no servidor e desfazer por exclusão lógica

- Status: aceita
- Data: 2026-11-02

## Contexto
O usuário importa extratos OFX do Nubank (conta e cartão) repetidas vezes: o mesmo arquivo pode ser reenviado, períodos se sobrepõem entre arquivos, duas abas podem confirmar ao mesmo tempo e um lote errado precisa poder ser desfeito sem apagar o que o usuário editou. O arquivo enviado contém dados financeiros reais e não deve ser guardado nem aparecer em logs.

## Decisão
- **Identidade de uma linha importada = conta + `SourceKey` (FITID do OFX) + `Ordinal`.** Um índice único parcial em `(AccountId, SourceKey, Ordinal)` (onde `SourceKey` não é nulo) impede duplicar uma linha, mesmo que o mesmo FITID apareça em arquivos diferentes. O `Ordinal` existe para tolerar FITIDs repetidos dentro de um arquivo sem perder linhas. Não usamos data+valor+descrição como chave, porque compras iguais no mesmo dia são legítimas.
- **A prévia fica no servidor** (`ImportBatch.PreviewJson`, estado `Preview`, validade de 24 h). O upload é processado em memória, o arquivo nunca é gravado, e o `confirm` usa apenas o que foi guardado na prévia, sem reenviar o arquivo e sem confiar no cliente. Ao confirmar (ou expirar) o JSON é descartado. O hash do arquivo (`FileHash`) detecta reenvio de um arquivo já confirmado.
- **Exclusão lógica com marcador (`IsDeleted`).** Apagar uma linha importada mantém a linha como marcador; assim reimportar o mesmo arquivo não a ressuscita. O filtro global de usuário não esconde apagados (a importação precisa vê-los); leituras usam `Visible()`.
- **Desfazer** marca o lote como `Undone` e apaga fisicamente as linhas que pertencem a ele (`BatchId`), inclusive as já excluídas logicamente, e o checkpoint `LedgerBal` que ele gerou; o hash fica livre para reimportar. A linha pertence ao lote que a criou: se outro lote sobreposto a "pulou" por já existir, desfazer o lote dono a remove também.
- **Concorrência sem filas globais:** (1) *claim condicional*: `UPDATE ... SET State='Confirmed' WHERE Id=@id AND State='Preview'`; só quem afeta 1 linha prossegue (o hash único de lotes confirmados barra o mesmo arquivo em dois lotes); (2) as linhas entram com `INSERT ... ON CONFLICT DO NOTHING` no índice acima, ordenadas por FITID para evitar deadlock; (3) o checkpoint do saldo usa `pg_advisory_xact_lock` por conta para serializar o "já existe?" entre confirmações simultâneas.
- **Privacidade:** nenhum log ou resposta de erro repete descrição, valor, nome ou corpo; erros do parser citam só tipo e número da linha. Testes automatizados capturam os logs e procuram marcadores únicos.

## Consequências
- Reenviar o mesmo arquivo, ou arquivos sobrepostos, não duplica lançamentos; linhas já existentes são contadas como "existentes" na prévia.
- Desfazer um lote remove as linhas que ele criou, mesmo as que outro lote sobreposto pulou; para recuperá-las, reimporte o arquivo. É um comportamento explícito, documentado no README.
- Enquanto o lote existir, uma linha já gravada nunca é sobrescrita por reimportação, então edições de estado/categoria e exclusões lógicas sobrevivem a arquivos sobrepostos.
- Prévias abandonadas ocupam uma linha de lote até expirarem (24 h); expiradas são descartadas e a confirmação é recusada.
- O índice único e o advisory lock dependem do PostgreSQL; a suíte usa Postgres real (Testcontainers).

## Alternativas consideradas
- **Chave por data + valor + descrição**: descartada; colide com compras idênticas legítimas e quebra com ajustes de texto.
- **Reenviar o arquivo no `confirm`**: descartada; exige guardar/reenviar dados reais e permite divergência entre prévia e confirmação.
- **Apagar fisicamente linhas excluídas**: descartada; a reimportação traria de volta o que o usuário apagou.
- **Bloqueio global ou isolamento `Serializable`**: descartados; o claim condicional + índice único + lock por conta cobrem as corridas com menos contenção.
