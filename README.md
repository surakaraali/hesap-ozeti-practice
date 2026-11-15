# HesapOzeti.Practice

Online hesap özeti benzeri küçük bir pratik projesi. İşletmenin banka hesap hareketlerini bir bankadan çeker,
SQL Server'a yazar, ERP'ye (Logo Tiger benzeri) fiş olarak aktarır ve hesap hareketlerini listeler.
Banka da ERP de sahte. Gerçek banka adı ya da gerçek veri kullanılmıyor.

Bu proje bir haftalık bakım işinin pratiği olarak yazıldı. Ticket'lar [TASKS.md](TASKS.md) içinde, her biri ayrı bir commit.

## Çalıştırma

Gerekenler: .NET 10 SDK ve Docker. Apple Silicon'da SQL Server imajı Rosetta ile çalışıyor.

```bash
cp .env.example .env                               # MSSQL_SA_PASSWORD'ü değiştir (8+ karakter, büyük/küçük harf, rakam, sembol)
docker-compose up -d --wait                        # SQL Server, localhost:14334
dotnet tool restore                                # dotnet-ef
dotnet run --project src/HesapOzeti.Api -- seed    # migration + yaklaşık 500 bin hareket, 15 sn civarı
dotnet run --project src/HesapOzeti.Api            # http://localhost:5196, job'lar 10 sn sonra başlar
dotnet test                                        # SQL Server açık olmalı, ayrı bir test DB'si kullanılır
```

Seed'i baştan almak için: `dotnet ef database drop -f -p src/HesapOzeti.Api`, sonra seed komutunu tekrar çalıştır.
İş bitince: `docker-compose stop`.

## Nasıl çalışıyor

```
 Demo Bank (/fake-bank)          API                                   SQL Server
 ┌───────────────────┐   v2   ┌──────────────────────────┐        ┌──────────────────┐
 │ oauth/token       │◄───────│ BankApiClient (adapter)  │        │ BankAccounts     │
 │ v1/transactions   │        │   token, retry, v1/v2    │        │ BankTransactions │
 │ v2/transactions   │        ├──────────────────────────┤ yazar  │ ErpVouchers      │
 └───────────────────┘        │ TransactionSyncJob  60 s │───────►│  (Tiger fişleri) │
                              │ ErpExportJob        60 s │───────►│                  │
                              │   └─ FakeTigerClient     │        └──────────────────┘
                              │ GET /accounts/{id}/...   │◄─ liste
                              └──────────────────────────┘
```

1. **Senkron** (`Sync/`): Quartz job'ı her dakika aktif hesaplar için **dünün ve bugünün** hareketlerini bankadan çeker
   ve yeni olanları `Pending` olarak kaydeder. Dün de okunduğu için gece yarısı civarında başarısız olan bir çalışma veri kaybettirmez.
2. **ERP aktarımı** (`Erp/`): Quartz job'ı `Pending` hareketleri Tiger'a fiş olarak yazar ve `Exported` yapar.
3. **Liste** (`Statements/`): hesap hareketleri en yeniden geriye doğru, cursor ile sayfalanır.

Sahte banka ve sahte Tiger aynı API içinde çalışıyor. Bu yüzden senkron job'ı API'nin kendisini HTTP ile çağırıyor.

## Proje yapısı

```
src/HesapOzeti.Api/
  Program.cs                   DI, Quartz, endpoint'ler, "seed" komutu
  Data/                        entity'ler, AppDbContext, migration'lar, Seeder (SqlBulkCopy), LocalDb (.env okur)
  FakeBank/                    Demo Bank: token, v1 ve v2 hareket uçları, örnek veri
  Sync/                        BankApiClient (adapter + BankTokenStore), TransactionSyncService + job
  Erp/                         ErpExportService + job, FakeTigerClient
  Statements/                  TransactionList: keyset sayfalı hareket listesi
tests/HesapOzeti.Tests/        xUnit; banka çağrıları stub, veritabanı testleri gerçek SQL Server'da
perf/                          measure.sh (endpoint süresi), list-plans.sql (execution plan + okuma sayısı)
scripts/duplicates-report.sql  temizlenen mükerrer kayıtların listesi (muhasebe için)
docs/performance.md            HOZ-104 ölçümleri
```

## Uç noktalar

| Metot | Yol | Açıklama |
|---|---|---|
| GET | `/accounts` | Hesaplar |
| GET | `/accounts/{id}/transactions?pageSize=50&cursor=&from=&to=` | Hareketler, en yeni önce. `pageSize` 1–200 arası. Cevaptaki `nextCursor` bir sonraki sayfayı getirir, son sayfada `null` döner. `total` sadece ilk sayfada dolu. |
| POST | `/sync/run` | Senkronu hemen çalıştırır, `{ added, failedAccounts }` döner |
| POST | `/erp/export` | ERP aktarımını hemen çalıştırır |
| POST | `/fake-bank/oauth/token` | `{ clientId, clientSecret }` ile token alır |
| GET | `/fake-bank/v1/transactions?iban=&date=dd.MM.yyyy` | Eski sürüm. `Sunset` header'ı döner. |
| GET | `/fake-bank/v2/transactions?iban=&date=yyyy-MM-dd` | Güncel sürüm |

