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

## Como rodar localmente

Pré-requisitos: .NET 8 SDK, e Postgres + RabbitMQ rodando (`docker start postgres rabbitmq`).

```bash
dotnet restore
dotnet run
# ou, com hot reload:
dotnet watch run
```

Sobe em `http://localhost:5201`. Swagger em `/swagger`. As migrations do EF Core são aplicadas
automaticamente no startup. Requisições prontas em `ArcadeLink.Catalog.Api.http`
(games/library/purchase) — algumas exigem um token JWT obtido via `POST /auth/login` no Users.

## Configuração (variáveis de ambiente)

Os valores em `appsettings.json` são placeholders de desenvolvimento. Para sobrescrever (Docker,
compose, k8s), use a sintaxe de variável de ambiente do ASP.NET Core (`__` no lugar de `:`):

| Variável de ambiente | Descrição | Exemplo |
|---|---|---|
| `ConnectionStrings__Postgres` | Connection string do Postgres (`arcadelink_catalog`). | `Host=postgres;Port=5432;Database=arcadelink_catalog;Username=postgres;Password=postgres` |
| `Jwt__Issuer` | Issuer esperado do JWT — **precisa bater** com o emitido pelo Users. | `ArcadeLink.Users` |
| `Jwt__Audience` | Audience esperada do JWT — **precisa bater** com o Users. | `ArcadeLink` |
| `Jwt__Key` | Chave de assinatura para validar o JWT — **idêntica** à do Users (Etapa 6). | `dev-only-change-me-arcadelink-shared-jwt-signing-key-32bytes-min` |
| `RabbitMq__Host` | Host do broker. | `localhost` (dev) / `rabbitmq` (compose/k8s) |
| `RabbitMq__Username` | Usuário do broker. | `guest` |
| `RabbitMq__Password` | Senha do broker. | `guest` |

## Idempotência da biblioteca

`LibraryEntries` tem índice único em `(UserId, GameId)`. O consumer de `PaymentProcessedEvent`
primeiro checa se a entrada já existe (evita trabalho no caminho comum) e ainda assim captura
`DbUpdateException` no `SaveChangesAsync` como rede de segurança contra uma corrida entre entregas
concorrentes do mesmo evento — nos dois casos, o resultado é idempotente (nunca duplica).

## Docker

```bash
docker build -t arcadelink-catalog .
```

A imagem final (`mcr.microsoft.com/dotnet/aspnet:8.0`) roda como usuário não-root e escuta na porta
**8080** (`ASPNETCORE_URLS=http://+:8080`) — não é possível bindar na porta 80 sem root. Em
k8s/compose, mapeie a porta externa (ex.: 80) para o `targetPort: 8080` do container.

Ao subir sozinho (fora do compose, contra a infra local exposta no host):

```bash
docker run -d --network host --name arcadelink-catalog arcadelink-catalog
```

O container aplica as migrations do EF Core automaticamente na inicialização
(`Database.Migrate()` em `Program.cs`).

**Orquestração completa**: o `docker-compose.yml` que sobe a aplicação inteira (Postgres, RabbitMQ e
os 4 serviços) mora no repo âncora, `ArcadeLink.Users` — não há um `docker-compose.yml` neste repo.
Rode a partir de lá com os 4 repositórios clonados lado a lado (mesmo diretório pai); veja o
[README do ArcadeLink.Users](../ArcadeLink.Users/README.md#orquestração-completa-docker-compose)
para instruções completas.

## Kubernetes

Manifestos em `k8s/` (`deployment.yaml`, `service.yaml`, `configmap.yaml`, `secret.yaml`) —
`Deployment` usa `image: localhost/arcadelink-catalog:latest` com `imagePullPolicy: Never` (sem
registry, precisa ser carregada localmente no cluster antes do `kubectl apply`), `Service` expõe
`port: 80` → `targetPort: 8080`. Config não sensível (`Jwt:Issuer`/`Audience`, `RabbitMq:Host`/
`Username`) em `ConfigMap`; sensível (`ConnectionStrings:Postgres`, `Jwt:Key`, `RabbitMq:Password`)
em `Secret`.

Os manifestos de infraestrutura compartilhada (Postgres, RabbitMQ) ficam no repo âncora,
`ArcadeLink.Users/k8s/` — veja o
[README do ArcadeLink.Users](../ArcadeLink.Users/README.md#kubernetes) para o passo a passo completo
(build das imagens, carregar no cluster local, ordem de `kubectl apply`).

## Testes

```bash
dotnet test
```

`tests/ArcadeLink.Catalog.Api.Tests` (xUnit, 3 testes): `PaymentProcessedEventConsumerTests`, usando
`AddMassTransitTestHarness` (bus in-memory) + `Microsoft.EntityFrameworkCore.InMemory` para o
`CatalogDbContext` — cobre `Approved` adiciona à biblioteca, `Rejected` não adiciona nada, e
reprocessar um jogo já possuído não duplica (a idempotência descrita acima). Não precisa de
Postgres/RabbitMQ rodando.

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
