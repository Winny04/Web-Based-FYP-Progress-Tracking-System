using System;
using FYPTracker.Services.Others;
using MailKit.Net.Smtp;
using MailKit.Security;
using Xunit;
using Xunit.Abstractions;

namespace FYPTracker.Tests
{
    public class SmtpConnectionDiagnosticsTests
    {
        private readonly ITestOutputHelper _output;

        public SmtpConnectionDiagnosticsTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public void Probe_SmtpConnect_WithSslOnConnect_Port465()
        {
            using var smtp = new SmtpClient();
            smtp.Timeout = 10000;
            _output.WriteLine("Attempting SmtpClient.Connect('smtp.gmail.com', 465, SecureSocketOptions.SslOnConnect)...");
            smtp.Connect("smtp.gmail.com", 465, SecureSocketOptions.SslOnConnect);
            _output.WriteLine($"SUCCESS: Connected={smtp.IsConnected}, Secure={smtp.IsSecure}");
            Assert.True(smtp.IsConnected);
            smtp.Disconnect(true);
        }

        [Fact]
        public void Probe_SmtpSubscribeSingleton_Connect_Port465()
        {
            using var smtp = new SmtpSubscribeSingleton();
            smtp.Timeout = 10000;
            _output.WriteLine("Attempting SmtpSubscribeSingleton.Connect('smtp.gmail.com', 465, SecureSocketOptions.SslOnConnect)...");
            smtp.Connect("smtp.gmail.com", 465, SecureSocketOptions.SslOnConnect);
            _output.WriteLine($"SUCCESS: Connected={smtp.IsConnected}, Secure={smtp.IsSecure}");
            Assert.True(smtp.IsConnected);
            smtp.Disconnect(true);
            smtp.Dispose();
        }
    }
}
