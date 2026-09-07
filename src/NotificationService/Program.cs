using System.Text.Json;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Redis Setup
var redisConnectionString = builder.Configuration.GetValue<string>("Redis:ConnectionString") ?? "localhost:6379";
builder.Services.AddSingleton<IConnectionMultiplexer>(sp => ConnectionMultiplexer.Connect(redisConnectionString));

// Background Worker to listen Redis Pub/Sub
builder.Services.AddHostedService<RedisNotificationSubscriber>();

// In-Memory Notification Logs Store
builder.Services.AddSingleton<NotificationStore>();

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();

app.MapGet("/notifications", (NotificationStore store) => Results.Ok(store.GetNotifications()));

app.Run();

// In-Memory Store
public class NotificationStore
{
    private readonly List<NotificationLog> _logs = new();

    public void Add(NotificationLog log)
    {
        _logs.Add(log);
        Console.WriteLine($"[NOTIFICATION SERVICE - REDIS PUB/SUB] 🔔 Sipariş Bildirimi: {log.Message}");
    }

    public List<NotificationLog> GetNotifications() => _logs;
}

public class NotificationLog
{
    public DateTime Timestamp { get; set; }
    public string Message { get; set; } = string.Empty;
    public object? Details { get; set; }
}

// Background Worker for Redis Pub/Sub Subscriber
public class RedisNotificationSubscriber : BackgroundService
{
    private readonly IConnectionMultiplexer _redis;
    private readonly NotificationStore _store;

    public RedisNotificationSubscriber(IConnectionMultiplexer redis, NotificationStore store)
    {
        _redis = redis;
        _store = store;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var sub = _redis.GetSubscriber();

        await sub.SubscribeAsync(RedisChannel.Literal("order_created_channel"), (channel, message) =>
        {
            try
            {
                var payload = JsonSerializer.Deserialize<JsonElement>(message.ToString());
                int orderId = payload.GetProperty("OrderId").GetInt32();
                int userId = payload.GetProperty("UserId").GetInt32();
                string productName = payload.GetProperty("ProductName").GetString() ?? "";

                var log = new NotificationLog
                {
                    Timestamp = DateTime.UtcNow,
                    Message = $"Sayın Kullanıcı (ID: {userId}), #{orderId} numaralı '{productName}' siparişiniz başarıyla alındı ve hazırlanıyor!",
                    Details = payload
                };

                _store.Add(log);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[NotificationService Error] Mesaj işlenemedi: {ex.Message}");
            }
        });
    }
}
