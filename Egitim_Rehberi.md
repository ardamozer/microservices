# 🎓 Mikroservis ve Redis Mimarisi Ders Notu & Eğitim Rehberi

Merhaba geleceğin kıdemli yazılımcısı! 👋 
Bu rehber, senin mikroservis mimarisini, **Redis**'in bir projede hangi kritik problemlere çözüm sunduğunu ve Docker ile sistemin nasıl ayağa kaldırıldığını en basit ve akılda kalıcı şekilde öğrenmen için hazırlandı.

---

## 🏛️ 1. Bölüm: Mikroservis Nedir? (Restoran Benzetmesi)

Eski yazılım mimarilerinde **Monolith (Tek Parça)** yapı vardı. Tüm işler tek bir büyük projenin içindeydi. 
Bir restoranda **aynı kişinin hem sipariş aldığını, hem yemek pişirdiğini, hem bulaşık yıkadığını hem de kasada durduğunu** düşün. Restoran kalabalıklaşırsa o kişi çöker!

**Mikroservis Mimarisi** ise uzmanlaşmış bir ekiptir:
- **API Gateway (Karşılama/Resepsiyon):** Müşteriyi karşılar, istekleri doğru masaya/servise yönlendirir.
- **User Service (Müşteri İlişkileri):** Müşteri bilgilerini tutar.
- **Product Service (Depo/Mutfak Kataloğu):** Hangi yemekten/üründen kaç adet var bilir.
- **Order Service (Kasa & Sipariş):** Siparişi alır, ödemeyi doğrular.
- **Notification Service (SMS/Garson):** Sipariş hazır olunca müşteriye mesaj atar.

---

## ⚡ 2. Bölüm: Redis Neden Bu Kadar Önemli? (4 Altın Senaryo)

Redis, verileri RAM (bellek) üzerinde tutan ultra hızlı bir anahtar-değer (Key-Value) veritabanıdır. Projemizde Redis'i 4 farklı süper güç olarak kullandık:

```text
               API Gateway (Rate Limiting)
                           │
       ┌───────────────────┼───────────────────┐
       ↓                   ↓                   ↓
  User Service       Product Service     Order Service
                           │                   │
                   (1. Redis Cache)    (2. Distributed Lock)
                           │                   │
                           └─────────┬─────────┘
                                     ↓
                                   REDIS
                                     │
                             (3. Redis Pub/Sub)
                                     ↓
                            Notification Service
```

---

### 🔍 1. Önbellekleme (Caching - Read-Aside Pattern)
- **Problem:** Müşteriler saniyede 10.000 kez "Gaming Laptop" sayfasına giriyor. Her seferinde SQL Server'a sormak veritabanını kilitler!
- **Çözüm:** 
  1. İstek geldiğinde önce Redis'e bakılır (`product:10`).
  2. Eğer varsa **(Cache HIT)**, doğrudan RAM'den yanıt döner (0.5 milisaniye!).
  3. Eğer yoksa **(Cache MISS)**, SQL Server'a gidilir, veri Redis'e kaydedilir ve kullanıcıya dönülür.
- **Kod Mantığımız (`ProductService`):**
```csharp
var cachedData = await db.StringGetAsync("product:10");
if (!cachedData.IsNullOrEmpty) {
    return CacheHit(cachedData); // SQL Server'a YÜK BİNMEZ!
}
```

---

### 🔒 2. Dağıtık Kilit (Distributed Lock - Stock Race Condition)
- **Problem:** Stokta **1 adet** Laptop var. İki kullanıcı (User A ve User B) **aynı milisaniyede** "Satın Al" butonuna basıyor.
  - İki servis de SQL'e bakar: `Stok = 1 > 0` görür.
  - İkisi de sipariş oluşturur. Stok eksiye düşer (`Stok = -1`)! 😱 (Buna *Race Condition* denir).
- **Çözüm:** **Redis Distributed Lock (`LockTakeAsync`)**
  1. User A gelince Redis'te `lock:product:10` anahtarını kilitler.
  2. User B aynı an gelirse beklemek zorundadır!
  3. User A stoğu 1'den 0'a düşürür ve kilidi bırakır (`LockReleaseAsync`).
  4. User B içeri girer, stoğun 0 olduğunu görür ve "Yetersiz Stok" hatası alır.

---

### 📣 3. Redis Pub/Sub (Event-Driven Mesajlaşma)
- **Problem:** Sipariş oluştuktan sonra SMS/Email göndermek Order Service'in işi değildir. Order Service'in hızlıca yanıt dönmesi gerekir.
- **Çözüm:** 
  - Order Service sipariş bitince Redis'in `order_created_channel` kanalına bir mesaj fırlatır (**Publish**).
  - Notification Service arka planda bu kanalı dinler (**Subscribe**) ve siparişi görünce kullanıcıya mesaj gönderir. Servisler birbirine bağımlı (tightly-coupled) olmaz!

---

