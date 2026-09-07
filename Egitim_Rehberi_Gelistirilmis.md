# 🎓 İleri Düzey Ders Notu: Redis CLI, Token İnceleme & Multi-Instance Load Balancing

Tebrikler! İlk seviyedeki mikroservis ve Redis konularını başarıyla tamamladın. 
Bu 2. seviye rehberde iki kritik konuyu öğreneceksin:
1. **Redis CLI Kullanımı:** Redis konteynerinin içine girip token'ları, önbellekteki verileri ve kilitleri canlı izlemek.
2. **Çoklu Sunucu (Multi-Instance Scaling) & Yük Dengeleme:** Aynı servisten 2'şer tane çalıştırarak sistemi **YARP Load Balancing** ve **Redis Distributed Lock** ile nasıl daha güçlü ve **stabil** hale getirdiğimizi görmek.

---

## 🔍 1. Bölüm: Redis CLI ile Token ve Anahtar İzleme

Redis bir "Kara Kutu" değildir! İçindeki tüm verileri, önbelleğe alınan ürün token'larını ve canlı trafiği terminal üzerinden doğrudan görebilirsin.

### 🛠️ Step 1: Redis Konteynerine Bağlanma
Terminalde (PowerShell) şu komutla çalışmakta olan Redis konteynerinin içine gir:

```powershell
docker exec -it redis-cache redis-cli
```
*Artık Redis'in komut satırındasın (`127.0.0.1:6379>`)!*

---

### 🔑 Step 2: Önemli Redis CLI Komutları

#### 1. Tüm Anahtarları (Keys / Tokens) Listeleme:
```text
KEYS *
```
*Örnek Çıktı:*
1) `"product:10"` (Cache token'ı)
2) `"ratelimit:172.23.0.1"` (Rate Limit sayacı token'ı)
3) `"lock:product:10"` (Anlık kilit anahtarı)

#### 2. Önbellekteki Veriyi Okuma (GET):
```text
GET product:10
```
*Çıktı:* `{"Id":10,"Name":"Gaming Laptop","Price":35000,"Stock":5}`

#### 3. Anahtarın Kalan Ömrünü (TTL - Time-To-Live) Sorgulama:
```text
TTL product:10
```
*Çıktı:* `284` (Veri 284 saniye sonra otomatik silinecek demektir).

#### 4. Rate Limit İsteğini Görme:
```text
GET ratelimit:172.23.0.1
```
*Çıktı:* `"4"` (O IP adresinin 1 dakika içinde attığı istek sayısı).

