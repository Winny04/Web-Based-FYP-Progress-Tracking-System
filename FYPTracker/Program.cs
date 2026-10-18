
using FYPTracker.Configuration;
using FYPTracker.Configuration.Auhtorization;
using FYPTracker.Helpers;
using FYPTracker.Models;

using FYPTracker.Services.Authentication;
using FYPTracker.Services.Employees;
using FYPTracker.Services.Others;
using FYPTracker.Services.Others.Interfaces;
using FYPTracker.Services.Quartz;
using FYPTracker.Services.BackgroundTasks;
using FYPTracker.Hubs;

using Microsoft.Data.SqlClient;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.AspNetCore.Http.Features;

using Newtonsoft.Json;
using Quartz;
using System;
using System.Collections.Generic;
using System.Net;

var builder = WebApplication.CreateBuilder(args);

// =====================================================
// 1. MVC / Razor Pages
// =====================================================

builder.Services.AddRazorPages();

builder.Services.AddControllersWithViews()
    .AddNewtonsoftJson(options =>
    {
        options.SerializerSettings.ReferenceLoopHandling =
            ReferenceLoopHandling.Ignore;
    });

// IMPORTANT:
// Do NOT manually add appsettings.json again.
// WebApplication.CreateBuilder already loads:
// appsettings.json
// appsettings.{Environment}.json
// environment variables

builder.Services.AddHttpContextAccessor();

// =====================================================
// 2. DATABASE CONFIGURATION & SAFETY GUARDS
// =====================================================

string connectionString =
    builder.Configuration.GetConnectionString("Entities")
    ?? throw new InvalidOperationException(
        "Database connection string 'Entities' is missing.");

var sqlConnectionBuilder =
    new SqlConnectionStringBuilder(connectionString);

string dbName = sqlConnectionBuilder.InitialCatalog;
string serverName = sqlConnectionBuilder.DataSource;
string environment = builder.Environment.EnvironmentName;

if (string.IsNullOrWhiteSpace(dbName))
{
    throw new InvalidOperationException(
        "SAFETY STOP: Database name is missing.");
}

