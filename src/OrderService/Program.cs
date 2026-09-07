using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Polly;
using Polly.Extensions.Http;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// EF Core DbContext Setup
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") 
                       ?? "Server=sqlserver-db,1433;Database=OrderDb;User Id=sa;Password=YourStrong@Passw0rd!;TrustServerCertificate=True;Encrypt=False;";

try
{
    builder.Services.AddDbContext<OrderDbContext>(options => options.UseSqlServer(connectionString));
}
catch
{
    builder.Services.AddDbContext<OrderDbContext>(options => options.UseInMemoryDatabase("OrderDbMemory"));
}

// POLLY RESILIENCE PATTERNS (Retry + Circuit Breaker)
builder.Services.AddHttpClient("ProductServiceClient")
    .AddPolicyHandler(GetRetryPolicy())
    .AddPolicyHandler(GetCircuitBreakerPolicy());

// Redis setup
var redisConnectionString = builder.Configuration.GetValue<string>("Redis:ConnectionString") ?? "localhost:6379";
builder.Services.AddSingleton<IConnectionMultiplexer>(sp => ConnectionMultiplexer.Connect(redisConnectionString));

var app = builder.Build();

var instanceId = Environment.GetEnvironmentVariable("INSTANCE_ID") ?? "OrderService-1";

// Ensure DB Created
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<OrderDbContext>();
    try { db.Database.EnsureCreated(); } catch { }
}

// Middleware to attach X-Instance-ID header
app.Use(async (context, next) =>
{
    context.Response.Headers.Append("X-Instance-ID", instanceId);
    await next();
});

app.UseSwagger();
app.UseSwaggerUI();

// GET /health & /orders/health - Health Check & Resilience Status
var healthHandler = (IConnectionMultiplexer redis, OrderDbContext dbContext) =>
{
    bool redisHealthy = redis.IsConnected;
    bool dbHealthy = dbContext.Database.CanConnect();
    return Results.Ok(new
    {
        Status = "Healthy",
        Instance = instanceId,
        Database = dbHealthy ? "Connected (SQL Server)" : "InMemory",
        Redis = redisHealthy ? "Connected" : "Disconnected",
        ResiliencePolicy = "Polly Retry (3x) & CircuitBreaker Active"
    });
};

app.MapMethods("/health", new[] { "GET", "HEAD" }, healthHandler);
app.MapMethods("/orders/health", new[] { "GET", "HEAD" }, healthHandler);

