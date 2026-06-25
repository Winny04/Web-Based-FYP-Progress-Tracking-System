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
using MailKit.Net.Smtp;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
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

builder.Services.AddScoped<IWebHelper, WebHelper>();
builder.Services.AddScoped<IAuthenticationService, CookieAuthenticationService>();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<IEmailService, EmailService>();
builder.Services.AddTransient<IFileWriter, FileWriter>();
builder.Services.AddHostedService<MeetingReminderService>();

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
builder.Services.AddSingleton<IHttpContextAccessor, HttpContextAccessor>();
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
    options.Cookie.HttpOnly = false;
    options.ExpireTimeSpan = TimeSpan.FromDays(1);
    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    options.LoginPath = AuthenticationDefaults.LoginPath;
    options.AccessDeniedPath = AuthenticationDefaults.AccessDeniedPath;
});

builder.Services.AddAuthorization(options =>
{

});

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

    if (response.StatusCode == (int)HttpStatusCode.Unauthorized ||
        response.StatusCode == (int)HttpStatusCode.Forbidden ||
        response.StatusCode == (int)HttpStatusCode.NotFound)
        response.Redirect("/Home/Error404");

    if (response.StatusCode == (int)HttpStatusCode.Unauthorized ||
            response.StatusCode == (int)HttpStatusCode.Forbidden)
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
});
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();



