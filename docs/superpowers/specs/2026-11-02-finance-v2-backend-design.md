# Finance Dashboard v2: backend, importação de extratos e patrimônio (design, revisão 2)

Esta é a segunda revisão do documento, após duas revisões independentes (uma checagem de "construtor" e uma revisão crítica). As mudanças principais estão em "Mudanças da revisão 2".

## Intenção e por quê
Hoje o Finance Dashboard é só front-end (Vue 3 + Pinia + `localStorage`). A única informação que vem de fora são os preços de cripto da CoinGecko; todo o resto é digitado à mão. Isso o torna uma vitrine bonita, mas inútil no dia a dia.

A v2 tem dois propósitos de peso igual:
1. **Uso real do João**, praticamente o único usuário. Os dados entram por importação de extrato e fatura (Itaú e Nubank), não por digitação, e o app mostra o que sobra, o que está comprometido em parcelas e o patrimônio total.
2. **Portfólio**: demonstrar habilidade de backend (modelagem de dados, importação idempotente, conciliação, jobs, API documentada, testes de integração, CI, Docker). O João trabalha com .NET/C# e REST, e o repositório é público.

Lacuna de mercado observada na pesquisa: o Firefly III não controla carteira de investimentos; os apps brasileiros são ou só de gastos (Mobills, Organizze) ou só de investimentos (Kinvo, Investidor10). Este projeto junta gastos e carteira em reais.

## Objetivos
- Importar extrato/fatura reais sem digitar: Itaú conta (OFX), Itaú cartão (xlsx), Nubank conta e cartão (OFX). Reimportar o mesmo arquivo ou um período sobreposto não duplica nada, e uma importação pode ser desfeita.
- Tratar os casos brasileiros: parcelamento, Pix parcelado no cartão, pagamento de fatura (transferência, não despesa), estorno/contestação, transferência entre contas próprias, aplicação/resgate automático.
- Calcular no servidor o que hoje é calculado nas stores do Pinia (saldos, resumo mensal, variação, lucro da carteira).
- Dinheiro sem erro de ponto flutuante, do banco ao navegador; datas sem o bug atual de comparar só o mês.
- Histórico de patrimônio e de carteira **real**, a partir de snapshots diários de preço guardados pelo servidor (hoje o gráfico de histórico só multiplica a quantidade pelo preço de compra).
- Preços de mercado buscados uma vez pelo servidor, com cache, em vez de cada navegador chamar a CoinGecko a cada 10 s.
- API documentada (OpenAPI) com cliente TypeScript gerado.
- Qualidade demonstrável: testes unitários e de propriedade dos importadores e da conciliação, testes de integração com Postgres real, CI, Docker Compose.
- O site público continua mostrando um **demo navegável, somente leitura**, sem login e sem depender do backend.

## Fora do escopo (v2)
- Cadastro aberto, multi-moeda (só BRL; valor original em moeda estrangeira é apenas metadado), orçamento compartilhado, app nativo.
- Open Finance para outros usuários (a Pluggy cobra planos comerciais; valor não verificado). Só o conector opcional "Meu Pluggy" para o próprio João, depois da v2.0.
- Contabilidade de partida dobrada.
- Importador de PDF do Itaú (o xlsx e o OFX cobrem o necessário).
- Categorização por IA, relatório de Imposto de Renda, alertas por Telegram/e-mail, PWA.
- Microsserviços. O backend é um monólito modular.

## Anti-objetivos (falha mesmo que "funcione")
- Qualquer dado financeiro real (extratos, nomes, CPF, agência, conta, nomes de terceiros em Pix, valores reais, hábitos e eventos pessoais identificáveis) entrar no repositório, em fixtures, testes, logs, mensagens de commit ou **neste documento público**.
- O demo do portfólio quebrar por causa do backend.
- Contar duas vezes o mesmo gasto (Pix parcelado, pagamento de fatura) ou tratar estorno como receita.
- Importar silenciosamente um arquivo cujo total não fecha, ou duplicar lançamentos em silêncio.