### 🛑 4. Rate Limiting (Kötüye Kullanımı Önleme)
- **Problem:** Kötü niyetli biri bot yazıp saniyede 1.000 istek atarak sunucumuzu çökertmeye çalışıyor (DDoS).
- **Çözüm:** **API Gateway** gelen her IP adresi için Redis'te sayaç tutar (`ratelimit:192.168.1.10`). 1 dakikada 100 isteği geçerse HTTP **429 Too Many Requests** hatası verir.

---

## 🚀 3. Bölüm: Projeyi Çalıştırma ve Test Etme

Tüm proje **`D:\ardaclaude\Microservices`** dizinindedir.

### Yöntem A: Docker Compose ile Tek Komutla Çalıştırma (Tavsiye Edilen)
> 💡 **Önemli Not:** Komutu çalıştırmadan önce **Docker Desktop** uygulamasının bilgisayarınızda açık ve çalışır vaziyette (*Engine running*) olduğundan emin olun.

Terminali açıp şu komutu çalıştır:
```powershell
cd D:\ardaclaude\Microservices
docker-compose up --build -d
```
Tüm servisler, Redis ve SQL Server konteyner olarak ayağa kalkacaktır!

### Yöntem B: Lokal dotnet run ile Çalıştırma
Eğer Docker kullanmadan doğrudan test etmek istersen (Bilgisayarında Redis kuruluysa):
1. Terminal 1: `cd D:\ardaclaude\Microservices\src\ProductService ; dotnet run` (Port: 5002)
2. Terminal 2: `cd D:\ardaclaude\Microservices\src\OrderService ; dotnet run` (Port: 5003)
3. Terminal 3: `cd D:\ardaclaude\Microservices\src\NotificationService ; dotnet run` (Port: 5004)
4. Terminal 4: `cd D:\ardaclaude\Microservices\src\UserService ; dotnet run` (Port: 5001)
5. Terminal 5: `cd D:\ardaclaude\Microservices\src\ApiGateway ; dotnet run` (Port: 5000)

---

## 🧪 4. Bölüm: Adım Adım Deney ve Testler

Şimdi bir bilim insanı gibi sistemimizi test edelim! 🥼

### Test 1: Redis Cache (HIT vs MISS) Deneyi
Terminalden (PowerShell veya CMD) API Gateway üzerinden Ürün 10'u iste (PowerShell kullanıyorsan `curl.exe` yazmalısın):

**1. İstek (Cache MISS - Veritabanından okunur):**
```powershell
curl.exe -i http://localhost:5000/api/products/10
```
*Gelen Header:* `X-Cache-Status: MISS (SQL Server / DB)`

**2. İstek (Cache HIT - Doğrudan Redis'ten okunur!):**
```powershell
curl.exe -i http://localhost:5000/api/products/10
```
*Gelen Header:* `X-Cache-Status: HIT (Redis)` -> SQL Server'a hiç gidilmedi!

---

### Test 2: Distributed Lock ile Güvenli Sipariş Oluşturma
Stokta 5 adet olan Gaming Laptop (ID: 10) için sipariş verelim:

```powershell
curl.exe -X POST http://localhost:5000/api/orders -H "Content-Type: application/json" -d "{\`"userId\`": 5, \`"productId\`": 10, \`"quantity\`": 2}"
```

*Sonuç:*
1. Redis Lock alındı.
2. Stok 5 -> 3'e düşürüldü.
3. Lock serbest bırakıldı.
4. Redis Pub/Sub kanalına `OrderCreated` mesajı yayınlandı!
5. `ProductService` cache'i otomatik temizlendi (Cache Invalidation).

---

### Test 3: Pub/Sub Bildirim Kontrolü
Notification Service'in Redis Pub/Sub mesajını yakalayıp yakalamadığını kontrol et:

```powershell
curl.exe http://localhost:5000/api/notifications
```
*Çıktı:* 
`"Sayın Kullanıcı (ID: 5), #1 numaralı 'Gaming Laptop' siparişiniz başarıyla alındı ve hazırlanıyor!"`

---

### Test 4: Rate Limiting (DDoS Engelleme) Deneyi
API Gateway'e üst üste hızlıca istek at. Sayaç 100'ü geçtiğinde ekranına şu mesaj düşecektir:

```json
{
  "error": "429 Too Many Requests",
  "message": "Rate limit aşıldı! Dakikada maksimum 100 istek atabilirsiniz."
}
```

---

## 📝 5. Bölüm: Öğrenci Ödevi ve Alıştırmalar

Kendini geliştirmek için şu eklemeleri yapmayı deneyebilirsin:
1. `ProductService` projesinde yeni bir ürün eklediğinde (`POST /products`), bu ürünün otomatik olarak Redis Cache'e eklenmesini sağla.
2. `OrderService` içindeki Lock süresini (Expiry) incele. Eğer işlem 10 saniyeden uzun sürerse ne olacağını düşün.
3. `NotificationService` içine SMS atıyormuş gibi bir Log yazdır.

Tebrikler! Artık mikroservis mimarisini ve Redis'in gerçek hayattaki 4 kritik kullanım senaryosunu öğrenmiş bulunuyorsun! 🚀