```bash
curl "localhost:5196/accounts/1/transactions?pageSize=2"
curl "localhost:5196/accounts/1/transactions?pageSize=2&cursor=<nextCursor>"
```

## Ayarlar (`appsettings.json`)

| Anahtar | Varsayılan | Ne işe yarar |
|---|---|---|
| `Bank:BaseUrl` | `http://localhost:5196/` | Banka API adresi |
| `Bank:ApiVersion` | `v2` | `v1` yazılırsa eski sürüme döner, deploy gerekmez |
| `Bank:MaxAttempts` / `Bank:RetryBaseDelayMs` | `3` / `500` | Geçici hatalarda (408, 429, 5xx, ağ hatası) deneme sayısı ve ilk bekleme. Her denemede bekleme iki katına çıkar. |
| `Jobs:Enabled` | `true` | Quartz job'larını açar/kapatır |
| `Jobs:SyncIntervalSeconds` / `Jobs:ExportIntervalSeconds` | `60` / `60` | Job aralıkları |
| `FakeBank:TokenLifetimeSeconds` | `120` | Sahte bankanın verdiği token ömrü |
| `FakeBank:FailureRate` | `0.05` | Sahte bankanın rastgele 503 dönme oranı |
| `Erp:TimeoutRate` | `0.05` | Sahte Tiger'ın fişi yazıp geç cevap verme oranı |

Her ayar ortam değişkeniyle ezilebilir, örneğin `Jobs__Enabled=false`. Bağlantı cümlesi `.env` içindeki SA şifresinden
kuruluyor (`Data/LocalDb.cs`). İstersen `ConnectionStrings__Default` ile tamamen değiştirebilirsin.

## Veri kuralları

- **Para:** `decimal(18,2)`. Tutar ve tarih okurken her zaman `CultureInfo.InvariantCulture` kullanılıyor, çünkü makine Türkçe locale'de olabilir.
- **Tarih:** `TransactionDate` bankanın verdiği Türkiye saatidir (UTC+3). `CreatedAt` ise UTC.
- **Mükerrer banka hareketi:** `(BankAccountId, BankReferenceNo)` unique. Eski mükerrerler silinmedi, `ErpSyncStatus = Duplicate (3)`
  olarak işaretlendi ve index'in dışında tutuluyor. Listede görünmezler, ERP'ye aktarılmazlar.
  Banka aynı referansı tek bir cevapta iki kez gönderirse bir kez kaydedilir ve uyarı loglanır. Kayıt hesap bazında yapılır,
  bir hesaptaki hata diğer hesapların hareketlerini durdurmaz.
- **ERP idempotency:** `ErpVouchers.BankTransactionId` iptal edilmemiş fişler için unique, idempotency anahtarı bu.
  Tiger zaman aşımı verip fişi yine de yazmışsa, bir sonraki deneme yeni fiş yazmaz, mevcut fişi döner.
  İptal edilen fişlerde `CancelledAt` dolu.
- **Silme yok:** Finansal kayıtlar düzeltilirken silinmez, durumları işaretlenir. Listeyi `scripts/duplicates-report.sql` çıkarır.

## Testler

`dotnet test` 23 test çalıştırıyor:
- Banka çağrıları bir `HttpMessageHandler` stub'ıyla test ediliyor: v1/v2 eşlemesi, token yenileme, 401, retry.
- Veritabanı testleri `HesapOzetiPractice_Tests` veritabanını her çalıştırmada sıfırdan kuruyor. Paralel çalışabilmeleri için her test kendi hesabını açıyor.

## Sorun giderme

- **`MSSQL_SA_PASSWORD is not set`:** `.env` dosyası eksik. `cp .env.example .env` ile oluştur.
- **SQL Server açılmıyor:** `docker-compose logs db` çıktısına bak. Şifre karmaşıklık kuralına uymuyorsa container kapanır.
- **`sqlcmd` ile filtreli index'li tablolara yazılamıyor ya da index kullanılmıyor:** `-I` (QUOTED_IDENTIFIER ON) ekle.
- **zsh'de noktalı ortam değişkeni:** `Logging__LogLevel__Microsoft.EntityFrameworkCore.Database.Command=Information` doğrudan yazılamaz, başına `env` koy.
- **EF'in çalıştırdığı SQL'i görmek:** yukarıdaki log ayarını `Information` yap.