## Restrições
- Custo mensal: zero. Hospedagem na nuvem fica fora da v2.0.
- Um desenvolvedor, tempo limitado: entregar em fatias verticais que já funcionam sozinhas.
- A pasta `api/` na raiz é tratada pela Vercel como funções serverless, então o backend **não** pode se chamar `api/`.
- Ambiente local verificado: .NET SDK 10, Node 24, git, gh, Docker Desktop com WSL2 funcionando (`hello-world` rodou). Windows 11 Pro.
- APIs externas (todos os limites abaixo são **a confirmar nas páginas oficiais** antes de depender deles):
  - CoinGecko plano Demo: gratuito com chave e atribuição; fontes divergem sobre o limite por minuto (100 ou 30 req/min) e sobre o histórico disponível.
  - Banco Central SGS (Selic, CDI, IPCA, câmbio): gratuita, sem chave; códigos das séries a confirmar no portal do BCB.
  - brapi.dev (B3): gratuito com atraso de 15 min, provavelmente exigindo token e com limites; FIIs detalhados e Tesouro estariam em planos pagos.

## Abordagens consideradas
1. **Evoluir o que existe**: o backend só troca o `localStorage` por CRUD. Rápido, mas herda float, IDs com `Date.now()`, comparação de mês sem ano e histórico falso, e não resolve a digitação.
2. **Reconstruir o backend primeiro e manter a interface (escolhida pelo João)**: reaproveita o visual Grafite + lima e os componentes Vue; domínio, banco, importação e cálculos nascem novos no servidor.
3. **Recomeçar front e back**: descartada; o front está bom e o foco é backend.

Stack (deixada à minha escolha pelo João): **.NET 10 + PostgreSQL**. Alternativa descartada: Node/TypeScript.

## Mudanças da revisão 2 (resumo)
- Fatias reordenadas em verticais (Nubank primeiro, depois Itaú, depois conciliação); orçamento/metas e ações/FIIs saem da v2.0.
- Front real servido pelo Compose na mesma origem da API; a Vercel serve só o demo.
- Autenticação por cookie de sessão (ASP.NET Core Identity) em vez de JWT + refresh.
- Idempotência redefinida como multiconjunto por escopo, com entidade Fatura que substitui o conjunto.
- Saldo por asserções de saldo (checkpoints) e uma única fórmula de patrimônio.
- Dinheiro trafega como string decimal; datas como `DateOnly`.
- Seção de segurança de upload e de privacidade; exemplos deste documento tornados genéricos.
- Casos brasileiros adicionais na conciliação.

## Decisões de desenho

### Estrutura do repositório e topologia
- Monorepo. O front Vue continua na raiz (não mexer no deploy da Vercel). O backend entra em `backend/` com poucos projetos: API, Domínio, Infraestrutura e Testes. Os "módulos" (Identidade, Contas, Lançamentos, Importação, Conciliação, Categorização, Investimentos, Dados de mercado) são pastas e namespaces, não projetos separados.
- **Uso real (local):** `docker compose up` sobe Postgres, API e o front buildado atrás de um proxy reverso (nginx ou Caddy), tudo na **mesma origem**. Não há CORS nem cookie entre domínios. Em desenvolvimento, o Vite usa proxy para a API.
- **Demo público:** o front na Vercel continua abrindo em modo demo, sem backend (ver "Front-end").
- Postgres publicado só em `127.0.0.1`. Segredos em `.env` ignorado; o repositório traz `.env.example`. Nenhuma senha ou chave no `docker-compose.yml`.
- Dados reais ficam em volume Docker nomeado. Há script de `pg_dump`/restauração, porque resetar o Docker Desktop apaga o volume.

### Backend
- ASP.NET Core (.NET 10), EF Core, PostgreSQL, migrations versionadas.
- Jobs em segundo plano com `BackgroundService` (sem Hangfire): preços, snapshots diários e recorrências. O corte do "dia" usa `TimeProvider` e o fuso America/Sao_Paulo.
- **Autenticação:** ASP.NET Core Identity com cookie de sessão `httpOnly` na mesma origem, proteção antiforgery. Sem cadastro aberto: o usuário inicial é criado por comando (seed) ou variáveis de ambiente. Todo dado é isolado por usuário (`UserId` com filtro global de consulta) e há **teste de integração que prova o isolamento**.
- OpenAPI gerado, UI de documentação (Scalar ou Swagger) acessível, e cliente TypeScript gerado (NSwag ou openapi-typescript); a CI falha se o cliente gerado divergir do commitado.
- Imagem Docker com `tzdata` (Debian/Ubuntu, não Alpine).
- Observabilidade: logs estruturados, health check, rate limiting. **Nunca** logar descrição de lançamento, valores, nomes ou corpo de requisição; `EnableSensitiveDataLogging` desligado; mensagens de erro do parser citam número da linha e tipo de erro, nunca o conteúdo.

