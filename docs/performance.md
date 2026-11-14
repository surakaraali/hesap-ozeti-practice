# HOZ-104: Hesap hareketleri listesi

Ana TL hesabı (hesap 1), yaklaşık 250 bin hareket. Tüm ölçümler bu makinede, Docker'daki SQL Server 2022 üzerinde yapıldı.
Endpoint süreleri `perf/measure.sh` ile alındı (5 ısınmış çağrının medyanı). Okuma sayıları ve plan'lar `perf/list-plans.sql` ile çıkarıldı.

## Endpoint süresi

| Konum | Önce (`?page=`) | Sonra (`?cursor=`) |
|---|---:|---:|
| İlk sayfa (toplam sayısı dahil) | 1.298 ms | 34 ms |
| 5.000. satır | 1.268 ms | 6 ms |
| 100.000. satır | 1.191 ms | 5 ms |
| 245.000. satır | 677 ms | 6 ms |

## Neden yavaştı

Önceden tek bir istek 61 SQL sorgusu çalıştırıyordu (EF Core komut logu):

1. Hesap, tracking açıkken tam entity olarak okunuyordu.
2. `COUNT(*)`: `TransactionDate` üzerinde index yoktu, o yüzden clustered index scan yapılıyordu (11.032 okuma).
3. Sayfa: tüm tablo okunup `Sort (TOP 100050)` çalışıyordu. OFFSET, atlanan satırların hepsini okuyup sıralamak zorunda.
4. Satır başına bir fiş sorgusu (N+1, 50 sorgu). En pahalı kısım buydu. `ErpVouchers.BankTransactionId` üzerindeki unique index filtreli (`WHERE CancelledAt IS NULL`). Sorguda bu koşul olmadığı için SQL Server index'i kullanamıyor ve her seferinde 500 bin fişi tarıyordu: 5.946 okuma × 50 ≈ 297 bin okuma.

## Ne değişti

- Index `(BankAccountId, TransactionDate, Id) INCLUDE (ErpSyncStatus)` eklendi. Hesaba seek yapıp en yeniden geriye doğru okuyor. `Id` aynı saniyedeki satırları ayırıyor. `INCLUDE` sayesinde `COUNT` ve `Duplicate` filtresi tabloya hiç gitmiyor.
- Keyset (cursor) sayfalama: `?cursor=<ticks>-<id>`. Her sayfa bir önceki sayfanın son satırından devam ediyor, bu yüzden sayfa 5000'in maliyeti sayfa 1 ile aynı.
- Tek sorgu: projeksiyon (`Select`) tracking yapmıyor, fiş numarası alt sorgudan geliyor ve `CancelledAt IS NULL` koşuluyla filtreli index'e seek yapıyor.
- Toplam sayısı sadece ilk sayfada hesaplanıyor. `pageSize` en fazla 200, üstü 400 dönüyor.

## Okuma sayıları (100.000. satırdaki sayfa)

| | Önce | Sonra |
|---|---:|---:|
| Toplam sayısı | 11.032 | 936 (sadece ilk sayfada) |
| Sayfa | 11.032 + sort | 168 |
| Fiş numaraları | ≈ 297.300 (50 sorgu) | 300 (aynı sorgu içinde) |

Plan'dan iki ders:
- Eski OFFSET sorgusu yeni index'le **daha kötü** çalışıyor: 306.791 okuma, çünkü atlanan her satır için tabloya key lookup yapılıyor. Index tek başına OFFSET sorununu çözmüyor, asıl çözüm keyset.
- Filtreli index'i sorgu ancak index'in filtre koşulunu da içerirse kullanabiliyor.

## API değişikliği

`page` parametresi kalktı, yerine `cursor` geldi. Cevapta `nextCursor` var, son sayfada `null` dönüyor. `total` sadece ilk sayfada dolu. Ekranın "sayfa 3'e atla" yerine "daha fazla yükle" mantığıyla çalışması gerekiyor. Belirli bir tarihe gitmek için `from`/`to` filtreleri kullanılabilir.

## Job retry

Banka çağrıları geçici hatalarda (408, 429, 5xx, ağ hatası) en fazla 3 kez deneniyor, aralarında 0,5 s ve 1 s bekleniyor (`Bank:MaxAttempts`, `Bank:RetryBaseDelayMs`). %30 hata oranıyla yapılan 5 senkronda 50 banka çağrısında 14 retry oldu ve sadece 1 hesap üç denemeden sonra da başarısız kaldı. Retry olmadan yaklaşık 15 çağrı başarısız olurdu. Kalan hatalar loglanıyor ve dün ile bugün her çalışmada yeniden okunduğu için veri kaybolmuyor.
