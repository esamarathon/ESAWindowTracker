using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Debug;
using Microsoft.Extensions.Logging.Console;

namespace ESAWindowTracker
{
    public partial class App : Application
    {
        private readonly IHost host;

        new public static App Current => (App)Application.Current;

        public App()
        {
            host = new HostBuilder()
                .ConfigureAppConfiguration((context, builder) =>
                {
                    builder.SetBasePath(AppDomain.CurrentDomain.BaseDirectory);
                    builder.AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);
#if DEBUG
                    builder.AddUserSecrets<App>();
#endif
                }).ConfigureServices((context, services) =>
                {
                    ConfigureServices(context.Configuration, services);
                })
                .ConfigureLogging(logging =>
                {
                    logging.ClearProviders();
                    logging.AddConsole();
#if DEBUG
                    logging.AddDebug();
#else
                    logging.AddEventLog();
#endif
                })
               .Build();

            WriteConfig();

            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        }

        private void ConfigureServices(IConfiguration configuration, IServiceCollection services)
        {
            services.AddLogging();

            services.AddOptions();
            services.Configure<Config>(configuration);
            services.Configure<RabbitConfig>(configuration.GetSection("RabbitConfig"));

            RabbitService.Register(services);
            WindowTracker.Register(services);

            services.AddSingleton<MainWindow>();
        }

        protected override async void OnStartup(StartupEventArgs e)
        {
            await host.StartAsync();
            host.Services.GetRequiredService<MainWindow>();
            base.OnStartup(e);
        }

        protected override async void OnExit(ExitEventArgs e)
        {
            using (host)
            {
                await host.StopAsync(TimeSpan.FromSeconds(5));
            }

            base.OnExit(e);
        }

        public void WriteConfig(Config? config = null)
        {
            if (config == null) {
                using var scope = host.Services.CreateScope();
                config = scope.ServiceProvider.GetRequiredService<IOptionsSnapshot<Config>>().Value;
            }

            var jsonWriteOptions = new JsonSerializerOptions()
            {
                WriteIndented = true
            };
            var newJson = JsonSerializer.Serialize(config, jsonWriteOptions);

            var appSettingsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appsettings.json");
            File.WriteAllText(appSettingsPath, newJson, Encoding.UTF8);
        }
    }
}