// Development must use Development database.
if (builder.Environment.IsDevelopment())
{
    if (!string.Equals(
        dbName,
        "FYPTracker_Development",
        StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException(
            "SAFETY STOP: Development must use " +
            "FYPTracker_Development.");
    }
}

// Testing must use ONLY the local Test database.
if (builder.Environment.IsEnvironment("Testing"))
{
    if (!string.Equals(
        dbName,
        "FYPTracker_Test",
        StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException(
            "SAFETY STOP: Testing must use FYPTracker_Test.");
    }

    if (!string.Equals(
        serverName,
        @"Boon\SQLEXPRESS",
        StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException(
            "SAFETY STOP: Testing must use " +
            @"Boon\SQLEXPRESS.");
    }

    if (!builder.Configuration.GetValue<bool>(
        "SmtpSettings:Offline"))
    {
        throw new InvalidOperationException(
            "SAFETY STOP: Testing requires Offline SMTP.");
    }
}

Console.WriteLine("====================================");
Console.WriteLine($"FYPTracker Environment: {environment}");
Console.WriteLine($"FYPTracker Server: {serverName}");
Console.WriteLine($"FYPTracker Database: {dbName}");
Console.WriteLine("====================================");

builder.Services.AddDbContext<FYPTrackerContext>(options =>
    options.UseSqlServer(connectionString, sqlOptions =>
    {
        sqlOptions.CommandTimeout(80);
    }));
// =====================================================
// 3. REGISTER SERVICES
// =====================================================

builder.Services.AddSingleton<
    IHttpContextAccessor,
    HttpContextAccessor>();

// Register Centralized Clock Provider (Development uses RealSystemClock; Testing uses TestingSystemClock)
if (builder.Environment.IsEnvironment("Testing"))
{
    builder.Services.AddSingleton<FYPTracker.Services.Time.ISystemClock, FYPTracker.Services.Time.TestingSystemClock>();
}
else
{
    builder.Services.AddSingleton<FYPTracker.Services.Time.ISystemClock, FYPTracker.Services.Time.RealSystemClock>();
}

builder.Services.AddHttpClient();

builder.Services.AddScoped<IWebHelper, WebHelper>();

builder.Services.AddScoped<
    IAuthenticationService,
    CookieAuthenticationService>();

builder.Services.AddScoped<IUserService, UserService>();

builder.Services.AddScoped<IEmailService, EmailService>();

builder.Services.AddTransient<IFileWriter, FileWriter>();
builder.Services.AddScoped<FYPTracker.Services.Testing.ITestingModePolicy, FYPTracker.Services.Testing.TestingModePolicy>();

// Existing background services.
// Keep original workflow unchanged.
builder.Services.AddHostedService<MeetingReminderService>();

builder.Services.AddHostedService<
    SupervisorRequestTimeoutService>();

// =====================================================
// 4. SMTP CONFIGURATION
// =====================================================

var smtpSettings = new SmtpSettings();

builder.Configuration.Bind(
    nameof(SmtpSettings),
    smtpSettings);

builder.Services.AddSingleton(smtpSettings);

// =====================================================
// 5. ENTITY FRAMEWORK / SQL SERVER
// =====================================================

builder.Services.AddDbContext<FYPTrackerContext>(options =>
    options.UseSqlServer(
        connectionString,
        sqlOptions =>
        {
            sqlOptions.CommandTimeout(80);
        }));

// =====================================================
// 6. CONTROLLERS / SESSION / SIGNALR
// =====================================================

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = null;
    });

builder.Services.AddSession(options =>
{
    // Separate Testing session cookie from Development.
    if (builder.Environment.IsEnvironment("Testing"))
    {
        options.Cookie.Name =
            ".AspNetCore.FYPTracker.Testing.Session";
    }
});

builder.Services.AddSignalR();

builder.Services.AddTransient<
    IAuthorizationHandler,
    UserTypeHandler>();

builder.Services.AddControllersWithViews()
    .AddRazorRuntimeCompilation();

// =====================================================
// 7. EXISTING CONTEXT INITIALIZATION
// =====================================================

// Preserved to avoid changing existing initialization
// behavior during Testing setup.
using (var scope =
    builder.Services.BuildServiceProvider().CreateScope())
{
    var _ctx =
        scope.ServiceProvider
            .GetRequiredService<FYPTrackerContext>();

    // Context initialization logic here if needed.
}

var serviceProvider =
    builder.Services.BuildServiceProvider();

// =====================================================
// 8. COOKIE POLICY
// =====================================================

builder.Services.Configure<CookiePolicyOptions>(options =>
{
    options.CheckConsentNeeded = context => false;
    options.MinimumSameSitePolicy = SameSiteMode.None;
});

// =====================================================
// 9. AUTHENTICATION
// =====================================================

builder.Services
    .AddAuthentication(
        AuthenticationDefaults.AuthenticationScheme)
    .AddCookie(
        AuthenticationDefaults.AuthenticationScheme,
        options =>
        {
            // Different authentication cookies for
            // Testing and Development.
            options.Cookie.Name =
                builder.Environment.IsEnvironment("Testing")
                ? ".AspNetCore.FYPTracker.Testing.Auth"
                : $"{CookieDefaults.Prefix}" +
                  $"{CookieDefaults.AuthenticationCookie}";

            options.Cookie.HttpOnly = true;

            options.ExpireTimeSpan =
                TimeSpan.FromHours(8);

            options.Cookie.SecurePolicy =
                CookieSecurePolicy.SameAsRequest;

            options.LoginPath =
                AuthenticationDefaults.LoginPath;

            options.AccessDeniedPath =
                AuthenticationDefaults.AccessDeniedPath;
        });

builder.Services.AddAuthorization(options =>
{
});

// =====================================================
// 10. UPLOAD SIZE CONFIGURATION
// =====================================================

builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit =
        100L * 1024 * 1024;
});

builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize =
        100L * 1024 * 1024;
});

builder.Services.Configure<IISServerOptions>(options =>
{
    options.MaxRequestBodySize =
        100L * 1024 * 1024;
});

