# ADR 0009: Regras de categoria

- Status: aceita
- Data: 2026-11-03

## Contexto
Depois das importações (Nubank, Itaú) e da conciliação (S3(a)/(b)), as linhas importadas chegam sem categoria e o usuário as categoriza uma a uma. A S3(c) cria regras "trecho da descrição → categoria" que preenchem só o que está vazio, sem nunca sobrescrever uma decisão do usuário.

## Decisão
- **Casamento**: o trecho (normalizado) tem que aparecer na descrição original (normalizada) **em limite de palavra**; "MERCADO" casa "MERCADO EXEMPLO", não "SUPERMERCADOS". **O trecho mais longo vence**; empate: a regra mais antiga.
- **Candidatas**: só linhas importadas (com lote), visíveis, `Normal`, sem categoria, **não editadas pelo usuário** e sem vínculo `Confirmed` (neutras). Linhas manuais e editadas sem categoria são decisão do usuário. O sinal deve combinar com o tipo da categoria (despesa para valor negativo, receita para positivo); por isso estorno não vira despesa categorizada.
- **Aplicação** (na confirmação do import, só no lote, e no botão "Aplicar regras"): roda sob o lock por usuário da conciliação. Um `ExecuteUpdate` com a guarda repetida no `WHERE` (sem categoria, `Normal`, não excluída, não editada, sinal) escreve a categoria; antes, as linhas são reservadas com `SELECT ... FOR NO KEY UPDATE SKIP LOCKED`. Motivo: esperar uma linha em edição pode dar deadlock (a edição, por sua vez, espera o lock por usuário); a linha pulada espera o próximo "Aplicar" e a edição a marca como do usuário de qualquer jeito. As categorias das regras são travadas `FOR KEY SHARE` até o commit, para a exclusão concorrente de categoria esperar e a chave estrangeira valer.
- **`EditedByUser` não é marcado**: categorizar por regra não é edição; a linha continua candidata a reconciliação e a desfazer lote sem aviso de edição.
- **Excluir regra não desfaz** as categorias que ela já deu (reverter é manual). **Excluir categoria apaga as regras dela em cascata** (e as categorias padrão recriadas na subida do app têm ids novos, sem regras).
- **API**: `GET/POST /api/category-rules`, `POST /apply`, `DELETE /{id}`; categoria inexistente (ou de outro usuário) responde 404, trecho inválido 400, duplicado 409. `matches` = linhas que a regra pegaria agora, ignorando precedência (pode somar mais do que o "Aplicar" categoriza).
- **Demo**: regras de exemplo vêm do serviço real na semente; criar, excluir e aplicar são somente leitura.

## Consequências
- Regras de pedaço de palavra não têm efeito (é preciso a palavra inteira).
- Sem prévia da categorização no import: o resultado aparece no `categorized` da confirmação e nas linhas.
- Sem edição de regra: exclua e crie de novo.
- Estornos e créditos de despesa ficam sem categoria (o tipo da categoria não combina com o sinal).
- Seguimentos conhecidos: (1) corrida preexistente edição × aplicar quando a edição mantém a categoria nula (`null` → `null`) pode marcar a linha como editada sem efeito prático; (2) `ReclassifyAsync` pode ter risco de deadlock com edições em voo, igual ao que a reserva `SKIP LOCKED` evita aqui.

## Alternativas consideradas
- Contém qualquer substring: "LOJA" pegaria "GLOJA"; limite de palavra erra menos.
- Aplicar automaticamente ao criar a regra: surpreende e é difícil de reverter; o usuário pede "Aplicar".
- Esperar o lock da linha em vez de `SKIP LOCKED`: risco de deadlock com edições.
- Marcar `EditedByUser` ao categorizar: tiraria a linha de futuras reconciliações sem o usuário ter decidido nada.