### Contrato de dinheiro e datas
- Dinheiro e quantidades trafegam na API como **string decimal**; o front nunca faz aritmética monetária (usa biblioteca decimal só para exibir). Valores em `numeric(18,2)`; quantidades e preços unitários de ativos em `numeric(38,18)`, com o total arredondado em 2 casas.
- Data do lançamento é `DateOnly` (coluna `date`), nunca `timestamptz`. Em OFX como `20260915000000[-3:BRT]`, usa-se a **data literal**, sem converter para UTC. Datas do xlsx (número serial do Excel) e valores são lidos como `decimal`, não double; texto como "1.234,56" é lido com cultura pt-BR.
- Cada lançamento guarda `data_compra` e `data_fatura` (competência x caixa); cada relatório declara qual usa.

### Modelo de dados (conceitual)
- **Conta**: tipo (corrente, cartão de crédito, carteira, corretora/cripto, empréstimo/passivo), instituição, moeda BRL. Cartão e empréstimo são contas de natureza passivo.
- **Cartão**: o cartão é uma conta; o "final do cartão" (físico, virtual, adicional) é atributo do lançamento, não uma conta separada.
- **Fatura**: conta-cartão, vencimento, total declarado, status. Importar uma fatura **substitui o conjunto de lançamentos dela**.
- **Lançamento**: conta, data, `data_compra`/`data_fatura`, valor com sinal, descrição original e normalizada, categoria, referência de origem (lote e identificador do banco, se houver), metadados opcionais (valor e moeda originais, IOF). O tipo "transferência" ou "neutro" é **derivado dos vínculos**, não campo livre, para evitar estados incoerentes.
- **Vínculo**: liga lançamentos (transferência, pagamento de fatura, par neutro do Pix parcelado); estados `sugerido`, `confirmado`, `rejeitado`.
- **Asserção de saldo (checkpoint)**: conta, data, valor, origem (`LEDGERBAL` do OFX ou total de fatura). Substitui o "ajuste de saldo inicial".
- **Grupo de parcelas**: compra parcelada, com parcela atual qualquer ("Parcela 4 de 10" na primeira importação), valor da parcela e arredondamento da última; parcelas futuras são lançamentos *previstos*, e o previsto vira realizado quando a parcela real chega (casamento por cartão, valor e descrição aproximada). A identidade do grupo tolera pequenas variações de descrição.
- **Importação (lote)**: hash do arquivo, fonte, período, resumo (novos, ignorados, pareados, avisos), estado. O arquivo original **não é guardado**, só o hash e as linhas lidas.
- **Regra de categorização**, **recorrência**, **meta**, **orçamento** (estes dois últimos na v2.1).
- **Ativo / posição / operação** (compra e venda) para cripto; ações, FIIs e renda fixa na v2.1. Custo médio ponderado, com política de taxas definida pelo construtor.
- **Preço (snapshot diário)** por ativo e **índice** (CDI, Selic, IPCA).

### Importação: fontes
As amostras reais ficam fora do git. O `.gitignore` ignora `finance-samples/`, `private/` e (a acrescentar) `*.ofx`, `*.qfx`, `*.xlsx`, com exceção explícita para `backend/tests/fixtures/`.

