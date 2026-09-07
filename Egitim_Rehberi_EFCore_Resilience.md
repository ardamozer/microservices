# 🎓 3. Seviye Ders Notu: EF Core SQL Server Kalıcılığı, Polly Resilience & Health Checks

Tebrikler! Artık projen üretim ortamlarına (Production-Ready) tam anlamıyla hazır!

Bu 3. seviye rehberde üç dev mimari konuyu öğreneceksin:
1. **EF Core & SQL Server Kalıcılığı (Persistence):** Verilerin RAM yerine gerçek SQL Server veritabanında saklanması.
2. **Polly ile Dayanıklılık (Resilience Patterns):** Mikroservisler arası iletişimde **Retry (Otomatik Yeniden Deneme)** ve **Circuit Breaker (Sigorta/Devre Kesici)** mekanizması.
3. **Sağlık Kontrolleri (Health Checks):** Tüm sistemin ve bağımlılıkların (Redis/SQL) anlık durumunu izleme (`/health`).

---

## 🗄️ 1. Bölüm: EF Core & SQL Server Kalıcılığı (Persistence)

Eski aşamalarda verilerimiz RAM üzerindeydi (Konteyner yeniden başlayınca siliniyordu). 
Şimdi **Entity Framework Core (EF Core)** entegrasyonu ile verilerimiz Docker içindeki **Microsoft SQL Server 2022 (`sqlserver-db`)** veritabanına kaydediliyor.

```text
 ┌──────────────────┐               ┌──────────────────┐
 │ ProductService   ├──────────────►│ SQL Server 2022  │
 │ (EF Core)        │  (DB Kaydı)   │ (sqlserver-db)   │
 └──────────────────┘               └──────────────────┘
```

- Uygulama ilk açıldığında `EnsureCreated()` ile veritabanı tabloları otomatik oluşturulur.
- Konteynerleri kapatsan veya bilgisayarı yeniden başlatsan dahi verilerin **asla silinmez!**

---

## 🛡️ 2. Bölüm: Polly ile Dayanıklılık Desenleri (Resilience)

Mikroservis mimarilerinde en büyük tehlike **Dağıtık Çökmedir (Cascading Failure)**. Bir servis yavaşlarsa veya ağda 1 saniyelik paket kaybı olursa diğer tüm servisler ona takılıp çökebilir!

Bu tehlikeyi önlemek için **Polly** kütüphanesini kullandık:

### 1. Retry Policy (Otomatik Yeniden Deneme):
- `OrderService`, `ProductService`'e istek atarken geçici bir network hatası oluşursa pes etmez!
- 200 ms aralıklarla **3 kez otomatik olarak tekrar dener**. İstek başarılı olursa kullanıcı hiç hata almaz!

### 2. Circuit Breaker (Devre Kesici / Evdeki Sigorta Mantığı):
- Eğer `ProductService` tamamen çöktüyse ve üst üste 2 kez hata verirse, Polly **Sigortayı Atlatır (Circuit OPEN)**.
- 15 saniye boyunca `ProductService`'e hiç istek göndermeyip doğrudan hızlı hata döner. Böylece sunucu kaynakları boşuna tüketilmez ve kilitlenmeler önlenir!

```text
 Normal Durum (CLOSED): İstekler Akıyor ──► [İletişim Başarılı]
 Hata Durumu   (OPEN)  : Sigorta Attı! ────► [Hızlı Hata Dönülüyor / Sistem Korunuyor]
```

---

## 🩺 3. Bölüm: Sağlık Kontrolleri (Health Checks)

Her mikroservise kendi durumunu ve bağımlılıklarını raporlayan `/health` endpoint'leri eklendi.

API Gateway veya sistem yöneticileri tek bir istekle servislerin ayakta olup olmadığını görebilir.

---

## 🧪 4. Bölüm: Canlı Test ve Doğrulama Adımları

### Step 1: Temiz Yeniden Başlatma
Terminalde yeni 3. Seviye mimariyi çalıştır:

```powershell
cd D:\ardaclaude\Microservices
docker-compose down --remove-orphans
docker-compose up --build -d
```

---

### Step 2: Sağlık Kontrolü (Health Check) Testi
ProductService'in ve SQL Server / Redis bağlantılarının durumunu sorgula:

```powershell
curl.exe http://localhost:5000/api/products/health
```

*Örnek Çıktı:*
```json
{
  "status": "Healthy",
  "instance": "ProductService-Instance-1",
  "database": "Connected (SQL Server)",
  "redis": "Connected"
}
```

---

### Step 3: SQL Server Veri Kalıcılığı (Persistence) Testi

1. **Bir Sipariş Oluştur:**
   ```powershell
   curl.exe -X POST http://localhost:5000/api/orders -H "Content-Type: application/json" -d "{\`"userId\`": 5, \`"productId\`": 10, \`"quantity\`": 2}"
   ```
   *(Stok 5 -> 3'e düşecek ve sipariş SQL Server'a yazılacak).*

2. **Konteynerleri Kapat ve Tekrar Aç (Reset Attır):**
   ```powershell
   docker-compose down
   docker-compose up -d
   ```

3. **Ürün Stoğunu Tekrar Kontrol Et:**
   ```powershell
   curl.exe http://localhost:5000/api/products/10
   ```

*Sonuç:* Stok sıfırlanmadı! **Stok halen 3 olarak SQL Server veritabanında duruyor!** 🎉

---

Tebrikler! Artık EF Core SQL Server Kalıcılığı, Polly Resilience ve Sağlık Kontrolleri içeren üretim seviyesinde gerçek bir Mikroservis Sistemine sahipsin! 🚀
