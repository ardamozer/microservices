using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddHttpClient();

// EF Core DbContext Setup
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") 
                       ?? "Server=sqlserver-db,1433;Database=UserDb;User Id=sa;Password=YourStrong@Passw0rd!;TrustServerCertificate=True;Encrypt=False;";

try
{
    builder.Services.AddDbContext<UserDbContext>(options => options.UseSqlServer(connectionString));
}
catch
{
    builder.Services.AddDbContext<UserDbContext>(options => options.UseInMemoryDatabase("UserDbMemory"));
}

var app = builder.Build();

// Ensure DB Created & Seed Data
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<UserDbContext>();
    try
    {
        db.Database.EnsureCreated();
        if (!db.Users.Any())
        {
            db.Users.AddRange(
                new User { Name = "Ahmet Yılmaz", Email = "ahmet@example.com" },
                new User { Name = "Ayşe Demir", Email = "ayse@example.com" }
            );
            db.SaveChanges();
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[UserService Warning] SQL Server Seed Error: {ex.Message}");
    }
}

app.UseSwagger();
app.UseSwaggerUI();

// GET /health & /users/health
var healthHandler = (UserDbContext dbContext) =>
{
    bool dbHealthy = false;
    try { dbHealthy = dbContext.Database.CanConnect(); } catch { }

    return Results.Ok(new
    {
        Status = "Healthy",
        Database = dbHealthy ? "Connected (SQL Server)" : "InMemory"
    });
};

app.MapMethods("/health", new[] { "GET", "HEAD" }, healthHandler);
app.MapMethods("/users/health", new[] { "GET", "HEAD" }, healthHandler);

// GET /users
app.MapGet("/users", async (UserDbContext db) =>
{
    var users = await db.Users.ToListAsync();
    return Results.Ok(users);
});

// GET /users/{id}
app.MapGet("/users/{id:int}", async (int id, UserDbContext db) =>
{
    var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id);
    return user != null ? Results.Ok(user) : Results.NotFound(new { Message = "Kullanıcı bulunamadı." });
});

// POST /users
app.MapPost("/users", async (User newUser, UserDbContext db) =>
{
    db.Users.Add(newUser);
    await db.SaveChangesAsync();
    return Results.Created($"/users/{newUser.Id}", newUser);
});

// GET /users/{id}/orders - Communicates with Order Service
app.MapGet("/users/{id:int}/orders", async (int id, UserDbContext db, IHttpClientFactory httpClientFactory, IConfiguration config) =>
{
    var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id);
    if (user == null)
        return Results.NotFound(new { Message = "Kullanıcı bulunamadı." });

    var orderServiceUrl = config.GetValue<string>("Services:OrderService") ?? "http://order-service-1:5003";
    var client = httpClientFactory.CreateClient();

    var response = await client.GetAsync($"{orderServiceUrl}/orders/user/{id}");
    if (!response.IsSuccessStatusCode)
    {
        return Results.Ok(new { User = user, Orders = new List<object>(), Message = "Sipariş servisine erişilemedi veya sipariş yok." });
    }

    var orders = await response.Content.ReadFromJsonAsync<List<object>>();
    return Results.Ok(new { User = user, Orders = orders });
});

app.Run();

public class UserDbContext : DbContext
{
    public UserDbContext(DbContextOptions<UserDbContext> options) : base(options) { }
    public DbSet<User> Users => Set<User>();
}

public class User
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
}