| Fonte | Formato | Particularidades |
|---|---|---|
| Itaú conta | OFX em SGML (folhas sem tag de fechamento), CP1252, CRLF | `FITID` é data + sequência do dia, estável só enquanto a ordem do dia não muda. Datas com sufixo de fuso. Descrições curtas e truncadas. Traz `LEDGERBAL`. |
| Itaú fatura cartão | xlsx | Cabeçalho (cartão, total, vencimento como serial do Excel), uma linha por lançamento: data, descrição, parcelamento, valor (cobrança positiva; pagamento e estorno negativos), final do cartão e tipo (físico ou virtual recorrente). Sem ID por transação, sem `LEDGERBAL`. |
| Nubank conta | OFX XML, UTF-8 (pode ter BOM) | `FITID` é UUID. O `MEMO` do Pix embute contraparte, documento mascarado e banco. |
| Nubank cartão | OFX XML | Cobrança é negativa; pagamento recebido é positivo; saldo negativo representa dívida. |

**Convenção de sinal normalizada** (feita no leitor de cada fonte): em conta ativa, entrada positiva e saída negativa; em conta passivo (cartão), cobrança negativa (aumenta a dívida) e pagamento positivo. O xlsx do Itaú vem com o sinal oposto e é invertido no leitor. A regra "o par de uma transferência soma zero" depende disso.

### Importação: regras
- Um leitor por fonte atrás de uma interface comum; o leitor de OFX é tolerante (não usa parser XML estrito) e decodifica pelo `CHARSET` do cabeçalho (registrar `CodePagesEncodingProvider` para CP1252).
- Fluxo: enviar arquivo, **prévia** (linhas lidas, já existentes, avisos), confirmar, gravar. Nada é gravado antes da confirmação.
- **Identidade e idempotência**
  - Nubank: o UUID do `FITID` é a identidade.
  - Itaú (OFX e fatura): a identidade é um **multiconjunto por escopo**. O escopo é (conta, data, chave normalizada) no OFX e (cartão, fatura) na fatura. A importação insere `max(0, n_no_arquivo − n_já_existentes)` por chave. Para fatura, reimportar **substitui o conjunto** dentro do escopo, em vez de deduplicar linha a linha.
  - No banco, um índice único `(conta, chave_origem, ordinal)` impede duplicação por requisições concorrentes.
  - Mesmo hash de arquivo já importado é no-op, mas o hash é **liberado ao desfazer o lote**, permitindo reimportar.
  - Itens que podem ter mudado entre exportações aparecem como "duvidoso" na prévia; nunca se grava duplicado em silêncio.
- **Conferências** (cada uma tem severidade e, quando aplicável, override explícito do usuário):

| Conferência | Severidade |
|---|---|
| Total da fatura = soma das cobranças e estornos (sem a linha de pagamento da fatura anterior) | Bloqueia; override com confirmação |
| Saldo final do OFX = saldo calculado, dado o checkpoint anterior | Avisa |
| Lacuna ou sobreposição entre períodos importados de uma conta | Avisa |
| Datas fora do período declarado | Avisa |
| Linha de tipo desconhecido | Vai para pendências |

### Segurança de upload
- Limite de tamanho por endpoint (ordem de 5 MB), um arquivo por requisição, validação por conteúdo e não só por extensão.
- xlsx é um zip: limitar tamanho descompactado, número de entradas e razão de compressão; rejeitar `.xlsm`; nunca avaliar fórmulas nem resolver links externos; leitura somente leitura. Licença: evitar EPPlus 5+ (Polyform); usar ClosedXML, ExcelDataReader ou o Open XML SDK.
- Parsers XML com `DtdProcessing = Prohibit` e `XmlResolver = null`. O leitor SGML tem limites de tamanho de campo e profundidade.
- Descrições e nomes de Pix são texto controlado por terceiros: nunca renderizar como HTML, e escapar em qualquer exportação.

