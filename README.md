# ArcadeLink.Catalog

Microsserviço de catálogo (CRUD de jogos e início/conclusão do fluxo de compra) da arquitetura de
microsserviços orientada a eventos do FIAP Cloud Games (FCG) — Tech Challenge Fase 2.

## Endpoints

| Método | Rota | Autorização | Descrição |
|---|---|---|---|
| GET | `/games` | Público | Lista os jogos do catálogo. |
| GET | `/games/{id}` | Público | Detalha um jogo. |
| POST | `/games` | `Admin` | Cria um jogo. |
| PUT | `/games/{id}` | `Admin` | Atualiza um jogo. |
| DELETE | `/games/{id}` | `Admin` | Remove um jogo. |
| GET | `/library` | Qualquer usuário autenticado | Lista os jogos da biblioteca do usuário logado (`sub` do JWT). |
| POST | `/games/{id}/purchase` | Qualquer usuário autenticado | Inicia a compra: publica `OrderPlacedEvent` (não adiciona o jogo ainda). Retorna 409 se o usuário já possui o jogo. |

O JWT validado aqui é o mesmo emitido pelo `ArcadeLink.Users` — `Jwt:Issuer`/`Jwt:Audience`/`Jwt:Key`
precisam ser idênticos entre os dois serviços (ver Etapa 6 do plano).

## Configuração (appsettings)

- `ConnectionStrings:Postgres` — string de conexão com o Postgres (`arcadelink_catalog`).
- `Jwt:Issuer` / `Jwt:Audience` / `Jwt:Key` — precisam bater exatamente com os valores do
  `ArcadeLink.Users` (que emite o token). Placeholder de desenvolvimento — externalizar via
  Secret/env var antes de produção (Etapa 6/9).
- `RabbitMq:Host` / `RabbitMq:Username` / `RabbitMq:Password` — conexão com o broker.

## Idempotência da biblioteca

`LibraryEntries` tem índice único em `(UserId, GameId)`. O consumer de `PaymentProcessedEvent`
primeiro checa se a entrada já existe (evita trabalho no caminho comum) e ainda assim captura
`DbUpdateException` no `SaveChangesAsync` como rede de segurança contra uma corrida entre entregas
concorrentes do mesmo evento — nos dois casos, o resultado é idempotente (nunca duplica).

## Contratos de eventos

Este serviço **publica** `OrderPlacedEvent` (ao iniciar uma compra) e **consome**
`PaymentProcessedEvent` (para adicionar o jogo à biblioteca do usuário quando aprovado).

Os contratos de eventos (namespace `ArcadeLink.Contracts`) **não são compartilhados via pacote
NuGet** — cada repositório mantém sua própria cópia idêntica (mesmo namespace, mesmo nome, mesmos
campos) em `src/ArcadeLink.Catalog.Api/Contracts/`. Isso é intencional e simplifica o desafio; o
MassTransit resolve os exchanges do RabbitMQ pelo nome completo do tipo, então qualquer divergência
entre a cópia deste repo e a dos demais serviços quebra a integração silenciosamente.

Eventos definidos aqui:

- `OrderPlacedEvent(Guid OrderId, Guid UserId, Guid GameId, decimal Price)` — consumido por
  `ArcadeLink.Payments`.
- `PaymentProcessedEvent(Guid OrderId, Guid UserId, Guid GameId, string Status)` — publicado por
  `ArcadeLink.Payments` (`Status`: `"Approved"` | `"Rejected"`).
