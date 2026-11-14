using System.Collections.Concurrent;
using System.Globalization;
using HesapOzeti.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace HesapOzeti.Api.FakeBank;

/// <summary>
/// A made-up bank ("Demo Bank") that runs inside the same API. It hands out short-lived tokens and
/// returns generated transactions for the accounts in our database. No real bank, no real data.
/// </summary>
public static class FakeBankEndpoints
{
    public const string ClientId = "practice-client";
    public const string ClientSecret = "practice-secret"; // local fake only, not a real secret

    static readonly ConcurrentDictionary<string, DateTime> Tokens = new();

    public static void MapFakeBank(this WebApplication app)
    {
        var bank = app.MapGroup("/fake-bank");
        var config = app.Configuration;

        bank.MapPost("/oauth/token", (TokenRequest req) =>
        {
            if (req.ClientId != ClientId || req.ClientSecret != ClientSecret)
                return Results.Json(new { error = "invalid_client" }, statusCode: 401);

            var lifetime = config.GetValue("FakeBank:TokenLifetimeSeconds", 120);
            var token = Guid.NewGuid().ToString("N");
            Tokens[token] = DateTime.UtcNow.AddSeconds(lifetime);
            return Results.Ok(new { accessToken = token, expiresIn = lifetime });
        });

        // v1: dates are "dd.MM.yyyy HH:mm:ss" strings, amounts are strings.
        bank.MapGet("/v1/transactions", async (string iban, string date, HttpContext http, AppDbContext db) =>
        {
            if (Check(http, config) is { } error) return error;
            if (!DateOnly.TryParseExact(date, "dd.MM.yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
                return Results.BadRequest(new { error = "date must be dd.MM.yyyy" });
            var currency = await AccountCurrency(db, iban);
            if (currency is null) return Results.NotFound(new { error = "unknown iban" });

            http.Response.Headers["Deprecation"] = "true";
            http.Response.Headers["Sunset"] = "Thu, 31 Dec 2026 23:59:59 GMT";
            return Results.Ok(new
            {
                accountIban = iban,
                transactions = Generate(iban, day, currency).Select(t => new
                {
                    refNo = t.ReferenceNo,
                    txnDate = t.Date.ToString("dd.MM.yyyy HH:mm:ss", CultureInfo.InvariantCulture),
                    amount = t.Amount.ToString("0.00", CultureInfo.InvariantCulture),
                    currency = t.Currency,
                    explanation = t.Description,
                    type = t.Direction == Direction.Incoming ? "A" : "B",
                }),
            });
        });

        // v2: new field names, ISO 8601 dates with offset, numeric amounts.
        bank.MapGet("/v2/transactions", async (string iban, DateOnly date, HttpContext http, AppDbContext db) =>
        {
            if (Check(http, config) is { } error) return error;
            var currency = await AccountCurrency(db, iban);
            if (currency is null) return Results.NotFound(new { error = "unknown iban" });

            return Results.Ok(new
            {
                iban,
                items = Generate(iban, date, currency).Select(t => new
                {
                    referenceNumber = t.ReferenceNo,
                    valueDate = new DateTimeOffset(t.Date, TimeSpan.FromHours(3)),
                    amount = t.Amount,
                    currencyCode = t.Currency,
                    description = t.Description,
                    direction = t.Direction == Direction.Incoming ? "CREDIT" : "DEBIT",
                }),
            });
        });
    }

    public record TokenRequest(string ClientId, string ClientSecret);

    public record GeneratedTransaction(string ReferenceNo, DateTime Date, decimal Amount, string Currency, string Description, Direction Direction);

    /// <summary>Returns 401 for a missing or expired token, and sometimes 503 like a real bank under load.</summary>
    static IResult? Check(HttpContext http, IConfiguration config)
    {
        var header = http.Request.Headers.Authorization.ToString();
        var token = header.StartsWith("Bearer ") ? header["Bearer ".Length..] : "";
        if (!Tokens.TryGetValue(token, out var expiresAt) || expiresAt < DateTime.UtcNow)
            return Results.Json(new { error = "invalid_token", error_description = "The access token is missing or expired." }, statusCode: 401);

        if (Random.Shared.NextDouble() < config.GetValue("FakeBank:FailureRate", 0.05))
            return Results.Json(new { error = "service_unavailable" }, statusCode: 503);
        return null;
    }

    static Task<string?> AccountCurrency(AppDbContext db, string iban) =>
        db.BankAccounts.Where(a => a.Iban == iban).Select(a => a.Currency).FirstOrDefaultAsync();

    /// <summary>The same IBAN and day always give the same transactions. For today only the ones up to now.</summary>
    public static List<GeneratedTransaction> Generate(string iban, DateOnly day, string currency)
    {
        var nowInTurkey = DateTime.UtcNow.AddHours(3); // Turkey is UTC+3 all year
        var seed = unchecked(iban.Aggregate(17, (h, c) => h * 31 + c) + day.DayNumber);
        var rnd = new Random(seed);
        var count = rnd.Next(20, 60);

        return Enumerable.Range(1, count)
            .Select(i =>
            {
                var date = day.ToDateTime(TimeOnly.MinValue).AddSeconds(rnd.Next(0, 86_400));
                var direction = rnd.Next(2) == 0 ? Direction.Incoming : Direction.Outgoing;
                return new GeneratedTransaction(
                    $"DB{day:yyyyMMdd}{iban[^4..]}{i:D4}", date, SampleData.Amount(rnd), currency,
                    SampleData.Description(rnd, direction), direction);
            })
            .Where(t => t.Date <= nowInTurkey)
            .OrderBy(t => t.Date)
            .ToList();
    }
}

/// <summary>Made-up company names and descriptions shared by the fake bank and the seeder.</summary>
public static class SampleData
{
    static readonly string[] Companies =
        ["Demo Gıda Ltd.", "Örnek Yapı A.Ş.", "Deneme Tekstil", "Test Lojistik", "Kurgu Elektrik", "Mavi Kırtasiye", "Yeşil Tarım Koop.", "Atlas Otomotiv"];

    static readonly string[] IncomingTemplates = ["EFT GELEN - {0} - Fatura tahsilatı", "HAVALE - {0} - Cari ödeme", "POS TAHSİLAT - Gün sonu", "FAST - {0}"];
    static readonly string[] OutgoingTemplates = ["EFT GİDEN - {0} - Mal alımı", "Kira ödemesi", "SGK prim ödemesi", "Vergi ödemesi - KDV", "Maaş ödemesi", "Havale masrafı"];

    public static decimal Amount(Random rnd) => Math.Round((decimal)Math.Exp(2 + rnd.NextDouble() * 9), 2);

    public static string Description(Random rnd, Direction direction)
    {
        var templates = direction == Direction.Incoming ? IncomingTemplates : OutgoingTemplates;
        return string.Format(templates[rnd.Next(templates.Length)], Companies[rnd.Next(Companies.Length)]);
    }
}
