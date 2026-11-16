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
| GET | `/search?q=&page=&size=` | Público | Busca no OpenSearch: fuzzy e ordenada por relevância (ver "Busca"). |
| POST | `/admin/search/reindex` | `Admin` | Reindexa todos os jogos do Postgres (+ `GameDetails` do Mongo) no OpenSearch. |
| GET | `/health` | Público | Health check dos probes do Kubernetes; devolve `status`, `service` e `version` (`APP_VERSION`, SHA curto). |

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
`games:{id}`); qualquer escrita (`POST`/`PUT`/`DELETE /games`) invalida as chaves afetadas. A resposta traz o
header **`X-Cache`**: `MISS` (veio do Postgres e foi gravada no cache), `HIT` (veio do Redis) ou `BYPASS` (Redis
inacessível ou com senha errada — a resposta vem normalmente do Postgres e a escrita não falha por causa da
invalidação). `ArcadeLink.Orchestration/scripts/demo-cache.sh` mostra a diferença de latência.

## Busca (OpenSearch)

Índice `games` no Amazon OpenSearch (local: container no compose do Users). Indexação no `POST`/`PUT /games`,
remoção no `DELETE` (que também apaga o `GameDetails` do Mongo); falhas de indexação são logadas e não derrubam
a escrita. `GET /search` consulta **só** o OpenSearch (sem fallback para o Postgres): `multi_match` em título
(boost 3), tags (2), gênero (2) e descrição, com `fuzziness: AUTO` (tolera erros de digitação, ex.: `zeldda`),
ordenado por relevância (`score`). Validação: `q` de 2 a 100 caracteres, `page` 1-100, `size` 1-50 (padrão 10) —
fora disso, 400 com mensagem. Sem `Search__Endpoint` ou com o OpenSearch indisponível, `/search` e o reindex
respondem **503** (ProblemDetails) e o resto do serviço funciona normalmente. Jogos criados enquanto a busca
estava desligada só aparecem depois de `POST /admin/search/reindex`. Demo: `ArcadeLink.Orchestration/scripts/demo-search.sh`.

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

## Segredos para desenvolvimento local (`dotnet user-secrets`)

Ao rodar com `dotnet run`, defina os valores sensíveis fora do repositório (gere senhas com `openssl rand -hex 24`):

```bash
cd src/ArcadeLink.Catalog.Api
dotnet user-secrets set "ConnectionStrings:Postgres" "Host=localhost;Port=5432;Database=arcadelink_catalog;Username=<usuario>;Password=<senha>"
dotnet user-secrets set "Mongo:ConnectionString" "mongodb://localhost:27017"
dotnet user-secrets set "RabbitMq:Username" "<usuario>"
dotnet user-secrets set "RabbitMq:Password" "<senha>"
dotnet user-secrets set "Redis:ConnectionString" "localhost:6379"
dotnet user-secrets set "Search:Endpoint" "http://localhost:9200"   # opcional; sem isso /search responde 503
```

## Configuração (variáveis de ambiente)

O `appsettings.json` contém apenas configuração **não sensível**; nenhuma senha, chave ou connection string fica no código-fonte. Valores sensíveis são injetados em runtime (compose: `.env`; k8s: Secrets criados por `ArcadeLink.Orchestration/scripts/create-secrets.sh`; dev local: `dotnet user-secrets`). Use a sintaxe de variável de ambiente do ASP.NET Core (`__` no lugar de `:`):

