using System.Globalization;
using BatHouseholdHub.Components;
using BatHouseholdHub.Services;
using Microsoft.AspNetCore.DataProtection;

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
builder.Services.AddHostedService<RecurringTransactionService>();
builder.Services.AddHttpClient<ProductLookupService>();
builder.Services.AddHttpClient<OpenGraphScraperService>();
builder.Services.AddHttpClient<HomeButlerService>();
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

app.Run();
