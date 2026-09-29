using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace PhoneKey.Service
{
    public static class Program
    {
        public static void Main(string[] args)
        {
            var builder = Host.CreateDefaultBuilder(args)
                .UseWindowsService(options =>
                {
                    options.ServiceName = "PhoneKeyService";
                })
                .ConfigureLogging(logging =>
                {
                    logging.ClearProviders();
                    logging.AddConsole();
                    logging.AddEventLog(eventLogSettings =>
                    {
                        eventLogSettings.SourceName = "PhoneKeyService";
                    });
                })
                .ConfigureServices((hostContext, services) =>
                {
                    services.AddHostedService<PhoneKeyServiceWorker>();
                });

            var host = builder.Build();
            host.Run();
        }
    }
}
