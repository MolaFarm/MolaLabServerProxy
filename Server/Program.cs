using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Protocal.Handler.Server;

namespace Server;

internal class Program
{
    private static ILoggerFactory loggerFactory;
    private static ILogger<Program> logger;
    private static Config config;
    private static SocksClientHandler socksClientHandler;

    private static async Task Main(string[] args)
    {
        loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        logger = loggerFactory.CreateLogger<Program>();

        // Read config
        try
        {
            var rawConf = File.ReadAllText(Path.GetDirectoryName(Environment.ProcessPath) + "/config.json");
            config = JsonSerializer.Deserialize(rawConf, SourceGenerationContext.Default.Config);
            if (config == null) throw new InvalidDataException();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load server configuration.");
            Environment.Exit(-1);
        }

        socksClientHandler = new SocksClientHandler(new X509Certificate2(config.Certificate),
            loggerFactory.CreateLogger<SocksClientHandler>());
        var listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        listener.Bind(new IPEndPoint(IPAddress.Any, 1080));
        listener.Listen(10);
        logger.LogInformation($"Server is listening at {listener.LocalEndPoint as IPEndPoint}");

        while (true)
            try
            {
                var client = await listener.AcceptAsync();
                _ = Task.Run(() => socksClientHandler.HandleClientAsync(client));
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to handle client");
            }
    }
}