using System;
using System.IO;
using System.Reflection;
using FYPTracker.Configuration;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Xunit;
using Xunit.Abstractions;

namespace FYPTracker.Tests
{
    public class SmtpConfigurationBindingTests
    {
        private readonly ITestOutputHelper _output;

        public SmtpConfigurationBindingTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public void DevelopmentConfiguration_LoadsSmtpSettings_WithUserSecrets()
        {
            var basePath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "FYPTracker"));
            
            var builder = new ConfigurationBuilder()
                .SetBasePath(basePath)
                .AddJsonFile("appsettings.Development.json", optional: false)
                .AddUserSecrets("22e147ba-d142-460b-bc9e-bbf23e98e5ac")
                .AddEnvironmentVariables();

            var configuration = builder.Build();

            var smtpSettings = new SmtpSettings();
            configuration.Bind(nameof(SmtpSettings), smtpSettings);

            _output.WriteLine($"SmtpHost: '{smtpSettings.SmtpHost}'");
            _output.WriteLine($"SmtpPort: {smtpSettings.SmtpPort}");
            _output.WriteLine($"SmtpUser: '{smtpSettings.SmtpUser}'");
            _output.WriteLine($"EmailFrom: '{smtpSettings.EmailFrom}'");
            _output.WriteLine($"Offline: {smtpSettings.Offline}");
            _output.WriteLine($"DevRedirectEmail: '{smtpSettings.DevRedirectEmail}'");
            _output.WriteLine($"SmtpPass is null or empty: {string.IsNullOrEmpty(smtpSettings.SmtpPass)}");
            _output.WriteLine($"SmtpPass length: {smtpSettings.SmtpPass?.Length ?? 0}");

            Assert.Equal("smtp.gmail.com", smtpSettings.SmtpHost);
            Assert.Equal(465, smtpSettings.SmtpPort);
            Assert.False(string.IsNullOrWhiteSpace(smtpSettings.SmtpUser));
            Assert.False(string.IsNullOrWhiteSpace(smtpSettings.EmailFrom));
            Assert.False(smtpSettings.Offline);
            Assert.Equal("ynboon04@gmail.com", smtpSettings.DevRedirectEmail);
            Assert.False(string.IsNullOrWhiteSpace(smtpSettings.SmtpPass));
        }
    }
}