| Variável de ambiente | Descrição | Exemplo |
|---|---|---|
| `ConnectionStrings__Postgres` | Connection string do Postgres (`arcadelink_catalog`). | `Host=postgres;Port=5432;Database=arcadelink_catalog;Username=<usuario>;Password=<senha>` |
| `RabbitMq__Host` | Host do broker. | `localhost` (dev) / `rabbitmq` (compose/k8s) |
| `RabbitMq__Username` | Usuário do broker (sensível). | `<usuario>` |
| `RabbitMq__Password` | Senha do broker (sensível). | `<senha>` |
| `Mongo__ConnectionString` | Connection string do MongoDB. | `mongodb://localhost:27017` (dev) / definido via Secret (k8s, com autenticação) |
| `Mongo__Database` | Nome do banco Mongo. | `arcadelink_catalog` |
| `Redis__ConnectionString` | Connection string do Redis (cache de consultas de jogos; sensível no EKS). | `localhost:6379` (dev) / definido via Secret (k8s) |
| `Search__Endpoint` | URL do OpenSearch (vazio = busca desligada). Sensível por convenção: vai no Secret. | `http://localhost:9200` (dev) / `https://<vpc-endpoint>` (EKS) |
| `Search__IndexName` | Nome do índice. | `games` |
| `Search__UseAwsSigV4` | Assina as requisições com SigV4 (credenciais via Pod Identity); `false` no dev local. | `true` (EKS) / `false` (dev) |
| `Messaging__Transport` / `Messaging__AmazonSqsRegion` | `RabbitMq` (padrão) ou `AmazonSqs` (opcional) e a região. | `RabbitMq` / `us-east-2` |
| `APP_VERSION` | Versão exibida em `/health` (build-arg). | SHA curto |

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

## CI/CD e Kubernetes (EKS)

Em produção este serviço roda no **EKS** (`arcadelink-eks`, namespace `arcadelink`); o guia completo (Terraform,
segredos, ordem de execução, custos, troubleshooting) está no
[README do ArcadeLink.Orchestration](../ArcadeLink.Orchestration/README.md).

**Pipeline** (`.github/workflows/ci-cd.yml`, GitHub Actions; autenticação na AWS por OIDC, sem access keys):

```
pull request  ->  Build e testes (dotnet test)
push na main  ->  Build e testes -> docker build (tag = SHA do commit) -> Trivy (CRITICAL) -> push no ECR `arcadelink/catalog`
                  -> kubectl apply (rolling update) -> rollout status (falhou? rollout undo)
```

- **Tag da imagem** = SHA completo do commit; o SHA curto entra como `APP_VERSION` (build-arg) e aparece em
  `GET /health` -> `{"status":"healthy","service":"catalog-api","version":"<sha curto>"}`.
- **Sem downtime**: `strategy.rollingUpdate` com `maxUnavailable: 0` e `maxSurge: 1`, `startupProbe`/
  `readinessProbe`/`livenessProbe` em `GET /health` (porta 8080). Se a nova versão não ficar pronta, os pods
  antigos continuam atendendo, o pipeline falha e executa `kubectl rollout undo`.
- O deploy só roda em push na `main`. Variables necessárias no repositório do GitHub: `AWS_REGION`,
  `AWS_ROLE_ARN`, `EKS_CLUSTER_NAME`, `ECR_REGISTRY` (sem Secrets neste repo — os segredos ficam no Orchestration).
- Manifestos em `k8s/` (`deployment.yaml` com `${IMAGE}` substituído por `envsubst`, `service.yaml`,
  `configmap.yaml`, `serviceaccount.yaml`). O ConfigMap contém só configuração **não sensível**; credenciais e
  connection strings vêm do Secret `catalog-api-secret` (via `envFrom`), criado pelo `deploy-platform` do Orchestration
  a partir dos GitHub Secrets. O ConfigMap opcional `messaging-config` escolhe o transporte (`Messaging__Transport`:
  `RabbitMq` padrão ou `AmazonSqs`, ver Orchestration).
- O acesso ao OpenSearch usa **EKS Pod Identity** (ServiceAccount `catalog-api`), sem access keys.
- Postgres, RabbitMQ, Redis, MongoDB, Kong e observabilidade vivem em `ArcadeLink.Orchestration/k8s/`
  (desde a Fase 4, não mais aqui).

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
