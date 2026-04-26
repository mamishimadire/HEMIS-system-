using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using HemisAudit.Data;
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
    options.SlidingExpiration = true;
});

builder.Services.AddScoped<IPasswordPolicyService, PasswordPolicyService>();
builder.Services.AddScoped<IEmailService, SmtpEmailService>();
builder.Services.AddScoped<PasswordAgeFilter>();
builder.Services.AddScoped<ISystemDatabaseService, SystemDatabaseService>();
builder.Services.AddHttpClient();
builder.Services.AddControllersWithViews(options =>
{
    options.Filters.AddService<PasswordAgeFilter>();
}).AddNewtonsoftJson();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IRule36Service, Rule36Service>();
builder.Services.AddScoped<IRule34Service, Rule34Service>();
builder.Services.AddScoped<IRule32Service, Rule32Service>();
builder.Services.AddScoped<IExportService, ExportService>();
builder.Services.AddScoped<IAuditLogService, AuditLogService>();

var app = builder.Build();

await SystemDatabaseBootstrapper.EnsureCreatedAsync(app.Configuration);

using (var scope = app.Services.CreateScope())
{
    await DbInitializer.SeedAsync(scope.ServiceProvider);
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
    app.UseHttpsRedirection();
}

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

app.MapControllerRoute(
    name: "dashboard-short",
    pattern: "Dashboard",
    defaults: new { controller = "Dashboard", action = "Index" });

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
