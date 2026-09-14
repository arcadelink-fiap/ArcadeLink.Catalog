using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using ArcadeLink.Catalog.Api.Auth;
using ArcadeLink.Catalog.Api.Caching;
using ArcadeLink.Catalog.Api.Consumers;
using ArcadeLink.Catalog.Api.Data;
using ArcadeLink.Catalog.Api.Domain;
using ArcadeLink.Catalog.Api.Models;
using ArcadeLink.Contracts;
using MassTransit;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.OpenApi.Models;
using MongoDB.Driver;
using Prometheus;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "ArcadeLink Catalog API",
        Version = "v1"
    });

    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Cole apenas o token JWT. O prefixo 'Bearer ' é adicionado automaticamente."
    });

    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

builder.Services.AddDbContext<CatalogDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Postgres")));

builder.Services.AddSingleton<IMongoClient>(_ =>
    new MongoClient(builder.Configuration["Mongo:ConnectionString"]));
builder.Services.AddScoped<IGameDetailsRepository, GameDetailsRepository>();

builder.Services.AddStackExchangeRedisCache(options =>
    options.Configuration = builder.Configuration["Redis:ConnectionString"]);

builder.Services.AddAuthentication(KongHeaderPrincipalBuilder.SchemeName)
    .AddScheme<AuthenticationSchemeOptions, KongHeaderAuthenticationHandler>(
        KongHeaderPrincipalBuilder.SchemeName, options => { });

builder.Services.AddAuthorization();

builder.Services.AddMassTransit(x =>
{
    // Prefixed so this service's receive endpoint names never collide with another service's
    // consumer of the same class name on the shared RabbitMQ vhost (e.g. PaymentProcessedEventConsumer
    // also exists in ArcadeLink.Notifications) — without this, MassTransit's default endpoint naming
    // would give both services literally the same queue, turning fan-out into competing consumers.
    x.SetEndpointNameFormatter(new KebabCaseEndpointNameFormatter("catalog", false));

    x.AddConsumer<PaymentProcessedEventConsumer>();

    x.UsingRabbitMq((context, cfg) =>
    {
        cfg.Host(builder.Configuration["RabbitMq:Host"], "/", h =>
        {
            h.Username(builder.Configuration["RabbitMq:Username"]!);
            h.Password(builder.Configuration["RabbitMq:Password"]!);
        });

        cfg.ConfigureEndpoints(context);
    });
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    scope.ServiceProvider.GetRequiredService<CatalogDbContext>().Database.Migrate();
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseExceptionHandler(errorApp =>
{
    errorApp.Run(async context =>
    {
        var feature = context.Features.Get<IExceptionHandlerFeature>();
        var logger = context.RequestServices.GetRequiredService<ILogger<Program>>();
        logger.LogError(feature?.Error, "Unhandled exception while processing {Path}", context.Request.Path);

        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        context.Response.ContentType = "application/problem+json";
        await context.Response.WriteAsJsonAsync(new
        {
            title = "An unexpected error occurred.",
            status = StatusCodes.Status500InternalServerError
        });
    });
});

app.UseHttpsRedirection();
app.UseHttpMetrics();
app.UseAuthentication();
app.UseAuthorization();
app.MapMetrics();

app.MapGet("/games", async (CatalogDbContext db, IDistributedCache cache) =>
{
    var cached = await JsonDistributedCache.GetAsync<List<GameResponse>>(cache, "games:all");
    if (cached is not null)
        return Results.Ok(cached);

    var games = await db.Games.OrderBy(g => g.Title).ToListAsync();
    var response = games.Select(g => GameResponseMapper.ToResponse(g)).ToList();
    await JsonDistributedCache.SetAsync(cache, "games:all", response, TimeSpan.FromSeconds(60));
    return Results.Ok(response);
})
.WithName("ListGames")
.WithOpenApi();

app.MapGet("/games/{id:guid}", async (
    Guid id,
    CatalogDbContext db,
    IGameDetailsRepository detailsRepo,
    ILogger<Program> logger,
    IDistributedCache cache) =>
{
    var cacheKey = $"games:{id}";
    var cached = await JsonDistributedCache.GetAsync<GameResponse>(cache, cacheKey);
    if (cached is not null)
        return Results.Ok(cached);

    var game = await db.Games.FindAsync(id);
    if (game is null)
        return Results.NotFound();

    var details = await GameDetailsSync.TryGetAsync(detailsRepo, logger, id);
    var response = GameResponseMapper.ToResponse(game, details);
    await JsonDistributedCache.SetAsync(cache, cacheKey, response, TimeSpan.FromSeconds(60));
    return Results.Ok(response);
})
.WithName("GetGame")
.WithOpenApi();

app.MapPost("/games", async (
    GameRequest request,
    CatalogDbContext db,
    IGameDetailsRepository detailsRepo,
    ILogger<Program> logger,
    IDistributedCache cache) =>
{
    if (string.IsNullOrWhiteSpace(request.Title))
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["title"] = ["Title is required."] });

    if (request.Price < 0)
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["price"] = ["Price cannot be negative."] });

    var game = new Game
    {
        Id = Guid.NewGuid(),
        Title = request.Title,
        Description = request.Description,
        Genre = request.Genre,
        Price = request.Price,
        CreatedAtUtc = DateTime.UtcNow
    };

    db.Games.Add(game);
    await db.SaveChangesAsync();

    var details = await SyncGameDetailsIfProvided(request, game.Id, detailsRepo, logger);

    await cache.RemoveAsync("games:all");
    await cache.RemoveAsync($"games:{game.Id}");

    return Results.Created($"/games/{game.Id}", GameResponseMapper.ToResponse(game, details));
})
.RequireAuthorization(policy => policy.RequireRole(Roles.Admin))
.WithName("CreateGame")
.WithOpenApi();

