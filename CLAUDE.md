# CLAUDE.md

Online hesap özeti pratik projesi. Akışı şöyle: banka → `BankTransactions` → ERP fişi (`ErpVouchers`) ve hesap hareketleri listesi.
Ayrıntılar [README.md](README.md) içinde. Bu dosya kodu değiştiren herkes için, AI asistan dahil.

## Komutlar

```bash
docker-compose up -d --wait                          # SQL Server :14334, testlerden önce açık olmalı
dotnet build
dotnet test                                          # tümü; tek test için: --filter "FullyQualifiedName~TransactionList"
dotnet run --project src/HesapOzeti.Api              # :5196
dotnet run --project src/HesapOzeti.Api -- seed      # 500 bin satır
dotnet ef migrations add <Ad> -p src/HesapOzeti.Api -o Data/Migrations
dotnet ef database update -p src/HesapOzeti.Api
perf/measure.sh "/accounts/1/transactions"           # API açıkken endpoint süresi (medyan)
```

- `dotnet` çıktısı Türkçe gelir. Çıktıyı okuyacaksan `DOTNET_CLI_UI_LANGUAGE=en` ver.
- NuGet'in "latest" sürümü 11.x. Paket eklerken 10.0.x sürümünü açıkça yaz.
- `sqlcmd`'de her zaman `-I` kullan, yoksa filtreli index'ler kullanılmaz.
- zsh'de içinde nokta olan ortam değişkenlerini `env 'A.B=c' komut` şeklinde ver.

## Değişmez kurallar

1. **Para `decimal`'dır**, kolon tipi `decimal(18,2)`. `double` ya da `float` kullanma.
2. **Kültür:** dışarıdan gelen tutarı ve tarihi her zaman `CultureInfo.InvariantCulture` ya da açık formatla oku ve yaz. Makine `tr-TR` locale'de, `"1250.50"` yanlış okunabilir.
3. **Hata yutma yok.** Banka çağrısı başarısız olunca boş liste dönme. Boş liste "hareket yok" demektir, "çağrı başarısız oldu" demek değildir. Hesap bazında yakala, Error seviyesinde logla, diğer hesaplara devam et.
4. **Banka formatı sadece `BankApiClient` içinde.** Bankanın DTO'ları dışarı sızmaz, geri kalan kod `BankTransactionDto` görür. Yeni sürüm eklenince eskisi config bayrağıyla seçilebilir kalır.
5. **ERP aktarımı idempotent kalmalı.** Anahtar `BankTransactionId`, garanti de `IX_ErpVouchers_BankTransactionId` unique index'i. Bu index'i kaldırma ve "önce var mı diye bak, sonra ekle" yöntemine geri dönme: iki işlem aynı anda çalışırsa ikisi de "yok" görür ve iki fiş yazılır.
6. **Filtreli index'i kullanacak sorgu, filtre koşulunu da yazmalı.** `ErpVouchers` sorgularında `CancelledAt == null` olmazsa 500 bin satır taranır.
7. **Liste sorguları:** `Select` ile projeksiyon kullan (tracking yok), satır başına ayrı sorgu (N+1) yazma, OFFSET yerine keyset (`TransactionDate, Id`) kullan, `pageSize`'a üst sınır koy.
8. **Finansal kayıt silinmez.** Düzeltme migration'ları kayıtları işaretler (`ErpSyncStatus.Duplicate`, `CancelledAt`) ve rapor scripti bırakır.
9. **Gerçek banka adı, IBAN ya da müşteri verisi yok.** Örnek veri `Demo Bank` ve `TR0000999900...` şeklinde. `.env` commit'lenmez.
10. **Retry sadece tekrar edilmesi güvenli işlerde.** Banka okuması güvenli. Fiş yazmak ancak idempotency anahtarıyla güvenli.

## Değişiklik yaparken

- Davranış değiştiren her düzeltmeye bir xUnit testi ekle. Önce hatayı gösteren testi yaz.
- Veritabanı testleri gerçek SQL Server'da çalışır. Her test `TestDb.NewAccountAsync` ile kendi hesabını açar, sabit ID'ye güvenme.
- Şema değişikliği her zaman migration ile yapılır. Unique index eklemeden önce mevcut veriyi kontrol et ve seed'in (`Seeder.GenerateRows`) yeni kurala uyduğundan emin ol.
- Performans değişikliğinde önce/sonra süresini (`perf/measure.sh`) ve okuma sayısını (`perf/list-plans.sql`) `docs/performance.md`'ye yaz.
- Commit mesajı: `HOZ-<no>: <ne değişti>`, gövdede nedeni yaz.
