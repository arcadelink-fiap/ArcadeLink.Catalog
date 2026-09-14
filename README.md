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

## Autenticação via Gateway

A partir da Fase 3, este serviço **não valida JWT nenhum**. `GET /games`/`GET /games/{id}`
continuam públicos; `POST`/`PUT`/`DELETE /games`, `GET /library` e `POST /games/{id}/purchase`
agora confiam nos headers `X-User-Id`/`X-User-Role` injetados pelo API Gateway (Kong) depois de ele
validar o JWT — ver `src/ArcadeLink.Catalog.Api/Auth/KongHeaderAuthenticationHandler.cs` e
`Auth/KongHeaderPrincipalBuilder.cs`.

Fora do Kong (dev local direto, `dotnet run`/compose), simule a autenticação passando esses dois
headers manualmente em vez de um Bearer token:

```
X-User-Id: <guid do usuário>
X-User-Role: Admin
```

## Persistência poliglota (MongoDB)

`GET /games/{id}` combina o `Game` do Postgres com um documento `GameDetails` opcional no MongoDB
(`RichDescription`, `Tags`, `Screenshots`) — um enriquecimento de leitura, não crítico ao fluxo de
compra (o Postgres continua sendo a fonte da verdade de existência/preço). `POST`/`PUT /games`
aceitam esses três campos opcionalmente no corpo da requisição para popular o Mongo junto com a
escrita no Postgres. Uma falha no Mongo nunca derruba a resposta — é best-effort, ver
`Data/GameDetailsSync.cs` (`TryUpsertAsync`/`TryGetAsync` capturam qualquer exceção e logam um
warning em vez de propagar).

## Cache (Redis)

`GET /games` e `GET /games/{id}` usam cache-aside com TTL de 60s (chaves `games:all` e
`games:{id}`); qualquer escrita (`POST`/`PUT`/`DELETE /games`) invalida as chaves afetadas.

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
| `RabbitMq__Host` | Host do broker. | `localhost` (dev) / `rabbitmq` (compose/k8s) |
| `RabbitMq__Username` | Usuário do broker. | `guest` |
| `RabbitMq__Password` | Senha do broker. | `guest` |
| `Mongo__ConnectionString` | Connection string do MongoDB. | `mongodb://localhost:27017` (dev) / `mongodb://mongodb:27017` (k8s) |
| `Mongo__Database` | Nome do banco Mongo. | `arcadelink_catalog` |
| `Redis__ConnectionString` | Connection string do Redis (cache de consultas de jogos). | `localhost:6379` (dev) / `redis:6379` (k8s) |

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
registry, precisa ser carregada localmente no cluster antes do `kubectl apply`), `Service` (tipo
`ClusterIP`) expõe `port: 80` → `targetPort: 8080`. `RabbitMq:Host`/`Username` em `ConfigMap`;
`ConnectionStrings:Postgres`/`RabbitMq:Password` em `Secret`.

Os manifestos (`configmap.yaml`/`secret.yaml`) já incluem `Mongo__ConnectionString`/`Mongo__Database`/
`Redis__ConnectionString`, apontando para os Services `mongodb`/`redis` do
`ArcadeLink.Orchestration` — nenhuma edição manual é necessária antes de aplicar.

Os manifestos de infraestrutura compartilhada (Postgres, RabbitMQ) ficam no repo âncora,
`ArcadeLink.Users/k8s/`; os de Redis, MongoDB, Prometheus/Grafana e Kong ficam no
`ArcadeLink.Orchestration` — veja o
[README do ArcadeLink.Users](../ArcadeLink.Users/README.md#kubernetes) e o
[README do ArcadeLink.Orchestration](../ArcadeLink.Orchestration/README.md) para o passo a passo
completo (build das imagens, carregar no cluster local, ordem de `kubectl apply`).

## Testes

```bash
dotnet test
```

`tests/ArcadeLink.Catalog.Api.Tests` (xUnit, 16 testes): os 3 originais — `PaymentProcessedEventConsumerTests`,
usando `AddMassTransitTestHarness` (bus in-memory) + `Microsoft.EntityFrameworkCore.InMemory` para o
`CatalogDbContext` — cobre `Approved` adiciona à biblioteca, `Rejected` não adiciona nada, e
reprocessar um jogo já possuído não duplica (a idempotência descrita acima) — mais quatro grupos
novos da Fase 3: `GameDetailsSyncTests` (comportamento best-effort do enriquecimento via Mongo,
inclusive quando o repositório lança exceção), `GameResponseMapperTests` (combinação do `Game` do
Postgres com o `GameDetails` opcional do Mongo na resposta), `JsonDistributedCacheTests`
(serialização JSON do cache do Redis) e `KongHeaderPrincipalBuilderTests` (construção do
`ClaimsPrincipal` a partir dos headers `X-User-Id`/`X-User-Role`). Não precisa de
Postgres/RabbitMQ/Mongo/Redis rodando.

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
