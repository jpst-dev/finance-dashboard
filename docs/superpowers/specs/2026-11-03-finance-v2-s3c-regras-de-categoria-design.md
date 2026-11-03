# S3(c) Regras de categoria — design

Continua a S3 (spec pai `2026-11-02-finance-v2-backend-design.md`, fatias de entrega). Escopo escolhido pelo Claude em modo ausente (ver `docs/away-log.md`): **só regras de categoria**. Recorrências, Pix parcelado/juros e aplicação/resgate ficam para uma S3(d).

## Intenção e por quê
Hoje toda linha importada chega sem categoria (só o override do preview ou a edição manual dão categoria). O resumo mensal agrupa por categoria, então um mês real fica quase todo em "sem categoria". O objetivo é categorizar sozinho o que se repete (mercado, assinatura, combustível) sem nunca contrariar uma decisão do usuário.

## Objetivos
1. **Regra de categoria** do usuário: texto a procurar na descrição normalizada + categoria destino.
2. **Aplicação automática** nas linhas novas na confirmação da importação e sob demanda ("Aplicar regras"), só em linhas **sem categoria**.
3. **Atalho "criar regra a partir desta linha"** na edição de lançamento (sugere o texto; o usuário confirma). Nada de regra criada sozinha.
4. Tela **Regras**: listar, criar, excluir, ver quantas linhas sem categoria a regra pega agora.
5. Demo estático com regras de exemplo (somente leitura).

## Não-objetivos
- Recorrências, assinaturas detectadas, Pix parcelado/juros, aplicação/resgate (S3(d)).
- Regex, regras por valor/conta/data, regras compostas, ordem manual entre regras, edição de regra (excluir e recriar basta).
- Aprendizado automático de regras a partir de edições passadas.
- Mudar categoria já definida (por regra ou por import) e recategorizar em lote.

## Modelo
- `CategoryRule : IUserOwned { Id, UserId, Pattern, CategoryId, CreatedAt }`. `Pattern` guardado já normalizado (maiúsculas, sem acento, espaços colapsados, via `NameKeys.Normalize`), de 3 a 100 caracteres. `CategoryId` com FK `ON DELETE CASCADE` (excluir a categoria apaga as regras dela). Único `(UserId, Pattern)`.
- Casamento: a regra casa uma linha quando o `Pattern` aparece **contido** na descrição da linha normalizada do mesmo jeito (`NameKeys.Normalize(OriginalDescription)`). Sem regex, sem curinga.
- Precedência quando várias regras casam: o `Pattern` **mais longo** vence; empate: a regra mais antiga (`CreatedAt`, depois `Id`).
- Compatibilidade de tipo: categoria `Expense` só se aplica a valor negativo e `Income` só a valor positivo; senão a regra é ignorada para aquela linha (um estorno não vira "Mercado").

## Aplicação
- `CategoryRuleService.ApplyAsync(ct)` (por usuário, sob o mesmo `pg_advisory_xact_lock` por usuário dos vínculos para não competir com a reconciliação): candidatas = linhas visíveis, `Normal`, `CategoryId == null`, sem vínculo `Confirmed` (neutras não contam no resumo, então não precisam de categoria). Atribui a categoria da regra vencedora e **não** marca `EditedByUser` (a categoria veio de regra, a linha continua "do banco"). Retorna `ApplyResult(Applied)`.
- Na confirmação da importação: depois da reconciliação (dentro da mesma transação), roda `ApplyAsync` restrito às linhas do lote; o override de categoria do preview tem precedência (linha com categoria não é candidata). `ConfirmResult` ganha `Categorized`.
- `POST /api/category-rules/apply` aplica a todas as linhas candidatas do usuário e devolve `{ applied }`.
- Reaplicar nunca desfaz: apagar uma regra **não** remove categorias já atribuídas.

## API
- `GET /api/category-rules` → `[{ id, pattern, categoryId, categoryName, createdAt, matches }]` onde `matches` = linhas candidatas hoje (visíveis, `Normal`, sem categoria, sem vínculo `Confirmed`, sinal compatível) que a regra pegaria, sem considerar precedência.
- `POST /api/category-rules` `{ pattern, categoryId }` → 201 com a regra; 400 padrão para padrão fora de 3–100 caracteres ou categoria inexistente; 409 `duplicate_rule` se já existe o mesmo padrão normalizado.
- `DELETE /api/category-rules/{id}` → 204; 404 de outro usuário.
- `POST /api/category-rules/apply` → `{ applied }`.
- Escritas com antiforgery; 404 para recurso de outro usuário; OpenAPI atualizado (`required` correto).

## Front real
- Página **Regras** (rota e item de menu): tabela padrão/categoria/linhas que pega, formulário de nova regra (padrão + `CategorySelect`), botão excluir (com confirmação) e botão "Aplicar regras" com resumo "N linhas categorizadas" (plural correto).
- No formulário de edição de lançamento: depois de escolher uma categoria e salvar, link "Sempre categorizar «TRECHO» como CATEGORIA" que abre a criação da regra com o trecho sugerido já preenchido (a descrição normalizada sem números e datas finais, editável). O usuário confirma.
- Resultado da importação mostra "N categorizadas" quando > 0.
- Textos de terceiros sempre como texto (nunca HTML).

## Demo
`DemoSeed` ganha 2–3 regras de exemplo (ex.: "SUPERMERCADO EXEMPLO" → Alimentação) criadas pelo serviço real e aplicadas; linhas de exemplo ficam categorizadas por regra. Adaptador estático lista as regras (somente leitura); criar/excluir/aplicar respondem `readOnly()`.

## Decisões minhas (Rulings)
- Contém + mais longo vence: simples, previsível, explicável na tela; regex fica de fora.
- Só linhas `Normal` sem categoria e sem vínculo `Confirmed`: o resumo só agrupa essas.
- Regra não marca `EditedByUser`: reclassificação e "desfazer lote" tratam a linha como do banco.
- Apagar regra não desfaz categorias: reversão é manual; evita perda silenciosa.
- Aplicar na confirmação do import, dentro da transação: consistência com os vínculos; custo: import um pouco mais lento (consulta indexada pelo usuário).
- `matches` calculado na listagem em memória sobre as candidatas do usuário (volume pessoal pequeno); custo se errado: otimizar com filtro SQL depois.
- Sem edição de regra e sem ordem manual: excluir e recriar.

## Testes
Domínio: `CategoryRuleMatcher` puro (contém, normalização, mais longo vence, desempate, compatibilidade de sinal, padrão curto). Persistência: unicidade, cascata da categoria, filtro por usuário, migração `S3cCategoryRules` sobre dados da S3(b). Serviço: não sobrescreve categoria existente, ignora Pendente/oculta/neutra, não marca `EditedByUser`, idempotência, concorrência com reconciliação. Import: override do preview vence a regra; `Categorized` no resultado. API: CRUD, isolamento, antiforgery, 409, `apply`. Front: store, página, atalho na edição, plurais, XSS. Demo: fixtures e e2e sem chamada à API.

## O que fica para o construtor
Nomes exatos de arquivos, estilo dos testes, textos i18n e o formato exato do trecho sugerido (só precisa ser editável e vir sem datas/números finais).