// =====================================================
// 11. FYP FORM / MEETING / DEFENSE SERVICES
// =====================================================

builder.Services.AddScoped<
    FYPTracker.Services.Forms.MeetingLogTemplateService>();

builder.Services.AddScoped<
    FYPTracker.Services.Forms.IafFormTemplateService>();

builder.Services.AddScoped<
    WeeklyMeetingSchedulerService>();

builder.Services.AddScoped<
    FYPTracker.Services.ILecturerAvailabilityService,
    FYPTracker.Services.LecturerAvailabilityService>();

builder.Services.AddScoped<
    FYPTracker.Services.Meetings.IMeetingRequirementService,
    FYPTracker.Services.Meetings.MeetingRequirementService>();

builder.Services.AddScoped<
    FYPTracker.Services.Meetings.IAcademicSessionResolver,
    FYPTracker.Services.Meetings.AcademicSessionResolver>();

builder.Services.AddScoped<
    FYPTracker.Services.Meetings.IWeeklyMeetingSchedulingService,
    FYPTracker.Services.Meetings.WeeklyMeetingSchedulingService>();

builder.Services.AddScoped<
    FYPTracker.Services.Defense.IIAFDefenseBookingService,
    FYPTracker.Services.Defense.IAFDefenseBookingService>();

builder.Services.AddScoped<
    FYPTracker.Services.Defense.IIAFAlternativeTimeRequestService,
    FYPTracker.Services.Defense.IAFAlternativeTimeRequestService>();

// =====================================================
// 12. BUILD APPLICATION
// =====================================================

var app = builder.Build();

// =====================================================
// 13. TEMPORARY ERROR LOGGING
// =====================================================

// Preserve existing temporary debugging behavior.
// Review/remove before production deployment.
app.Use(async (context, next) =>
{
    try
    {
        await next();
    }
    catch (Exception ex)
    {
        var logPath = Path.Combine(
            AppContext.BaseDirectory,
            "crash.txt");

        await File.WriteAllTextAsync(
            logPath,
            $"TIME: {DateTime.Now}\n" +
            $"URL: {context.Request.Path}" +
            $"{context.Request.QueryString}\n" +
            $"ERROR: {ex.GetType().Name}: {ex.Message}\n\n" +
            $"STACK:\n{ex.StackTrace}\n\n" +
            $"INNER: {ex.InnerException?.Message}\n" +
            $"{ex.InnerException?.StackTrace}");

        throw;
    }
});

// =====================================================
// 14. ENVIRONMENT ERROR HANDLING
// =====================================================

if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}
else
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

// =====================================================
// 15. MIDDLEWARE
// =====================================================

app.UseCookiePolicy();

app.UseHttpsRedirection();

app.UseStaticFiles();

var accessor =
    serviceProvider.GetService<IHttpContextAccessor>();

CommonHelpers.SetHttpContextAccessor(
    accessor,
    serviceProvider.GetService<IWebHostEnvironment>());

app.UseStatusCodePages(async context =>
{
    var response = context.HttpContext.Response;

    if (response.StatusCode ==
        (int)HttpStatusCode.NotFound)
    {
        response.Redirect("/Home/Error404");
    }
    else if (response.StatusCode ==
        (int)HttpStatusCode.Forbidden)
    {
        response.Redirect("/Home/AccessDenied");
    }
    else if (response.StatusCode ==
        (int)HttpStatusCode.Unauthorized)
    {
        response.Redirect("/User/login");
    }
});

app.UseRouting();

app.UseAuthentication();

app.UseAuthorization();

app.UseSession();

// =====================================================
// 16. ROUTES / SIGNALR HUB
// =====================================================

app.UseEndpoints(endpoints =>
{
    endpoints.MapControllerRoute(
        name: "Login",
        pattern: "/login",
        defaults: new
        {
            controller = "User",
            action = "Login"
        });

    endpoints.MapControllerRoute(
        name: "Homepage",
        pattern: "",
        defaults: new
        {
            controller = "Home",
            action = "Index"
        });

    endpoints.MapHub<NotificationHub>(
        "/notificationHub");
});

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

// =====================================================
// 17. START APPLICATION
// =====================================================

app.Run();
