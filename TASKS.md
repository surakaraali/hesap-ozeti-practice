# Haftanın ticket'ları

Online Hesap Özeti ekibinde bir hafta. Her gün bir ticket.

---

## HOZ-101 · Pazartesi · Banka API v2'ye geçiş

**Tür:** Geliştirme · **Öncelik:** Yüksek

Demo Bank'tan duyuru geldi: hesap hareketleri servisinin v1'i 31.12.2026'da kapanıyor. v1 cevaplarında artık
`Deprecation` ve `Sunset` header'ları dönüyor. v2 dokümanına göre:

- Endpoint `/fake-bank/v2/transactions` oldu.
- Alan adları değişti (`refNo` → `referenceNumber`, `txnDate` → `valueDate` vb.).
- Tarihler `dd.MM.yyyy` yerine ISO 8601 geliyor, sorgudaki `date` parametresi de öyle.
- `amount` artık string değil, number.

Bizim adapter hâlâ v1'i çağırıyor. Sunset tarihinden önce v2'ye geçmemiz lazım.

**Kabul kriterleri**
- Senkron v2 üzerinden çalışıyor, kaydedilen hareketler v1 ile aynı.
- Gerekirse v1'e geri dönebiliyoruz.

---

## HOZ-102 · Salı · "Bugünün hareketleri gelmemiş"

**Tür:** Bug · **Öncelik:** Kritik

Müşteri destek kaydı: *"Sabah 10'dan beri ekranda yeni hareket yok, ama bankada var."*

Logda hata görünmüyor, senkron job her dakika `Sync finished, 0 new transactions` yazıyor. API'yi yeniden
başlatınca düzeliyor, bir süre sonra tekrar başlıyor.

**Kabul kriterleri**
- Kök neden bulundu ve düzeltildi.
- Banka hata döndüğünde bunu loglardan anlayabiliyoruz.
- Kaçan hareketler bir sonraki senkronda geliyor.

---

## HOZ-103 · Çarşamba · ERP'ye mükerrer tahsilat aktarımı

**Tür:** Bug · **Öncelik:** Kritik

Muhasebe ekibi Tiger'da aynı tahsilattan iki fiş görmüş, cari bakiye yanlış çıkıyor. Birkaç örnek:
bazı fişler aynı banka hareketine bağlı, bazı hareketler de bizde iki kez kayıtlı (aynı banka referans no).

Ayrıca Tiger entegrasyonunda ara sıra `Tiger timed out` uyarısı görülüyor.

**Kabul kriterleri**
- Aynı banka hareketi ERP'ye bir kereden fazla aktarılamıyor.
- Aynı banka referansıyla ikinci hareket kaydedilemiyor.
- Mevcut mükerrer kayıtların listesi çıkarıldı.

---

## HOZ-104 · Perşembe · Hesap hareketleri ekranı yavaş

**Tür:** Performans · **Öncelik:** Yüksek

Ana TL hesabında (yaklaşık 250 bin hareket) `GET /accounts/{id}/transactions` her sayfada 1 saniyeyi geçiyor.
Müşteri ileri sayfalara gittikçe daha da yavaşladığını söylüyor.

Ayrıca senkron job'ı bankadan geçici hata (503) alınca o dakikanın verisini atlıyor, tekrar denemiyor.

**Kabul kriterleri**
- Liste ekranının önce/sonra süreleri ölçüldü.
- İlk sayfa 100 ms'nin altında.
- Geçici banka hatalarında job tekrar deniyor.

---

## HOZ-105 · Cuma · Dokümantasyon

**Tür:** Dokümantasyon · **Öncelik:** Normal

Yeni katılan ekip arkadaşı projeyi kendi başına ayağa kaldıramadı.

**Kabul kriterleri**
- README: proje ne yapıyor, nasıl çalıştırılır, mimari kısaca, job'lar ve ayarlar.
- Ekipte AI asistanla çalışanlar için `CLAUDE.md`: komutlar, kurallar, dikkat edilecekler.
