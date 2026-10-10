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
using MailKit.Net.Smtp;
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


builder.Services.AddRazorPages();
// Add services to the container.
builder.Services.AddControllersWithViews()
    .AddNewtonsoftJson(options =>
    options.SerializerSettings.ReferenceLoopHandling = ReferenceLoopHandling.Ignore
);
builder.Configuration.SetBasePath(Directory.GetCurrentDirectory()).AddJsonFile("appsettings.json", optional: false);

builder.Services.AddHttpContextAccessor();

#region register services

builder.Services.AddSingleton<IHttpContextAccessor, HttpContextAccessor>();
builder.Services.AddHttpClient();

builder.Services.AddScoped<IWebHelper, WebHelper>();
builder.Services.AddScoped<IAuthenticationService, CookieAuthenticationService>();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<IEmailService, EmailService>();
builder.Services.AddTransient<IFileWriter, FileWriter>();
builder.Services.AddHostedService<MeetingReminderService>();
// Runs daily: auto-rejects supervisor requests with no response beyond the configured timeout
builder.Services.AddHostedService<SupervisorRequestTimeoutService>();

//register smtp
var smtpSettings = new SmtpSettings();
builder.Configuration.Bind(nameof(smtpSettings), smtpSettings);
builder.Services.AddSingleton(smtpSettings);

#endregion

string connectionString = builder.Configuration.GetConnectionString("Entities");

builder.Services.AddDbContext<FYPTrackerContext>(options =>
                options.UseSqlServer(connectionString, options =>
                {
                    options.CommandTimeout(80);
                }));

builder.Services.AddControllers().AddJsonOptions(options => { options.JsonSerializerOptions.PropertyNamingPolicy = null; });
builder.Services.AddSession();
builder.Services.AddSignalR();
builder.Services.AddTransient<IAuthorizationHandler, UserTypeHandler>();
builder.Services.AddControllersWithViews().AddRazorRuntimeCompilation();

using (var scope = builder.Services.BuildServiceProvider().CreateScope())
{
    var _ctx = scope.ServiceProvider.GetRequiredService<FYPTrackerContext>();
    // Context initialization logic here if needed...
}

builder.Services.Configure<CookiePolicyOptions>(options =>
{
    options.CheckConsentNeeded = context => false;
    options.MinimumSameSitePolicy = SameSiteMode.None;
});

builder.Services.AddAuthentication(AuthenticationDefaults.AuthenticationScheme)
.AddCookie(AuthenticationDefaults.AuthenticationScheme, options =>
{
    options.Cookie.Name = $"{CookieDefaults.Prefix}{CookieDefaults.AuthenticationCookie}";
    options.Cookie.HttpOnly = true;
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    options.LoginPath = AuthenticationDefaults.LoginPath;
    options.AccessDeniedPath = AuthenticationDefaults.AccessDeniedPath;
});

builder.Services.AddAuthorization(options =>
{

});

builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 100L * 1024 * 1024;
});

builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = 100L * 1024 * 1024;
});

builder.Services.Configure<IISServerOptions>(options =>
{
    options.MaxRequestBodySize = 100L * 1024 * 1024;
});

builder.Services.AddScoped<
    FYPTracker.Services.Forms.MeetingLogTemplateService>();

builder.Services.AddScoped<
    FYPTracker.Services.Forms.IafFormTemplateService>();

builder.Services.AddScoped<WeeklyMeetingSchedulerService>();
builder.Services.AddScoped<FYPTracker.Services.Meetings.IMeetingRequirementService, FYPTracker.Services.Meetings.MeetingRequirementService>();
builder.Services.AddScoped<FYPTracker.Services.Meetings.IAcademicSessionResolver, FYPTracker.Services.Meetings.AcademicSessionResolver>();
builder.Services.AddScoped<FYPTracker.Services.Meetings.IWeeklyMeetingSchedulingService, FYPTracker.Services.Meetings.WeeklyMeetingSchedulingService>();
var app = builder.Build();

// === TEMP DEBUG: Log all unhandled exceptions to a file ===
app.Use(async (context, next) =>
{
    try { await next(); }
    catch (Exception ex)
    {
        var logPath = Path.Combine(AppContext.BaseDirectory, "crash.txt");
        await File.WriteAllTextAsync(logPath,
            $"TIME: {DateTime.Now}\nURL: {context.Request.Path}{context.Request.QueryString}\nERROR: {ex.GetType().Name}: {ex.Message}\n\nSTACK:\n{ex.StackTrace}\n\nINNER: {ex.InnerException?.Message}\n{ex.InnerException?.StackTrace}");
        throw;
    }
});
// === END TEMP DEBUG ===

app.UseDeveloperExceptionPage();
app.UseCookiePolicy();

app.UseHttpsRedirection();
app.UseStaticFiles();

var serviceProvider = builder.Services.BuildServiceProvider();

var accessor = serviceProvider.GetService<IHttpContextAccessor>();
CommonHelpers.SetHttpContextAccessor(accessor,serviceProvider.GetService<IWebHostEnvironment>());

//app.UseMiddleware<AuthenticationMiddleware>();
app.UseStatusCodePages(async context => {
    var response = context.HttpContext.Response;

    if (response.StatusCode == (int)HttpStatusCode.NotFound)
        response.Redirect("/Home/Error404");
    else if (response.StatusCode == (int)HttpStatusCode.Forbidden)
        response.Redirect("/Home/AccessDenied");
    else if (response.StatusCode == (int)HttpStatusCode.Unauthorized)
        response.Redirect("/User/login");

});
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.UseSession();
//app.UseMiddleware<SmtpSubscribeSingleton>();
app.UseEndpoints(builder =>
{
    builder.MapControllerRoute(
        name: "Login",
        pattern: "/login",
        defaults: new { controller = "User", action = "Login" }
        );
    builder.MapControllerRoute(
        name: "Homepage",
        pattern: "",
        defaults: new { controller = "Home", action = "Index" }
        );
    builder.MapHub<NotificationHub>("/notificationHub");
});
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();



