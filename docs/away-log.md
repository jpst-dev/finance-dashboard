# Diário do modo ausente

Decisões tomadas pelo Claude sem o João. Formato: data — decisão — por quê — consultado — custo se errada / como desfazer.
Regras em [away-mode.md](away-mode.md).

- 2026-11-03 — Criado o modo ausente com autorização ampla do João e travas de dados sensíveis — pedido direto do João — ninguém consultado — se as travas forem fortes demais ou fracas demais, editar `docs/away-mode.md`.
- 2026-11-03 — PR #2 (S1b) já estava mergeado na `main` quando fui concluí-lo (merge commit 14149dd, CI verde no Linux) — o João fez o merge — n/a — nada a desfazer.
- 2026-11-03 — Criado .claude/settings.json com lista de comandos pré-aprovados (exatos para testes/builds; git add/commit/checkout/push origin; gh pr create/merge; gitleaks; docker compose sem down/-v; curl só 127.0.0.1) — pedido do João — ninguém — remover linhas do arquivo para endurecer.
- 2026-11-03 — Definido o escopo do período ausente: S2 e depois S1c completas, depois parar (sem S3) — resposta do João — n/a — editar a seção 'Escopo e parada' de docs/away-mode.md.
- 2026-11-03 — S2 (Itaú): design escrito com 2 conselheiros independentes (simplicidade × correção) e builder check; decisões e racional em docs/superpowers/specs/2026-11-03-finance-v2-s2-itau-design.md (seções 'Decisões minhas' e 'Decisões complementares'). Principais: conferência de total bloqueia (validada em 2 faturas reais), PIX CARTAO e PAG BOLETO pendentes, parcela>1 datada em vencimento−1 mês com data original guardada, fatura com linhas faltando bloqueia (desfazer lote antigo), sem checkpoint de cartão, xlsx via Open XML SDK. Custo se erradas: ajustar regras de texto/datas (sem migração de dados, pois a data original é guardada) — consultados: 2 agentes opus + builder check.
