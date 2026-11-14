# HesapOzeti.Practice

Online hesap özeti benzeri küçük bir pratik projesi. Banka hareketlerini sahte bir bankadan çeker, SQL Server'a
yazar ve ERP'ye (Logo Tiger benzeri) fiş olarak aktarır. Haftalık ticket'lar [TASKS.md](TASKS.md) içinde.

## Çalıştırma

Gerekenler: .NET 10 SDK, Docker.

```bash
cp .env.example .env              # şifreyi değiştir
docker-compose up -d --wait       # SQL Server, localhost:14334
dotnet tool restore
dotnet run --project src/HesapOzeti.Api -- seed   # yaklaşık 500 bin hareket
dotnet run --project src/HesapOzeti.Api           # http://localhost:5196
dotnet test
```

Uç noktalar: `GET /accounts`, `GET /accounts/{id}/transactions?page=1`, `POST /sync/run`, `POST /erp/export`,
sahte banka `/fake-bank/...`.
