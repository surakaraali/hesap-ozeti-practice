using HesapOzeti.Api.Data;
using HesapOzeti.Api.Erp;
using HesapOzeti.Api.FakeBank;
using HesapOzeti.Api.Sync;
using Microsoft.EntityFrameworkCore;
using Quartz;

LocalDb.LoadDotEnv();
var builder = WebApplication.CreateBuilder(args);
var connectionString = builder.Configuration.GetConnectionString("Default") ?? LocalDb.ConnectionString();

// The factory also registers AppDbContext itself as a scoped service.
builder.Services.AddDbContextFactory<AppDbContext>(o => o.UseSqlServer(connectionString));
builder.Services.AddHttpClient<BankApiClient>(c => c.BaseAddress = new Uri(builder.Configuration["Bank:BaseUrl"]!));
builder.Services.AddScoped<TransactionSyncService>();
builder.Services.AddScoped<ErpExportService>();
builder.Services.AddSingleton<FakeTigerClient>();

if (builder.Configuration.GetValue("Jobs:Enabled", true))
{
    var startAt = DateTimeOffset.UtcNow.AddSeconds(10); // let Kestrel start first, the sync job calls this same API
    builder.Services.AddQuartz(q =>
    {
        q.ScheduleJob<TransactionSyncJob>(t => t.WithIdentity("transaction-sync").StartAt(startAt)
            .WithSimpleSchedule(s => s.WithInterval(TimeSpan.FromSeconds(builder.Configuration.GetValue("Jobs:SyncIntervalSeconds", 60))).RepeatForever()));
        q.ScheduleJob<ErpExportJob>(t => t.WithIdentity("erp-export").StartAt(startAt)
            .WithSimpleSchedule(s => s.WithInterval(TimeSpan.FromSeconds(builder.Configuration.GetValue("Jobs:ExportIntervalSeconds", 60))).RepeatForever()));
    });
    builder.Services.AddQuartzHostedService(o => o.WaitForJobsToComplete = true);
}

var app = builder.Build();

if (args.Contains("seed"))
{
    using var scope = app.Services.CreateScope();
    await Seeder.RunAsync(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    return;
}

using (var scope = app.Services.CreateScope())
    await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();

app.MapFakeBank();

app.MapGet("/accounts", (AppDbContext db) => db.BankAccounts.AsNoTracking().ToListAsync());

app.MapGet("/accounts/{id:int}/transactions", async (int id, int? page, int? pageSize, DateTime? from, DateTime? to, AppDbContext db) =>
{
    var account = await db.BankAccounts.FirstOrDefaultAsync(a => a.Id == id);
    if (account is null) return Results.NotFound();

    var p = page ?? 1;
    var size = pageSize ?? 50;
    var query = db.BankTransactions.Where(t => t.BankAccountId == id);
    if (from is not null) query = query.Where(t => t.TransactionDate >= from);
    if (to is not null) query = query.Where(t => t.TransactionDate < to);

    var total = await query.CountAsync();
    var rows = await query.OrderByDescending(t => t.TransactionDate).Skip((p - 1) * size).Take(size).ToListAsync();

    var items = new List<TransactionRow>();
    foreach (var t in rows)
    {
        var voucher = await db.ErpVouchers.FirstOrDefaultAsync(v => v.BankTransactionId == t.Id);
        items.Add(new TransactionRow(t.Id, t.BankReferenceNo, t.TransactionDate, t.Amount, t.Currency, t.Description,
            t.Direction.ToString(), t.ErpSyncStatus.ToString(), voucher?.VoucherNo));
    }
    return Results.Ok(new { account.Iban, total, page = p, pageSize = size, items });
});

// Manual triggers, handy while practising. The Quartz jobs call the same services.
app.MapPost("/sync/run", async (TransactionSyncService sync) =>
    Results.Ok(new { added = await sync.SyncAsync(DateOnly.FromDateTime(DateTime.UtcNow.AddHours(3))) }));
app.MapPost("/erp/export", async (ErpExportService export) => Results.Ok(new { exported = await export.ExportPendingAsync() }));

app.Run();

public record TransactionRow(long Id, string BankReferenceNo, DateTime TransactionDate, decimal Amount, string Currency,
    string Description, string Direction, string ErpSyncStatus, string? VoucherNo);
