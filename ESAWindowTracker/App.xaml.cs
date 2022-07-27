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

namespace ESAWindowTracker
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        public IServiceProvider ServiceProvider { get; set; }
        public IConfiguration Configuration { get; set; }

        public App()
        {
            var builder = new ConfigurationBuilder()
                .SetBasePath(AppDomain.CurrentDomain.BaseDirectory)
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);

#if DEBUG
            builder.AddUserSecrets<App>();
#endif

            Configuration = builder.Build();
            WriteConfig();

            var serviceCollection = new ServiceCollection();
            ConfigureServices(serviceCollection);

            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            ServiceProvider = serviceCollection.BuildServiceProvider();
        }

        private void ConfigureServices(ServiceCollection services)
        {
            services.AddTransient<MainWindow>();
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            ServiceProvider.GetRequiredService<MainWindow>();
        }

        public void WriteConfig(Config? config = null)
        {
            if (config == null)
                config = Configuration.Get<Config>() ?? new Config();

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