### Conciliação (o diferencial)
Todos os vínculos entram como `sugerido` quando há ambiguidade e `confirmado` quando a regra é determinística.
- **Pagamento de fatura**: o lançamento "fatura paga" na conta casa com a linha de pagamento (valor negativo no xlsx) na fatura seguinte, por valor e janela de datas; o Nubank tem "Pagamento de fatura" na conta e "Pagamento recebido" no cartão. Vira transferência entre conta e cartão. Casos com pagamento parcial, a maior, rotativo, parcelamento de fatura, encargos/IOF/multa, pagamento por outro banco, boleto ou débito automático exigem casamento 1:N com tolerância ou ficam como pendência; nunca viram despesa em silêncio.
- **Estorno não é pagamento**: crédito por contestação é estorno de compra e reduz despesa.
- **Pix parcelado no cartão** (confirmado pelo João: funciona como compra no crédito, com juros): na conta aparece uma saída e um crédito liberado de mesmo valor e data referenciando o cartão; o efeito líquido na conta é zero e o par vira **neutro**. O gasto real é o conjunto de parcelas na fatura. Estornos desse crédito cancelam pares e podem ocorrer várias vezes no mesmo dia, então o pareamento suporta vários pares de mesmo valor. **Juros** = soma das parcelas menos o valor liberado, na categoria "Juros e encargos"; a ligação entre o valor liberado e o grupo de parcelas é sugerida (proximidade de data e valor) e confirmada pelo usuário.
- **Transferência entre contas próprias** (Itaú ↔ Nubank por Pix/TED): pareadas por valor, data e contraparte (documento do próprio usuário); é o caso neutro mais comum.
- **Aplicação e resgate automático, caixinhas e investimentos na conta**: tratados como transferência para uma posição, não despesa nem receita.
- Outros tipos: salário, rendimentos automáticos, crédito consignado ou empréstimo (passivo, não receita), boletos, compras internacionais (valor original e IOF como metadados). **Tipo desconhecido vai para pendências**, nunca para despesa ou receita por padrão.
- Parcelas e recorrências: cada compra parcelada é um grupo, e as parcelas futuras alimentam "quanto já está comprometido nos próximos meses". Assinaturas são detectadas por cartão virtual recorrente e descrições repetidas.
- **Desfazer lote**: remove definitivamente os lançamentos do lote e os vínculos que dependem deles; edições manuais dentro do lote geram aviso antes. Reimportar depois é permitido.

### Patrimônio
- Fórmula única: **patrimônio = Σ saldo das contas ativas + Σ saldo das contas passivo (negativo) + Σ valor das posições**. Não se subtrai "faturas em aberto" à parte.
- A dívida do cartão é o saldo da conta-cartão: soma das faturas importadas e ainda não quitadas pelos pagamentos conciliados. Parcelas futuras ficam fora do saldo e aparecem na tela de comprometido. Como o Itaú não traz `LEDGERBAL`, o checkpoint do cartão do Itaú é o total da fatura.
- Empréstimos são conta passivo e entram no patrimônio.
- Em cada importação com `LEDGERBAL`, cria-se um checkpoint; a conferência verifica continuidade entre checkpoints e detecta lacunas de período (na primeira importação o checkpoint define o saldo inicial, sem conferência tautológica).

### Categorização
Normalização da descrição (remover prefixos de adquirentes e marketplaces e a cidade colada no final) e **regras primeiro**. Correções manuais viram regras novas. IA fica para depois da v2.1.

### Investimentos e dados de mercado
- Cripto migra para o servidor: posições e operações no Postgres, custo médio ponderado e lucro calculados no servidor.
- Um job busca preços na CoinGecko uma vez por intervalo, com cache e retentativa em 429, e grava **snapshot diário** em BRL; o histórico da carteira e do patrimônio vem desses snapshots.
- CDI, Selic e IPCA via Banco Central SGS entram para comparação na v2.1, junto com ações e FIIs da B3 (brapi.dev) e renda fixa.

### Front-end
- Mantém a identidade Grafite + lima e os componentes. As stores do Pinia deixam de calcular e passam a consumir a API (cliente gerado).
- **Dois adaptadores atrás do mesmo contrato:** HTTP (uso real, local) e **estático** (demo). O demo é navegável e **somente leitura**, com fixtures geradas pelo backend a partir de uma semente sintética com **data de referência fixa**, cobrindo todos os recortes que a interface consulta (por mês). Não se reintroduz cálculo no navegador. A edição e adição que o demo permite hoje deixam de existir no demo.
- Telas novas (importar com prévia, pendências de conciliação, parcelas comprometidas, patrimônio) entram na fatia que cria o respectivo backend; no demo aparecem com exemplo fixo.
- O token/estado de sessão não fica em `localStorage` (cookie `httpOnly`).

