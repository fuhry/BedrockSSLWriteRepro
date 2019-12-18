using System;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Bedrock.Framework;
using Bedrock.Framework.Middleware.Tls;
using Microsoft.AspNetCore.Connections;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ServerApplication
{
    public partial class Program
    {
        public static async Task Main(string[] args)
        {
            // Manual wire up of the server
            var services = new ServiceCollection();
            services.AddLogging(builder =>
            {
                builder.SetMinimumLevel(LogLevel.Debug);
                builder.AddConsole();
            });

            var serviceProvider = services.BuildServiceProvider();

            var server = new ServerBuilder(serviceProvider)
                        .UseSockets(sockets =>
                        {
                            sockets.Listen(IPAddress.Loopback, 5004,
                                builder => builder.UseServerTls(options =>
                                {
                                    options.LocalCertificate = new X509Certificate2("testcert.pfx", "testcert");
                                    options.RemoteCertificateMode = RemoteCertificateMode.NoCertificate;
                                })
                                .UseConnectionHandler<EchoServerApplication>());
                        })
                        .Build();

            var logger = serviceProvider.GetRequiredService<ILoggerFactory>().CreateLogger<Program>();

            await server.StartAsync();

            foreach (var ep in server.EndPoints)
            {
                logger.LogInformation("Listening on {EndPoint}", ep);
            }

            var tcs = new TaskCompletionSource<object>();
            Console.CancelKeyPress += (sender, e) => tcs.TrySetResult(null);
            await tcs.Task;

            await server.StopAsync();
        }
    }
}
