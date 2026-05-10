using eForm_dotnetCore.Configuration;
using eForm_dotnetCore.Configuration.Auhtorization;
using eForm_dotnetCore.Helpers;
using eForm_dotnetCore.Models;
using eForm_dotnetCore.Services.AuditTrails;
using eForm_dotnetCore.Services.Authentication;
using eForm_dotnetCore.Services.Employees;
using eForm_dotnetCore.Services.Others;
using eForm_dotnetCore.Services.Others.Interfaces;
using eForm_dotnetCore.Services.Quartz;
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
builder.Services.AddScoped<IAuditTrailService, AuditTrailService>();
builder.Services.AddScoped<IWebHelper, WebHelper>();
builder.Services.AddScoped<IAuthenticationService, CookieAuthenticationService>();
builder.Services.AddScoped<IEmployeeService, EmployeeService>();
builder.Services.AddScoped<IEmailService, EmailService>();
builder.Services.AddTransient<IFileWriter, FileWriter>();

//register smtp
var smtpSettings = new SmtpSettings();
builder.Configuration.Bind(nameof(smtpSettings), smtpSettings);
builder.Services.AddSingleton(smtpSettings);

#endregion

string connectionString = builder.Configuration.GetConnectionString("Entities");

builder.Services.AddDbContext<EformSophicDemoContext>(options =>
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
    var _ctx = scope.ServiceProvider.GetRequiredService<EformSophicDemoContext>();
    foreach (KeyValuePair<string, (string,List<string>)> kvp in EmailTemplateConfiguration.GetEmailTemplates)
    {
        if(!_ctx.EmailTemplateConfig.Any(x => x.ConfigKey== kvp.Key))
        {
            _ctx.EmailTemplateConfig.Add(new EmailTemplateConfig
            {
                ConfigKey = kvp.Key,
                ConfigValue = kvp.Value.Item1,
                RequireParams = string.Join(",", kvp.Value.Item2)
            });

            _ctx.SaveChanges();
        }
    }
}

builder.Services.Configure<CookiePolicyOptions>(options =>
{
    options.CheckConsentNeeded = context => true;
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
    options.AddPolicy("PMOnly", policy => policy.Requirements.Add(new PMOnlyRequirement()));
    options.AddPolicy("ManagementLevelOnly", policy => policy.Requirements.Add(new ManagementLevelOnlyRequirement()));
});

var app = builder.Build();

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
        response.Redirect("/EmployeeInfo/login");

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
        defaults: new { controller = "EmployeeInfo", action = "Login" }
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
