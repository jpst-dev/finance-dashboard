# Modo ausente (away mode)

Regras para o Claude continuar o projeto enquanto o João está fora, olhando só de vez em quando pelo celular (Remote Control). Vale quando o João disser "modo ausente" / "estou fora" na conversa, ou quando esta conversa o citar. Em qualquer outro caso, as regras normais de trabalho continuam.

## Autorização do João (2026-11-03)
"Te autorizo a qualquer coisa, apenas tomar as medidas de segurança com dados sensíveis." Ou seja: o Claude decide sozinho, faz merge/push, roda a S2 e S3 inteiras com o fluxo superpowers, e só para pelas travas abaixo.

## Travas (nunca flexibilizar)
1. **Dados sensíveis.** Nada de `finance-samples/`, `.env`, senhas, extratos ou valores reais em commit, log, saída de terminal, mensagem, issue, PR, fixture ou teste. Amostras reais podem ser lidas só para descobrir **estrutura** (campos, formato, codificação, tipos de linha), sem imprimir conteúdo; fixtures são sempre sintéticas.
2. **Nada de dado real para fora da máquina.** Consultores externos, subagentes de pesquisa e busca na web recebem só o problema descrito em abstrato, nunca trechos de amostra, valores, nomes, documentos ou chaves.
3. **Banco local.** Nunca `docker compose down -v`, `DROP`, apagar volume ou migração destrutiva sem backup feito antes (`backups/` é git-ignored) e sem teste de restauração do backup.
4. **Segredos e sessão:** não afrouxar autenticação, antiforgery, cookies, CORS ou rate limit; não desligar hooks (gitleaks) nem `--no-verify`; gitleaks limpo antes de cada push.
5. **Sem force-push** na `main`, sem reescrever histórico publicado, sem apagar branches que não sejam as do próprio trabalho já mergeado.
6. **Parar e esperar o João** (registrar no diário, resumir em 3 linhas) quando: o plano está tão quebrado que todo caminho é chute; um problema de segurança real aparece; algo exigiria gastar dinheiro ou enviar dados para um serviço externo; ou a mesma tarefa falha 3 vezes seguidas sem diagnóstico novo.

## O que o Claude decide sozinho
Escolhas de design dentro do spec pai (`docs/superpowers/specs/2026-11-02-finance-v2-backend-design.md`), nomes, estrutura, bibliotecas já alinhadas com a stack, ordem de tarefas, correções de revisão, merge de PR com CI verde e revisão final limpa, push para a `main` de docs e de fatias revisadas.

## Como decidir quando houver dúvida
1. Se o spec responde, siga o spec.
2. Se não, escreva um briefing curto e **abstrato** (problema, 2–3 opções, custo de cada uma, sem dados reais) e mande para um agente novo, sem o contexto da sessão, pedindo recomendação independente. Para decisões de produto/arquitetura que mudam o resultado, peça a opinião de 2 agentes com enfoques diferentes (p. ex. "simplicidade e manutenção" × "correção e segurança").
3. Decida pelo critério que o João já deu: "o que der melhor resultado final para o projeto"; prefira a opção mais simples e reversível quando empatar.
4. Registre no diário (abaixo). Uma decisão sem registro é uma decisão escondida.

## Diário: `docs/away-log.md`
Uma linha por decisão: data, o que decidiu, por quê, quem foi consultado, **o que custa se estiver errada** e como desfazer. Commitar junto com o trabalho. O João lê pelo celular e desfaz o que não gostar. Ao fim de cada fatia, o Claude escreve no diário um resumo de 5 linhas: o que foi entregue, o que foi decidido sem ele, o que precisa do olhar dele.

## Fluxo
Fatia nova: brainstorming (perguntas ao João viram decisões registradas, com a recomendação do Claude como padrão) → design + builder check → plano → execução com subagentes (revisão por tarefa e final) → PR → CI verde → merge. As regras do skill de execução ("rulings, not stalls") valem integralmente.

## Pedidos de permissão do Claude Code
O modo ausente não desliga os pedidos de permissão da ferramenta. Se a ferramenta pedir aprovação, o João pode aprovar pelo celular; para reduzir isso, o projeto pode ter uma lista de comandos pré-aprovados em `.claude/settings.json` (testes, builds, git local, docker compose sem `down -v`).

## Escopo e parada (definido pelo João em 2026-11-03)
Neste período ausente: **S2 (importador do Itaú) completa** — brainstorm, design, plano, execução, PR, CI verde, merge — e **depois a S1c** (demo estático que substitui o legado) completa, do mesmo jeito. **Depois da S1c, parar**: escrever o resumo de 5 linhas no `away-log.md`, atualizar a memória e não começar a S3 (conciliação) nem outra fatia até o João voltar; a S3 tem decisões de produto grandes que ele quer acompanhar. Se ficar sem trabalho antes dele voltar, não inventar escopo: no máximo preparar perguntas objetivas para ele no diário.

## Novo escopo ausente (definido pelo João em 2026-11-03, depois da S1c)
O João voltou, fez o brainstorm da S3 com o Claude e pediu para executar em modo ausente **só a S3(a)**: pagamento de fatura (conta ↔ cartão, Nubank e Itaú) e transferência entre contas próprias (Itaú ↔ Nubank). Decisões dele: pareamento dentro da confirmação da importação, com tabela de vínculos (caminho A); par exato 1:1 sem ambiguidade confirma sozinho, com opção de desfazer; ambíguo vira `sugerido`; parcial/a maior/rotativo/1:N ficam como pendência. **Depois da S3(a), parar** (resumo de 5 linhas no `away-log.md`, memória atualizada): S3(b) (Pix parcelado, aplicação/resgate) e S3(c) (categorização, recorrências) esperam o João.

## Novo escopo ausente (definido pelo João em 2026-11-03, depois da S1c)
O João voltou, fez o brainstorm da S3 com o Claude e pediu para executar em modo ausente **só a S3(a)**: pagamento de fatura (conta ↔ cartão, Nubank e Itaú) e transferência entre contas próprias (Itaú ↔ Nubank). Decisões dele: pareamento dentro da confirmação da importação, com tabela de vínculos (caminho A); par exato 1:1 sem ambiguidade confirma sozinho, com opção de desfazer; ambíguo vira `sugerido`; parcial/a maior/rotativo/1:N ficam como pendência. **Depois da S3(a), parar** (resumo de 5 linhas no `away-log.md`, memória atualizada): S3(b) (Pix parcelado, aplicação/resgate) e S3(c) (categorização, recorrências) esperam o João.
