using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using System.IO.Compression;
using HemisAudit.Data;
using HemisAudit.Filters;
using HemisAudit.Models;
using HemisAudit.Services;

var builder = WebApplication.CreateBuilder(args);

// SQLite-backed application database for the current MVC app
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options =>
{
    options.Password.RequireDigit = true;
    options.Password.RequireLowercase = true;
    options.Password.RequireUppercase = true;
    options.Password.RequireNonAlphanumeric = true;
    options.Password.RequiredLength = 8;

    options.Lockout.MaxFailedAccessAttempts = 5;
    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    options.Lockout.AllowedForNewUsers = true;

    options.User.RequireUniqueEmail = true;
})
.AddEntityFrameworkStores<ApplicationDbContext>()
.AddDefaultTokenProviders();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Account/Login";
    options.AccessDeniedPath = "/Account/AccessDenied";
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.SlidingExpiration = false;
});

builder.Services.AddScoped<IPasswordPolicyService, PasswordPolicyService>();
builder.Services.AddScoped<IEmailService, SmtpEmailService>();
builder.Services.AddScoped<PasswordAgeFilter>();
builder.Services.AddScoped<ISystemDatabaseService, SystemDatabaseService>();
builder.Services.AddHttpClient();
builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
    options.Providers.Add<BrotliCompressionProvider>();
    options.Providers.Add<GzipCompressionProvider>();
    options.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(new[]
    {
        "application/json",
        "application/sql",
        "text/csv"
    });
});
builder.Services.Configure<BrotliCompressionProviderOptions>(options =>
{
    options.Level = CompressionLevel.Fastest;
});
builder.Services.Configure<GzipCompressionProviderOptions>(options =>
{
    options.Level = CompressionLevel.Fastest;
});
builder.Services.AddControllersWithViews(options =>
{
    options.Filters.AddService<PasswordAgeFilter>();
}).AddNewtonsoftJson();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IRule22Service, Rule22Service>();
builder.Services.AddScoped<IRule18Service, Rule18Service>();
builder.Services.AddScoped<IRule19Service, Rule19Service>();
builder.Services.AddScoped<IRule20Service, Rule20Service>();
builder.Services.AddScoped<IRule21Service, Rule21Service>();
builder.Services.AddScoped<IRule23Service, Rule23Service>();
builder.Services.AddScoped<IRule24Service, Rule24Service>();
builder.Services.AddScoped<IRule25Service, Rule25Service>();
builder.Services.AddScoped<IRule26Service, Rule26Service>();
builder.Services.AddScoped<IRule36Service, Rule36Service>();
builder.Services.AddScoped<IRule34Service, Rule34Service>();
builder.Services.AddScoped<IRule32Service, Rule32Service>();
builder.Services.AddScoped<IRule31Service, Rule31Service>();
builder.Services.AddScoped<IRule30Service, Rule30Service>();
builder.Services.AddScoped<IRule29Service, Rule29Service>();
builder.Services.AddScoped<IRule27Service, Rule27Service>();
builder.Services.AddScoped<IExportService, ExportService>();
builder.Services.AddScoped<IAuditLogService, AuditLogService>();

var app = builder.Build();

await SystemDatabaseBootstrapper.EnsureCreatedAsync(app.Configuration);

using (var scope = app.Services.CreateScope())
{
    var systemDb = scope.ServiceProvider.GetRequiredService<ISystemDatabaseService>();
    await systemDb.EnsurePerformanceObjectsAsync();
    await DbInitializer.SeedAsync(scope.ServiceProvider);
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseResponseCompression();
app.UseStaticFiles();
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(Path.Combine(app.Environment.WebRootPath, "uploads", "messages")),
    RequestPath = "/uploads/messages",
    ServeUnknownFileTypes = true,
    DefaultContentType = "application/octet-stream",
    OnPrepareResponse = context =>
    {
        context.Context.Response.Headers.Append("X-Content-Type-Options", "nosniff");
    }
});
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.Use(async (context, next) =>
{
    context.Response.OnStarting(() =>
    {
        if (context.User.Identity?.IsAuthenticated == true &&
            HttpMethods.IsGet(context.Request.Method))
        {
            context.Response.Headers["Cache-Control"] = "no-store, no-cache, max-age=0, must-revalidate";
            context.Response.Headers["Pragma"] = "no-cache";
            context.Response.Headers["Expires"] = "0";
        }

        return Task.CompletedTask;
    });

    await next();
});

app.MapControllerRoute(
    name: "dashboard-short",
    pattern: "Dashboard",
    defaults: new { controller = "Dashboard", action = "Index" });

app.MapControllerRoute(
    name: "rule18-short",
    pattern: "Rule18",
    defaults: new { controller = "Rule18", action = "Index" });

app.MapControllerRoute(
    name: "rule19-short",
    pattern: "Rule19",
    defaults: new { controller = "Rule19", action = "Index" });

app.MapControllerRoute(
    name: "rule20-short",
    pattern: "Rule20",
    defaults: new { controller = "Rule20", action = "Index" });

app.MapControllerRoute(
    name: "rule21-short",
    pattern: "Rule21",
    defaults: new { controller = "Rule21", action = "Index" });

app.MapControllerRoute(
    name: "rule22-short",
    pattern: "Rule22",
    defaults: new { controller = "Rule22", action = "Index" });

app.MapControllerRoute(
    name: "rule23-short",
    pattern: "Rule23",
    defaults: new { controller = "Rule23", action = "Index" });

app.MapControllerRoute(
    name: "rule24-short",
    pattern: "Rule24",
    defaults: new { controller = "Rule24", action = "Index" });

app.MapControllerRoute(
    name: "rule25-short",
    pattern: "Rule25",
    defaults: new { controller = "Rule25", action = "Index" });

app.MapControllerRoute(
    name: "rule26-short",
    pattern: "Rule26",
    defaults: new { controller = "Rule26", action = "Index" });

app.MapControllerRoute(
    name: "rule29-short",
    pattern: "Rule29",
    defaults: new { controller = "Rule29", action = "Index" });

app.MapControllerRoute(
    name: "rule27-short",
    pattern: "Rule27",
    defaults: new { controller = "Rule27", action = "Index" });

app.MapControllerRoute(
    name: "rule30-short",
    pattern: "Rule30",
    defaults: new { controller = "Rule30", action = "Index" });

app.MapControllerRoute(
    name: "rule31-short",
    pattern: "Rule31",
    defaults: new { controller = "Rule31", action = "Index" });

app.MapControllerRoute(
    name: "rule32-short",
    pattern: "Rule32",
    defaults: new { controller = "Rule32", action = "Index" });

app.MapControllerRoute(
    name: "rule36-short",
    pattern: "Rule36",
    defaults: new { controller = "Rule36", action = "Index" });

app.MapControllerRoute(
    name: "rule34-short",
    pattern: "Rule34",
    defaults: new { controller = "Rule34", action = "Index" });

app.MapControllerRoute(
    name: "messages-short",
    pattern: "Messages",
    defaults: new { controller = "Messages", action = "Index" });

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Account}/{action=Login}/{id?}");

app.Run();