app.MapPut("/games/{id:guid}", async (
    Guid id,
    GameRequest request,
    CatalogDbContext db,
    IGameDetailsRepository detailsRepo,
    ILogger<Program> logger,
    IDistributedCache cache) =>
{
    var game = await db.Games.FindAsync(id);
    if (game is null)
        return Results.NotFound();

    if (string.IsNullOrWhiteSpace(request.Title))
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["title"] = ["Title is required."] });

    if (request.Price < 0)
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["price"] = ["Price cannot be negative."] });

    game.Title = request.Title;
    game.Description = request.Description;
    game.Genre = request.Genre;
    game.Price = request.Price;

    await db.SaveChangesAsync();

    var details = await SyncGameDetailsIfProvided(request, game.Id, detailsRepo, logger);

    await cache.RemoveAsync("games:all");
    await cache.RemoveAsync($"games:{game.Id}");

    return Results.Ok(GameResponseMapper.ToResponse(game, details));
})
.RequireAuthorization(policy => policy.RequireRole(Roles.Admin))
.WithName("UpdateGame")
.WithOpenApi();

app.MapDelete("/games/{id:guid}", async (Guid id, CatalogDbContext db, IDistributedCache cache) =>
{
    var game = await db.Games.FindAsync(id);
    if (game is null)
        return Results.NotFound();

    db.Games.Remove(game);
    await db.SaveChangesAsync();

    await cache.RemoveAsync("games:all");
    await cache.RemoveAsync($"games:{id}");

    return Results.NoContent();
})
.RequireAuthorization(policy => policy.RequireRole(Roles.Admin))
.WithName("DeleteGame")
.WithOpenApi();

app.MapGet("/library", async (ClaimsPrincipal user, CatalogDbContext db) =>
{
    var userId = GetUserId(user);

    var entries = await (
        from entry in db.LibraryEntries
        join game in db.Games on entry.GameId equals game.Id
        where entry.UserId == userId
        orderby entry.AcquiredAtUtc
        select new LibraryEntryResponse(game.Id, game.Title, entry.AcquiredAtUtc)
    ).ToListAsync();

    return Results.Ok(entries);
})
.RequireAuthorization()
.WithName("GetLibrary")
.WithOpenApi();

app.MapPost("/games/{id:guid}/purchase", async (
    Guid id,
    ClaimsPrincipal user,
    CatalogDbContext db,
    IPublishEndpoint publishEndpoint,
    ILogger<Program> logger) =>
{
    var userId = GetUserId(user);

    var game = await db.Games.FindAsync(id);
    if (game is null)
        return Results.NotFound();

    var alreadyOwned = await db.LibraryEntries.AnyAsync(e => e.UserId == userId && e.GameId == id);
    if (alreadyOwned)
        return Results.Conflict(new { title = "User already owns this game." });

    var orderId = Guid.NewGuid();
    await publishEndpoint.Publish(new OrderPlacedEvent(orderId, userId, game.Id, game.Price));
    logger.LogInformation(
        "Pedido {OrderId} publicado: usuário {UserId} comprando jogo {GameId} por {Price}",
        orderId, userId, game.Id, game.Price);

    return Results.Accepted(value: new PurchaseResponse(orderId, game.Id, game.Price));
})
.RequireAuthorization()
.WithName("PurchaseGame")
.WithOpenApi();

app.Run();

static Guid GetUserId(ClaimsPrincipal user)
{
    var value = user.FindFirstValue(JwtRegisteredClaimNames.Sub) ?? user.FindFirstValue(ClaimTypes.NameIdentifier);
    return Guid.Parse(value!);
}

static async Task<GameDetails?> SyncGameDetailsIfProvided(
    GameRequest request, Guid gameId, IGameDetailsRepository detailsRepo, ILogger logger)
{
    if (request.RichDescription is null && request.Tags is null && request.Screenshots is null)
        return null;

    var details = new GameDetails
    {
        GameId = gameId,
        RichDescription = request.RichDescription,
        Tags = request.Tags ?? new List<string>(),
        Screenshots = request.Screenshots ?? new List<string>()
    };
    await GameDetailsSync.TryUpsertAsync(detailsRepo, logger, details);
    return details;
}