using System.Globalization;
using BatHouseholdHub.Components;
using BatHouseholdHub.Services;
using Microsoft.AspNetCore.DataProtection;
using Going.Plaid;

var usCulture = new CultureInfo("en-US");
CultureInfo.DefaultThreadCurrentCulture = usCulture;
CultureInfo.DefaultThreadCurrentUICulture = usCulture;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
var dataFolder = Path.Combine(builder.Environment.ContentRootPath, "App_Data");
Directory.CreateDirectory(dataFolder);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
// Mobile/cellular connections drop and re-establish more than wifi; keep circuits
// around longer so a flaky connection reconnects instead of silently losing state.
builder.Services.AddServerSideBlazor(options =>
{
    options.DisconnectedCircuitMaxRetained = 100;
    options.DisconnectedCircuitRetentionPeriod = TimeSpan.FromMinutes(5);
    options.JSInteropDefaultCallTimeout = TimeSpan.FromSeconds(30);
});
builder.Services.AddSingleton<HouseholdStore>();
builder.Services.AddScoped<CashflowService>();
builder.Services.AddScoped<BillCalendarService>();
builder.Services.AddScoped<ActivePersonService>();
builder.Services.AddHostedService<RecurringTransactionService>();
builder.Services.AddHostedService<HouseholdBackupService>();
builder.Services.AddHostedService<PushNotificationService>();
builder.Services.AddHttpClient<ProductLookupService>();
builder.Services.AddHttpClient<OpenGraphScraperService>();
builder.Services.AddHttpClient<HomeButlerService>();

var plaidEnv = builder.Configuration["PLAID_ENV"] switch
{
    "production" => Going.Plaid.Environment.Production,
    "development" => Going.Plaid.Environment.Development,
    _ => Going.Plaid.Environment.Sandbox
};
builder.Services.AddSingleton(new PlaidClient(plaidEnv,
    secret: builder.Configuration["PLAID_SECRET"] ?? "",
    clientId: builder.Configuration["PLAID_CLIENT_ID"] ?? ""));
builder.Services.AddScoped<PlaidService>();
builder.Services.AddHostedService<PlaidSyncService>();
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(dataFolder, "keys")))
    .SetApplicationName("BatHouseholdHub");

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapGet("/uploads/{id:guid}", (Guid id, HouseholdStore store) =>
{
    var record = store.Data.UploadedFiles.FirstOrDefault(x => x.Id == id);
    if (record is null || !File.Exists(store.UploadPath(id))) return Results.NotFound();
    return Results.File(store.UploadPath(id), record.ContentType, record.FileName);
});

app.MapGet("/api/push/public-key", (IConfiguration config) =>
    Results.Text(config["VAPID_PUBLIC_KEY"] ?? ""));

app.MapPost("/api/push/subscribe", async (PushSubscribeRequest req, HouseholdStore store) =>
{
    await store.AddPushSubscriptionAsync(req.Endpoint, req.P256dh, req.Auth, req.Owner ?? "Shared");
    return Results.Ok();
});

app.MapPost("/api/push/unsubscribe", async (PushUnsubscribeRequest req, HouseholdStore store) =>
{
    await store.RemovePushSubscriptionAsync(req.Endpoint);
    return Results.Ok();
});

app.MapGet("/api/plaid/configured", (PlaidService plaid) => Results.Ok(new { configured = plaid.IsConfigured }));

app.MapPost("/api/plaid/link-token", async (PlaidLinkTokenRequest req, PlaidService plaid) =>
{
    if (!plaid.IsConfigured) return Results.BadRequest("Plaid is not configured on the server.");
    try { return Results.Ok(new { linkToken = await plaid.CreateLinkTokenAsync(req.Owner) }); }
    catch (Exception ex) { return Results.BadRequest(ex.Message); }
});

app.MapPost("/api/plaid/exchange", async (PlaidExchangeRequest req, PlaidService plaid) =>
{
    try { var item = await plaid.ExchangePublicTokenAsync(req.PublicToken, req.InstitutionName, req.Owner); return Results.Ok(new { id = item.Id }); }
    catch (Exception ex) { return Results.BadRequest(ex.Message); }
});

app.MapPost("/api/plaid/disconnect", async (PlaidDisconnectRequest req, HouseholdStore store) =>
{
    await store.RemovePlaidItemAsync(req.Id);
    return Results.Ok();
});

app.Run();

record PushSubscribeRequest(string Endpoint, string P256dh, string Auth, string? Owner);
record PushUnsubscribeRequest(string Endpoint);
record PlaidLinkTokenRequest(string Owner);
record PlaidExchangeRequest(string PublicToken, string InstitutionName, string Owner);
record PlaidDisconnectRequest(Guid Id);
