using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using System.Net;
using System.Net.Sockets;

namespace eForm_dotnetCore.Common;
public class ServerContext
{
    private readonly IServer _server;
    private static IHttpContextAccessor httpContextAccessor;

    public ServerContext(IServer server)
    {
        _server = server;
    }

    public string BaseUrl()
    {
        var addresses = _server.Features.Get<IServerAddressesFeature>().Addresses;
        // Now that you have the request you can select what you need from it.
        return addresses.Where(x => x.Contains("https")).FirstOrDefault();
    }

    public string GetServerAddress()
    {
        var builder = new ConfigurationBuilder().SetBasePath(Directory.GetCurrentDirectory()).AddJsonFile("appsettings.json", optional: false);
        IConfiguration config = builder.Build();
        string server = config.GetValue<string>("Server:Address");
        return server;
    }
}