using System.Net;
using System.Net.Quic;
using System.Net.Security;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Protocol.Handler.Server;

namespace Server;

[SupportedOSPlatform("linux")]
public class Program
{
	private static ILoggerFactory loggerFactory;
	private static ILogger<Program> logger;
	private static QuicListener listener;
	private static readonly CancellationTokenSource cancellationTokenSource = new();
	private static Config config;

	private static extern int signal(int signal, Action action);

	private static async Task Main(string[] args)
	{
		PosixSignalRegistration.Create(PosixSignal.SIGINT, GratefullyShutdown);
		PosixSignalRegistration.Create(PosixSignal.SIGTERM, GratefullyShutdown);
		loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
		logger = loggerFactory.CreateLogger<Program>();

		// Read config
		try
		{
			var rawConf = await File.ReadAllTextAsync(Path.GetDirectoryName(Environment.ProcessPath) + "/config.json");
			config = JsonSerializer.Deserialize(rawConf, SourceGenerationContext.Default.Config);
			if (config == null) throw new InvalidDataException();
		}
		catch (Exception ex)
		{
			Console.WriteLine($"Failed to load server configuration: {ex.Message}");
			Environment.Exit(-1);
		}

		// First, check if QUIC is supported.
		if (!QuicListener.IsSupported)
		{
			Console.WriteLine("QUIC is not supported, check for presence of libmsquic and support of TLS 1.3.");
			return;
		}

		// Share configuration for each incoming connection.
		// This represents the minimal configuration necessary.
		var serverConnectionOptions = new QuicServerConnectionOptions
		{
			DefaultStreamErrorCode = 0x0A,
			DefaultCloseErrorCode = 0x0B,
			ServerAuthenticationOptions = new SslServerAuthenticationOptions
			{
				ApplicationProtocols = new List<SslApplicationProtocol> { SslApplicationProtocol.Http3 },
				ServerCertificate = new X509Certificate2(config.Certificate)
			}
		};

		// Initialize, configure the listener and start listening.
		listener = await QuicListener.ListenAsync(new QuicListenerOptions
		{
			ListenEndPoint = new IPEndPoint(IPAddress.Any, config.Port),
			ApplicationProtocols = new List<SslApplicationProtocol> { SslApplicationProtocol.Http3 },
			ConnectionOptionsCallback = (_, _, _) => ValueTask.FromResult(serverConnectionOptions)
		});

		logger.LogInformation($"Program is listening at {listener.LocalEndPoint}");

		while (!cancellationTokenSource.IsCancellationRequested)
			try
			{
				var connection = await listener.AcceptConnectionAsync();
				_ = Task.Run(new QuicConnectionHandler(connection, cancellationTokenSource.Token,
						loggerFactory.CreateLogger<QuicConnectionHandler>())
					.HandleConnectionAsync);
			}
			catch (Exception ex)
			{
				logger.LogWarning(ex, "Failed to handle client");
			}
	}

	private static void GratefullyShutdown(PosixSignalContext context)
	{
		logger.LogInformation($"Received Signal({context.Signal}). Gratefully shutting down...");
		var operationCancelTask = cancellationTokenSource.CancelAsync();
		operationCancelTask.Wait();
		var listenerCloseTask = listener.DisposeAsync().AsTask();
		listenerCloseTask.Wait();
		Environment.Exit(0);
	}
}