#### 5. CANLI CANLI REDİS TRAFİĞİNİ İZLEME (En Eğlenceli Kısım 🔥):
```text
MONITOR
```
*(Bu komutu yazdıktan sonra tarayıcıdan veya cURL ile istek attığında, Redis'e gelen tüm `GET`, `SET`, `INCR`, `PUBLISH` komutları ekranda canlı akar! Çıkmak için `Ctrl + C` yapabilirsin).*

#### 6. Tüm Redis Önbelleğini Temizleme (Reset):
```text
FLUSHALL
```

Redis CLI'dan çıkmak için `exit` yazabilirsin.

---

## ⚖️ 2. Bölüm: Çoklu Servis (Multi-Instance) ve YARP Load Balancing

Gerçek dünyada Trendyol, Amazon gibi sistemlerde tek bir `ProductService` çalıştırmak intihardır. Servis çökerse tüm site durur!

Bu yüzden **`docker-compose.yml`** dosyamızı güncelledik ve:
- **2 adet ProductService** (`ProductService-Instance-1` ve `ProductService-Instance-2`)
- **2 adet OrderService** (`OrderService-Instance-1` ve `OrderService-Instance-2`)

çalışacak şekilde mimarimizi **Ölçekledik (Scaled)**!

```text
                                Client / cURL
                                      │
                                      ↓
                            ┌──────────────────┐
                            │   API Gateway    │
                            │ (YARP Proxy &    │
                            │  Rate Limiting)  │
                            └────────┬─────────┘
                                     │
                    (RoundRobin Yük Dengeleme)
                                     │
           ┌─────────────────────────┴─────────────────────────┐
           ↓                                                   ↓
 ┌──────────────────┐                                ┌──────────────────┐
 │ ProductService-1 │                                │ ProductService-2 │
 └────────┬─────────┘                                └────────┬─────────┘
          │                                                   │
          └─────────────────────────┬─────────────────────────┘
                                    ↓
                              ┌──────────┐
                              │  REDIS   │
                              └──────────┘
```

---

### 🌟 API Gateway YARP RoundRobin Mantığı:
API Gateway gelen istekleri sırayla paylaşır:
- **1. İstek** -> `ProductService-Instance-1`'e gider.
- **2. İstek** -> `ProductService-Instance-2`'ye gider.
- **3. İstek** -> `ProductService-Instance-1`'e gider.

Buna **Round-Robin Yük Dengeleme (Load Balancing)** denir.

---

## 🔒 3. Bölüm: 2 Tane Servis Varken Sistem Nasıl Stabil Kalıyor?

Aklına şu soru gelebilir: *"Hocam, 2 tane OrderService aynı anda çalışıyorsa stok karışmaz mı?"*

**Cevap: ASLA KARIŞMAZ! ÇÜNKÜ REDIS DISTRIBUTED LOCK VAR!** 🛡️

1. Müşteri A isteği `OrderService-Instance-1`'e gider.
2. Müşteri B isteği `OrderService-Instance-2`'ye gider.
3. İki sunucu da bağımsızdır ama **ortak tek bir Redis** kullanırlar.
4. `OrderService-Instance-1` önce davranıp Redis'te `lock:product:10` kilidini KAPAR.
5. `OrderService-Instance-2` kilidi kapalı görüp BEKLEMEDE kalır!
6. `Instance-1` stok düşürüp kilidi açınca, `Instance-2` içeri girer ve güncellenmiş yeni stoğu görür.

İşte mikroservis mimarisinde **Stabilizasyon ve Eşzamanlılık (Concurrency Control)** bu şekilde sağlanır!

---

## 🧪 4. Bölüm: Çoklu Servis & Load Balance Testleri

### Step 1: Eski Konteynerleri Temizleyip Yeniden Başlatma
Eski servisi kapatıp yeni 2'şerli mimariyi tek komutla temiz bir şekilde başlat:

```powershell
cd D:\ardaclaude\Microservices
docker-compose down --remove-orphans
docker-compose up --build -d
```

---

### Step 2: Yük Dengeleme (RoundRobin) Testi
Terminalde sırayla 2 kez istek at ve `X-Instance-ID` Header'ına bak:

**1. İstek:**
```powershell
curl.exe -i http://localhost:5000/api/products/10
```
*Yanıt Header:* `X-Instance-ID: ProductService-Instance-1`

**2. İstek:**
```powershell
curl.exe -i http://localhost:5000/api/products/10
```
*Yanıt Header:* `X-Instance-ID: ProductService-Instance-2`

> 🎉 İki farklı isteğin iki farklı sunucu tarafından karşılandığını gözlerinle gördün! YARP yükü harika bir şekilde dengeliyor!

---

### Step 3: Stok Azalmasını Görme ve Doğrulama
Sipariş verdikten sonra ürün stoğunun düştüğünü 3 farklı yöntemle doğrulayabilirsin:

**Yöntem 1: API Gateway Üzerinden Tek Ürün Sorgulama:**
```powershell
curl.exe http://localhost:5000/api/products/10
```
*Gelen Yanıt:* `{"id":10, "name":"Gaming Laptop", "price":35000, "stock":3}` (Stok 5'ten 3'e düşmüş olacaktır!).

**Yöntem 2: Tüm Ürün Listesini Görme:**
```powershell
curl.exe http://localhost:5000/api/products
```

**Yöntem 3: Güncel Stoğu Doğrudan Redis Önbelleğinden Okuma:**
```powershell
docker exec -it redis-cache redis-cli GET product:10
```
*Çıktı:* `{"Id":10,"Name":"Gaming Laptop","Price":35000,"Stock":3}`

---

### Step 4: Redis MONITOR ile Canlı Trafik İzleme Testi
1. Yeni bir PowerShell penceresi aç ve Redis CLI Monitor başlat:
   ```powershell
   docker exec -it redis-cache redis-cli MONITOR
   ```
2. Ana pencereden sipariş verme isteği at:
   ```powershell
   curl.exe -X POST http://localhost:5000/api/orders -H "Content-Type: application/json" -d "{\`"userId\`": 5, \`"productId\`": 10, \`"quantity\`": 1}"
   ```
3. Monitor ekranında `SET lock:product:10`, `PUBLISH order_created_channel`, `DEL product:10` komutlarının ışık hızında aktığını izle!

Tebrikler! Artık ileri düzey mikroservis ölçekleme, YARP yük dengeleme ve Redis CLI inceleme tekniklerine hakimsin! 🚀
