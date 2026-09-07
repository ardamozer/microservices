using StackExchange.Redis;
using Yarp.ReverseProxy.LoadBalancing;
using Yarp.ReverseProxy.Transforms;

var builder = WebApplication.CreateBuilder(args);

// Redis setup for Rate Limiting
var redisConnectionString = builder.Configuration.GetValue<string>("Redis:ConnectionString") ?? "localhost:6379";
builder.Services.AddSingleton<IConnectionMultiplexer>(sp => ConnectionMultiplexer.Connect(redisConnectionString));

// YARP Config with RoundRobin Load Balancing
builder.Services.AddReverseProxy()
    .LoadFromMemory(GetRoutes(), GetClusters());

// CORS setup for Web UI
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader()
              .WithExposedHeaders("X-RateLimit-Limit", "X-RateLimit-Remaining", "X-Instance-ID", "X-Cache-Status");
    });
});

var app = builder.Build();

app.UseCors("AllowAll");

// REDIS RATE LIMITING MIDDLEWARE
app.Use(async (context, next) =>
{
    var redis = context.RequestServices.GetRequiredService<IConnectionMultiplexer>();
    var db = redis.GetDatabase();

    // Client IP belirleme
    string clientIp = context.Connection.RemoteIpAddress?.ToString() ?? "127.0.0.1";
    string rateLimitKey = $"ratelimit:{clientIp}";

    // Dakikada Maksimum Request Sınırı (Örn: 100 request)
    int maxRequestsPerMinute = 100;

    // Redis INCR komutu ile sayacı 1 artırır
    long requestCount = await db.StringIncrementAsync(rateLimitKey);

    // Eğer ilk request ise 60 saniyelik TTL (Time-To-Live) ayarla
    if (requestCount == 1)
    {
        await db.KeyExpireAsync(rateLimitKey, TimeSpan.FromMinutes(1));
    }

    // Rate Limit aşıldıysa 429 Too Many Requests dön
    if (requestCount > maxRequestsPerMinute)
    {
        context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsJsonAsync(new
        {
            Error = "429 Too Many Requests",
            Message = $"Rate limit aşıldı! Dakikada maksimum {maxRequestsPerMinute} istek atabilirsiniz.",
            CurrentRequests = requestCount,
            ClientIp = clientIp
        });
        return;
    }

    // İstek sayısını HTTP Header'da göster
    context.Response.Headers.Append("X-RateLimit-Limit", maxRequestsPerMinute.ToString());
    context.Response.Headers.Append("X-RateLimit-Remaining", (maxRequestsPerMinute - requestCount).ToString());

    await next();
});

app.MapReverseProxy();

app.Run();

// YARP Routing Configurations
static IReadOnlyList<Yarp.ReverseProxy.Configuration.RouteConfig> GetRoutes()
{
    return new[]
    {
        new Yarp.ReverseProxy.Configuration.RouteConfig
        {
            RouteId = "user_route",
            ClusterId = "user_cluster",
            Match = new Yarp.ReverseProxy.Configuration.RouteMatch { Path = "/api/users/{**catch-all}" }
        }.WithTransformPathRemovePrefix("/api"),

        new Yarp.ReverseProxy.Configuration.RouteConfig
        {
            RouteId = "product_route",
            ClusterId = "product_cluster",
            Match = new Yarp.ReverseProxy.Configuration.RouteMatch { Path = "/api/products/{**catch-all}" }
        }.WithTransformPathRemovePrefix("/api"),

        new Yarp.ReverseProxy.Configuration.RouteConfig
        {
            RouteId = "order_route",
            ClusterId = "order_cluster",
            Match = new Yarp.ReverseProxy.Configuration.RouteMatch { Path = "/api/orders/{**catch-all}" }
        }.WithTransformPathRemovePrefix("/api"),

        new Yarp.ReverseProxy.Configuration.RouteConfig
        {
            RouteId = "notification_route",
            ClusterId = "notification_cluster",
            Match = new Yarp.ReverseProxy.Configuration.RouteMatch { Path = "/api/notifications/{**catch-all}" }
        }.WithTransformPathRemovePrefix("/api")
    };
}

static IReadOnlyList<Yarp.ReverseProxy.Configuration.ClusterConfig> GetClusters()
{
    var userUrl = Environment.GetEnvironmentVariable("USER_SERVICE_URL") ?? "http://localhost:5001";
    var productUrl1 = Environment.GetEnvironmentVariable("PRODUCT_SERVICE_URL_1") ?? "http://product-service-1:5002";
    var productUrl2 = Environment.GetEnvironmentVariable("PRODUCT_SERVICE_URL_2") ?? "http://product-service-2:5002";
    var orderUrl1 = Environment.GetEnvironmentVariable("ORDER_SERVICE_URL_1") ?? "http://order-service-1:5003";
    var orderUrl2 = Environment.GetEnvironmentVariable("ORDER_SERVICE_URL_2") ?? "http://order-service-2:5003";
    var notificationUrl = Environment.GetEnvironmentVariable("NOTIFICATION_SERVICE_URL") ?? "http://localhost:5004";

    return new[]
    {
        new Yarp.ReverseProxy.Configuration.ClusterConfig
        {
            ClusterId = "user_cluster",
            Destinations = new Dictionary<string, Yarp.ReverseProxy.Configuration.DestinationConfig>
            {
                { "user_dest", new Yarp.ReverseProxy.Configuration.DestinationConfig { Address = userUrl } }
            }
        },
        new Yarp.ReverseProxy.Configuration.ClusterConfig
        {
            ClusterId = "product_cluster",
            LoadBalancingPolicy = LoadBalancingPolicies.RoundRobin, // YARP RoundRobin Load Balancing
            Destinations = new Dictionary<string, Yarp.ReverseProxy.Configuration.DestinationConfig>
            {
                { "product_dest_1", new Yarp.ReverseProxy.Configuration.DestinationConfig { Address = productUrl1 } },
                { "product_dest_2", new Yarp.ReverseProxy.Configuration.DestinationConfig { Address = productUrl2 } }
            }
        },
        new Yarp.ReverseProxy.Configuration.ClusterConfig
        {
            ClusterId = "order_cluster",
            LoadBalancingPolicy = LoadBalancingPolicies.RoundRobin, // YARP RoundRobin Load Balancing
            Destinations = new Dictionary<string, Yarp.ReverseProxy.Configuration.DestinationConfig>
            {
                { "order_dest_1", new Yarp.ReverseProxy.Configuration.DestinationConfig { Address = orderUrl1 } },
                { "order_dest_2", new Yarp.ReverseProxy.Configuration.DestinationConfig { Address = orderUrl2 } }
            }
        },
        new Yarp.ReverseProxy.Configuration.ClusterConfig
        {
            ClusterId = "notification_cluster",
            Destinations = new Dictionary<string, Yarp.ReverseProxy.Configuration.DestinationConfig>
            {
                { "notification_dest", new Yarp.ReverseProxy.Configuration.DestinationConfig { Address = notificationUrl } }
            }
        }
    };
}
