using EntityBuilder.Configuration;
using EntityBuilder.Data;
using EntityBuilder.Interfaces;
using EntityBuilder.Services;
using Microsoft.AspNetCore.Authentication.Cookies;

var builder = WebApplication.CreateBuilder(args);

// Bind configuration
builder.Services.Configure<DatabaseSettings>(
    builder.Configuration.GetSection(DatabaseSettings.SectionName));
builder.Services.Configure<AuthSettings>(
    builder.Configuration.GetSection(AuthSettings.SectionName));
builder.Services.Configure<CryptographySettings>(
    builder.Configuration.GetSection(CryptographySettings.SectionName));
builder.Services.Configure<MessagingSettings>(
    builder.Configuration.GetSection(MessagingSettings.SectionName));

// Register data layer
var providerType = builder.Configuration["DatabaseSettings:ProviderType"] ?? "SqlServer";
if (providerType == "SqlServer")
{
    builder.Services.AddSingleton<IDbConnectionFactory, SqlServerConnectionFactory>();
    builder.Services.AddScoped<IDatabaseMetadataService, SqlServerMetadataService>();
    builder.Services.AddScoped<IQueryExecutionService, SqlServerQueryExecutionService>();
}

// Services
builder.Services.AddScoped<IReportEmailService, ReportEmailService>();

// HTTP client for external API calls
builder.Services.AddHttpClient();

// Authentication
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Auth/Login";
        options.LogoutPath = "/Auth/Logout";
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
    });

builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "RequestVerificationToken";
});

builder.Services.AddControllersWithViews();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

app.Run();
