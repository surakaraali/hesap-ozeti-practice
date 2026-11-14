using HesapOzeti.Api.Data;
using HesapOzeti.Api.Erp;
using HesapOzeti.Api.FakeBank;
using HesapOzeti.Api.Statements;
using HesapOzeti.Api.Sync;
using Microsoft.EntityFrameworkCore;
using Quartz;

LocalDb.LoadDotEnv();
var builder = WebApplication.CreateBuilder(args);
var connectionString = builder.Configuration.GetConnectionString("Default") ?? LocalDb.ConnectionString();

// The factory also registers AppDbContext itself as a scoped service.
builder.Services.AddDbContextFactory<AppDbContext>(o => o.UseSqlServer(connectionString));
builder.Services.AddHttpClient<BankApiClient>(c => c.BaseAddress = new Uri(builder.Configuration["Bank:BaseUrl"]!));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<BankTokenStore>();
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

app.MapGet("/accounts/{id:int}/transactions", async (int id, string? cursor, int? pageSize, DateTime? from, DateTime? to,
    AppDbContext db, CancellationToken ct) =>
{
    var size = pageSize ?? 50;
    if (size is < 1 or > TransactionList.MaxPageSize)
        return Results.BadRequest(new { error = $"pageSize must be between 1 and {TransactionList.MaxPageSize}" });
    Cursor? after = null;
    if (cursor is not null)
    {
        if (!Cursor.TryParse(cursor, out var c)) return Results.BadRequest(new { error = "invalid cursor" });
        after = c;
    }

    var page = await TransactionList.GetPageAsync(db, id, after, size, from, to, ct);
    return page is null ? Results.NotFound() : Results.Ok(page);
});

// Manual triggers, handy while practising. The Quartz jobs call the same services.
app.MapPost("/sync/run", async (TransactionSyncService sync) =>
    Results.Ok(await sync.SyncAsync(DateOnly.FromDateTime(DateTime.UtcNow.AddHours(3)))));
app.MapPost("/erp/export", async (ErpExportService export) => Results.Ok(new { exported = await export.ExportPendingAsync() }));

app.Run();
