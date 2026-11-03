# ADR 0002: Cookie de sessão na mesma origem em vez de JWT

- Status: aceita
- Data: 2026-11-02

## Contexto
A API guarda dados financeiros pessoais e tem praticamente um único usuário, sem cadastro aberto. O front real e a API podem ser servidos pela mesma origem pelo Docker Compose (nginx servindo o front buildado e repassando `/api/` para a API). Em desenvolvimento, o proxy do Vite cumpre o mesmo papel. O site público na Vercel é só um demo sem backend.

## Decisão
- Autenticação com **ASP.NET Core Identity e cookie de sessão** `httpOnly`, com **proteção antiforgery** (o token vem de `/api/auth/csrf`).
- **Mesma origem**: front e API atrás do mesmo host e porta (`http://127.0.0.1:8080`). Sem CORS e sem cookie entre domínios.
- Sem cadastro aberto: o usuário inicial é criado por variáveis de ambiente (`SEED_USER_EMAIL`, `SEED_USER_PASSWORD`).
- Rate limit no `POST /api/auth/login`, por IP do cliente (as demais rotas de autenticação não têm rate limit).

## Consequências
- Nenhum token fica acessível a JavaScript (menos superfície para XSS) e não há fluxo de refresh, rotação ou revogação para manter.
- O front real **só funciona** servido junto da API. A Vercel serve apenas o demo; não há login lá.
- Atrás de um proxy com TLS (hospedagem futura), será preciso marcar o cookie como `Secure` e configurar os cabeçalhos encaminhados (`X-Forwarded-For`/`X-Forwarded-Proto`) para a API enxergar HTTPS e o IP real.
- O rate limit por IP depende desses cabeçalhos: o nginx sobrescreve `X-Forwarded-For` com o IP de origem, para o cliente não forjar o próprio IP.
- Clientes não-navegador (app nativo, scripts) teriam de lidar com cookie e antiforgery; hoje isso está fora do escopo.

## Alternativas consideradas
- **JWT + refresh token**: descartado. Exige armazenar o token no navegador (ou mais um cookie), rotação, revogação e CORS, tudo para um único usuário em uma só origem.
- **Cookie entre domínios (Vercel + API separada)**: descartado. Traz CORS, `SameSite=None` e mais risco de CSRF sem ganho real.
- **Autenticação por provedor externo (OIDC)**: desnecessária para um usuário local e traz dependência de serviço externo, contra a restrição de custo zero.