// POST /orders - Distributed Lock, Polly Resilience & SQL Server Persistence
app.MapPost("/orders", async (CreateOrderRequest request, OrderDbContext dbContext, IConnectionMultiplexer redis, IHttpClientFactory httpClientFactory, IConfiguration config) =>
{
    var redisDb = redis.GetDatabase();
    string lockKey = $"lock:product:{request.ProductId}";
    string lockValue = Guid.NewGuid().ToString();
    TimeSpan expiry = TimeSpan.FromSeconds(10);

    // 1. REDIS DISTRIBUTED LOCK EDİNME
    bool lockAcquired = await redisDb.LockTakeAsync(lockKey, lockValue, expiry);
    int retryCount = 0;
    while (!lockAcquired && retryCount < 5)
    {
        await Task.Delay(200);
        retryCount++;
        lockAcquired = await redisDb.LockTakeAsync(lockKey, lockValue, expiry);
    }

    if (!lockAcquired)
    {
        return Results.Json(new { Error = "Sistem yoğun. Lütfen siparişinizi tekrar deneyin (Lock Acquire Timeout)." }, statusCode: 429);
    }

    try
    {
        // 2. Product Service'ten Polly Resilience korumalı HTTP İsteği
        var productServiceUrl = config.GetValue<string>("Services:ProductService") ?? "http://localhost:5002";
        var httpClient = httpClientFactory.CreateClient("ProductServiceClient");

        var productResponse = await httpClient.GetAsync($"{productServiceUrl}/products/{request.ProductId}");
        if (!productResponse.IsSuccessStatusCode)
        {
            return Results.NotFound(new { Message = $"Ürün (ID: {request.ProductId}) bulunamadı." });
        }

        var productJson = await productResponse.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(productJson);
        var dataElement = doc.RootElement.GetProperty("data");
        int currentStock = dataElement.GetProperty("stock").GetInt32();
        string productName = dataElement.GetProperty("name").GetString() ?? "";

        if (currentStock < request.Quantity)
        {
            return Results.BadRequest(new { Message = $"Yetersiz stok! Mevcut stok: {currentStock}, Talep edilen: {request.Quantity}" });
        }

        // Stok Düşürme İsteği
        int newStock = currentStock - request.Quantity;
        var stockUpdatePayload = JsonContent.Create(new { NewStock = newStock });
        var updateStockResponse = await httpClient.PutAsync($"{productServiceUrl}/products/{request.ProductId}/stock", stockUpdatePayload);

        if (!updateStockResponse.IsSuccessStatusCode)
        {
            return Results.Problem("Stok güncellenirken hata oluştu.");
        }

        // 3. Siparişi SQL Server Veritabanına Kaydet (Persistence)
        var newOrder = new Order
        {
            UserId = request.UserId,
            ProductId = request.ProductId,
            ProductName = productName,
            Quantity = request.Quantity,
            CreatedAt = DateTime.UtcNow,
            Status = "Completed"
        };
        dbContext.Orders.Add(newOrder);
        await dbContext.SaveChangesAsync();

        // 4. REDIS PUB/SUB: Event Yayınla (OrderCreated)
        var sub = redis.GetSubscriber();
        var eventMessage = JsonSerializer.Serialize(new
        {
            OrderId = newOrder.Id,
            UserId = newOrder.UserId,
            ProductId = newOrder.ProductId,
            ProductName = newOrder.ProductName,
            Quantity = newOrder.Quantity,
            CreatedAt = newOrder.CreatedAt
        });
        await sub.PublishAsync(RedisChannel.Literal("order_created_channel"), eventMessage);

        return Results.Created($"/orders/{newOrder.Id}", new
        {
            Message = "Sipariş SQL Server'a başarıyla kaydedildi! Redis Lock serbest bırakıldı ve Pub/Sub eventi yayınlandı.",
            ProcessedByInstance = instanceId,
            Order = newOrder
        });
    }
    finally
    {
        await redisDb.LockReleaseAsync(lockKey, lockValue);
    }
});

// GET /orders
app.MapGet("/orders", async (OrderDbContext dbContext) =>
{
    var orders = await dbContext.Orders.ToListAsync();
    return Results.Ok(new { Instance = instanceId, Data = orders });
});

// GET /orders/user/{userId}
app.MapGet("/orders/user/{userId:int}", async (int userId, OrderDbContext dbContext) =>
{
    var userOrders = await dbContext.Orders.Where(o => o.UserId == userId).ToListAsync();
    return Results.Ok(userOrders);
});

app.Run();

// Polly Policies
static IAsyncPolicy<HttpResponseMessage> GetRetryPolicy()
{
    return HttpPolicyExtensions
        .HandleTransientHttpError()
        .WaitAndRetryAsync(3, retryAttempt => TimeSpan.FromMilliseconds(200 * retryAttempt));
}

static IAsyncPolicy<HttpResponseMessage> GetCircuitBreakerPolicy()
{
    return HttpPolicyExtensions
        .HandleTransientHttpError()
        .CircuitBreakerAsync(2, TimeSpan.FromSeconds(15));
}

public class OrderDbContext : DbContext
{
    public OrderDbContext(DbContextOptions<OrderDbContext> options) : base(options) { }
    public DbSet<Order> Orders => Set<Order>();
}

public class Order
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public int ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public DateTime CreatedAt { get; set; }
    public string Status { get; set; } = "Pending";
}

public class CreateOrderRequest
{
    public int UserId { get; set; }
    public int ProductId { get; set; }
    public int Quantity { get; set; }
}