### Testes e entrega
- Testes unitários dos leitores e da conciliação, com **fixtures sintéticas** geradas no formato dos arquivos reais. Testes de propriedade (property-based) para o pareamento (por exemplo, estornos e vários pares de mesmo valor) e para a idempotência. Teste de concorrência da importação.
- Testes de integração com Postgres real via Testcontainers; rodar o mesmo teste local e na CI (Linux), que divergem em tzdata, collation e fim de linha.
- `.gitattributes` com `*.ofx -text` (e fixtures binárias) para o `autocrlf` do Windows não corromper as fixtures.
- Suíte opcional contra os arquivos reais locais: roda só quando `finance-samples/` existe, é ignorada na CI e **nunca imprime descrições em asserts que falham**.
- CI no GitHub Actions: build, testes, geração e checagem do cliente OpenAPI, e varredura de segredos (gitleaks ou equivalente) também no pre-commit.
- Entrega visível para o portfólio: README com diagrama de arquitetura e ADRs curtos (numeric e `DateOnly`, como funciona a idempotência, por que a fatura substitui o conjunto), `docker compose up` com semente sintética, documentação da API acessível e um GIF do fluxo de importação e conciliação.

### Fatias de entrega
- **S0 Fundação mínima**: solução .NET, Compose com Postgres/API/proxy, health check, CI, autenticação por cookie, `.gitignore`/`.gitattributes`/gitleaks. O cliente OpenAPI gerado entra quando o front precisar dele.
- **S1 Núcleo + Nubank**: contas, categorias, lançamentos, checkpoints de saldo, resumo mensal no servidor, **importador do Nubank (OFX)** com prévia e desfazer; front com paridade da Visão geral e telas de contas, lançamentos e importar. Demo estático preservado.
- **S2 Itaú**: importadores do OFX da conta e do xlsx da fatura, com a identidade por multiconjunto, Fatura que substitui o conjunto, conferências e parcelas (grupos e projeção).
- **S3 Conciliação e categorização**: transferências, pagamento de fatura, Pix parcelado e juros, transferência entre contas próprias, aplicação/resgate, pendências, regras de categoria e recorrências. Antes disso a prévia mostra só duplicados e avisos.
- **S4 Investimentos e patrimônio**: cripto no servidor, job de preços e snapshots, patrimônio líquido e histórico real.
- **S5 API na nuvem + demo da API** (decisão de hospedagem adiada).
- **v2.1**: orçamento por categoria e metas, ações e FIIs (brapi), renda fixa, comparação com CDI/IPCA/Ibovespa, insights.
- **Depois**: Meu Pluggy (só João), PDF do Itaú, IA, Imposto de Renda, alertas, PWA.

## Decisões do João
- **Hospedagem na v2.0: só local**, com Docker Compose; a API na nuvem vira fatia própria depois da importação funcionar.
- **Sem dados a migrar**: a v2 começa vazia, sem exportar nem importar o `localStorage`.
- Opção 2 (backend primeiro, manter a interface), .NET e Postgres; Open Finance só para ele e só depois.

## Decisões minhas para o João conferir
- .NET 10 (versão instalada) em vez do 9.
- Backend em `backend/`; front permanece na raiz.
- **Cookie de sessão (Identity) em vez de JWT + refresh** (muda a proposta anterior).
- **Front real servido pelo Compose, na mesma origem**; a Vercel serve só o demo.
- **Demo somente leitura** com fixtures estáticas geradas pelo backend (muda o "como hoje" anterior).
- **Ações/FIIs e orçamento/metas saem da v2.0** e vão para a v2.1 (muda o corte combinado antes).
- Importação sempre com prévia e confirmação; arquivo original não é guardado.
- Conferências com severidade e override; tipo desconhecido vira pendência.
- Suíte local opcional contra arquivos reais, nunca na CI.

## O que fica para o construtor
Nomes dos projetos e pastas, lista exata de categorias padrão, mapeador, biblioteca de leitura de xlsx entre as permitidas, ferramenta de geração do cliente TypeScript, proxy reverso (nginx ou Caddy), índices e migrations, desenho fino das telas, política exata de pareamento (janelas de data e tolerâncias), política de taxas no custo médio de cripto, e valores finais dos limites de upload.
