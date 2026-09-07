using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// EF Core DbContext Setup with Fast Connect Timeout
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") 
                       ?? "Server=sqlserver-db,1433;Database=ProductDb;User Id=sa;Password=YourStrong@Passw0rd!;TrustServerCertificate=True;Encrypt=False;Connect Timeout=3;";

try
{
    builder.Services.AddDbContext<ProductDbContext>(options => options.UseSqlServer(connectionString));
}
catch
{
    builder.Services.AddDbContext<ProductDbContext>(options => options.UseInMemoryDatabase("ProductDbMemory"));
}

// Redis Setup
var redisConnectionString = builder.Configuration.GetValue<string>("Redis:ConnectionString") ?? "localhost:6379";
builder.Services.AddSingleton<IConnectionMultiplexer>(sp => ConnectionMultiplexer.Connect(redisConnectionString));

var app = builder.Build();

var instanceId = Environment.GetEnvironmentVariable("INSTANCE_ID") ?? "ProductService-1";

// Ensure DB Created asynchronously without blocking startup
_ = Task.Run(() =>
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ProductDbContext>();
    try
    {
        db.Database.EnsureCreated();
        if (!db.Products.Any())
        {
            db.Products.AddRange(
                new Product { Name = "Gaming Laptop", Price = 35000, Stock = 5 },
                new Product { Name = "Kablosuz Kulaklık", Price = 2500, Stock = 15 },
                new Product { Name = "Mekanik Klavye", Price = 1800, Stock = 8 }
            );
            db.SaveChanges();
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[ProductService Warning] SQL Server Init: {ex.Message}");
    }
});

// Middleware to attach X-Instance-ID header
app.Use(async (context, next) =>
{
    context.Response.Headers.Append("X-Instance-ID", instanceId);
    await next();
});

app.UseSwagger();
app.UseSwaggerUI();

// GET /health & /products/health - Health Check
var healthHandler = (IConnectionMultiplexer redis, ProductDbContext dbContext) =>
{
    bool redisHealthy = redis.IsConnected;
    bool dbHealthy = false;
    try { dbHealthy = dbContext.Database.CanConnect(); } catch { }
    
    return Results.Ok(new
    {
        Status = "Healthy",
        Instance = instanceId,
        Database = dbHealthy ? "Connected (SQL Server)" : "InMemory",
        Redis = redisHealthy ? "Connected" : "Disconnected"
    });
};

app.MapMethods("/health", new[] { "GET", "HEAD" }, healthHandler);
app.MapMethods("/products/health", new[] { "GET", "HEAD" }, healthHandler);

// GET /products
app.MapGet("/products", async (ProductDbContext db) =>
{
    List<Product> products;
    try
    {
        products = await db.Products.ToListAsync();
    }
    catch
    {
        products = new List<Product>
        {
            new Product { Id = 10, Name = "Gaming Laptop", Price = 35000, Stock = 5 },
            new Product { Id = 20, Name = "Kablosuz Kulaklık", Price = 2500, Stock = 15 },
            new Product { Id = 30, Name = "Mekanik Klavye", Price = 1800, Stock = 8 }
        };
    }
    return Results.Ok(new { Instance = instanceId, Data = products });
});

// GET /products/{id} - Redis Caching Strategy (Read-Aside)
app.MapGet("/products/{id:int}", async (int id, ProductDbContext dbContext, IConnectionMultiplexer redis, HttpResponse response) =>
{
    var redisDb = redis.GetDatabase();
    string cacheKey = $"product:{id}";

    // 1. Redis Cache Kontrolü (Cache Hit?)
    var cachedData = await redisDb.StringGetAsync(cacheKey);
    if (!cachedData.IsNullOrEmpty)
    {
        response.Headers.Append("X-Cache-Status", "HIT (Redis)");
        var cachedProduct = JsonSerializer.Deserialize<Product>(cachedData.ToString());
        return Results.Ok(new { Source = "Redis Cache", HandledBy = instanceId, Data = cachedProduct });
    }

    // 2. SQL Server Database Kontrolü (Cache Miss)
    Product? product = null;
    try
    {
        product = await dbContext.Products.FirstOrDefaultAsync(p => p.Id == id);
    }
    catch
    {
        // Fallback default list
        var defaultProducts = new List<Product>
        {
            new Product { Id = 10, Name = "Gaming Laptop", Price = 35000, Stock = 5 },
            new Product { Id = 20, Name = "Kablosuz Kulaklık", Price = 2500, Stock = 15 },
            new Product { Id = 30, Name = "Mekanik Klavye", Price = 1800, Stock = 8 }
        };
        product = defaultProducts.FirstOrDefault(p => p.Id == id);
    }

    if (product == null)
        return Results.NotFound(new { Message = $"Ürün (ID: {id}) bulunamadı." });

    // 3. Redis Cache'e Yazma (TTL: 5 dakika)
    string serializedProduct = JsonSerializer.Serialize(product);
    await redisDb.StringSetAsync(cacheKey, serializedProduct, TimeSpan.FromMinutes(5));

    response.Headers.Append("X-Cache-Status", "MISS (SQL Server / DB)");
    return Results.Ok(new { Source = "SQL Server / Database", HandledBy = instanceId, Data = product });
});

// POST /products
app.MapPost("/products", async (Product newProduct, ProductDbContext dbContext) =>
{
    try
    {
        dbContext.Products.Add(newProduct);
        await dbContext.SaveChangesAsync();
    }
    catch { }

    return Results.Created($"/products/{newProduct.Id}", new { HandledBy = instanceId, Product = newProduct });
});

// PUT /products/{id}/stock - Update stock in SQL Server & Invalidate Cache
app.MapPut("/products/{id:int}/stock", async (int id, StockUpdateRequest request, ProductDbContext dbContext, IConnectionMultiplexer redis) =>
{
    try
    {
        var product = await dbContext.Products.FirstOrDefaultAsync(p => p.Id == id);
        if (product != null)
        {
            product.Stock = request.NewStock;
            await dbContext.SaveChangesAsync();
        }
    }
    catch { }

    // Cache Invalidation: Stok değiştiği için önbellekteki eski veriyi temizle
    var redisDb = redis.GetDatabase();
    await redisDb.KeyDeleteAsync($"product:{id}");

    return Results.Ok(new { Message = "Stok güncellendi ve Redis Cache temizlendi.", HandledBy = instanceId });
});

app.Run();

public class ProductDbContext : DbContext
{
    public ProductDbContext(DbContextOptions<ProductDbContext> options) : base(options) { }
    public DbSet<Product> Products => Set<Product>();
}

public class Product
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public int Stock { get; set; }
}

public class StockUpdateRequest
{
    public int NewStock { get; set; }
}
