using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Security.Authentication;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using RabbitMQ.Client;
using System.Text.Json;

namespace ESAWindowTracker
{
    public class RabbitMessage
    {
        [JsonPropertyName("event_short")]
        public string? Eventshort { get; set; }

        [JsonPropertyName("pc_id")]
        public string? PCID { get; set; }

        [JsonPropertyName("window_title")]
        public string? WindowTitle { get; set; }

        [JsonPropertyName("window_left")]
        public int WindowLeft { get; set; }
        [JsonPropertyName("window_right")]
        public int WindowRight { get; set; }

        [JsonPropertyName("window_top")]
        public int WindowTop { get; set; }
        [JsonPropertyName("window_bottom")]
        public int WindowBottom { get; set; }
    }

    public class RabbitMessageSender
    {
        public event Action<RabbitMessage>? OnRabbitMessage;

        public void PostMesage(RabbitMessage message)
        {
            OnRabbitMessage?.Invoke(message);
        }

        private string status = "";
        public string Status
        {
            get => status;
            set
            {
                status = value;
                StatusChanged?.Invoke(status);
            }
        }
        public event Action<string>? StatusChanged;
    }

    public class RabbitService : IHostedService
    {
        public static void Register(IServiceCollection services)
        {
            services.AddSingleton<RabbitMessageSender>();
            services.AddHostedService<RabbitService>();
        }

        private readonly ILogger logger;
        private readonly IOptionsMonitor<Config> options;
        private readonly RabbitMessageSender msg_sender;

        private IConnection? mqCon;
        private IModel? channel;

        public RabbitService(ILogger<RabbitService> logger, IOptionsMonitor<Config> options, RabbitMessageSender msg_sender)
        {
            this.logger = logger;
            this.options = options;
            this.msg_sender = msg_sender;
        }

        private Task CloseAsync(CancellationToken cancellationToken)
        {
            return Task.Run(() =>
            {
                msg_sender.OnRabbitMessage -= OnRabbitMessage;

                if (channel != null)
                {
                    channel.Close();
                    channel = null;
                }
                if (mqCon != null)
                {
                    mqCon.Close();
                    mqCon = null;
                }
            }, cancellationToken);
        }

        private RabbitConfig inUseOptions = new();

        private ConnectionFactory GetConnFac()
        {
            RabbitConfig opts = options.CurrentValue.RabbitConfig;
            inUseOptions = opts;

            var factory = new ConnectionFactory
            {
                HostName = opts.Host,
                VirtualHost = opts.VHost,
                Port = opts.Port,

                UserName = opts.User,
                Password = opts.Pass,

                AutomaticRecoveryEnabled = true,
                NetworkRecoveryInterval = TimeSpan.FromSeconds(10),

                DispatchConsumersAsync = true
            };

            factory.Ssl.Enabled = opts.Tls;
            factory.Ssl.Version = SslProtocols.Tls12 | SslProtocols.Tls13;
            factory.Ssl.ServerName = factory.HostName;

            return factory;
        }

        private void InitRabbitMQ()
        {
            if (channel != null)
                channel.Close();
            if (mqCon != null)
                mqCon.Close();

            channel = null;
            mqCon = null;

            mqCon = GetConnFac().CreateConnection();

            channel = mqCon.CreateModel();
            channel.BasicQos(0, 1, false);

            channel.ExchangeDeclare("cg", ExchangeType.Topic, true, true);

            logger.LogInformation("Connected to MQ service at {0}.", mqCon.Endpoint.HostName);
            msg_sender.Status = $"Connected to MQ service at {mqCon.Endpoint.HostName}.";
        }

        private async Task SetupRabbitConsumers(CancellationToken cancellationToken)
        {
            try
            {
                await Task.Run(() =>
                {
                    InitRabbitMQ();
                }, cancellationToken);

                msg_sender.OnRabbitMessage += OnRabbitMessage;

                logger.LogInformation("Rabbit up and running.");
            }
            catch (Exception e)
            {
                logger.LogWarning($"Failed establishing Rabbit connection: {e.Message}");
                msg_sender.Status = $"Failed establishing Rabbit connection: {e.Message}";
            }
        }

        private readonly SemaphoreSlim rabbitLock = new SemaphoreSlim(1);

        private async void OnRabbitMessage(RabbitMessage msg)
        {
            if (await rabbitLock.WaitAsync(10000))
            {
                try
                {
                    if (channel == null)
                        throw new Exception("No channel to send message to.");

                    var cfg = options.CurrentValue;
                    string msg_json = JsonSerializer.Serialize(msg);

                    await Task.Run(() =>
                    {
                        channel.BasicPublish(
                            "cg",
                            $"{cfg.EventShort}.{cfg.PCID}.window_info_changed",
                            null,
                            Encoding.UTF8.GetBytes(msg_json));
                    });
                }
                catch (Exception e)
                {
                    logger.LogError(e, "Failed sending rabbit message.");
                    msg_sender.Status = $"Failed sending rabbit message: {e.Message}";
                }
                finally
                {
                    rabbitLock.Release();
                }
            }
        }

        private async void OnOptionsChanged(Config opts)
        {
            if (await rabbitLock.WaitAsync(10000))
            {
                try
                {
                    if (inUseOptions == null || OptionsEqual(inUseOptions, opts.RabbitConfig))
                    {
                        logger.LogInformation("Rabbit config unchanged.");
                        return;
                    }

                    logger.LogInformation("Reloading rabbit config.");
                    await CloseAsync(CancellationToken.None);
                    await SetupRabbitConsumers(CancellationToken.None);
                }
                catch (Exception e)
                {
                    logger.LogError(e, "Failed connecting to Rabbit.");
                    msg_sender.Status = $"Failed connecting to Rabbit: {e.Message}";
                }
                finally
                {
                    rabbitLock.Release();
                }
            }
        }

        private static bool OptionsEqual(RabbitConfig a, RabbitConfig b)
        {
            return a.Host == b.Host
                && a.VHost == b.VHost
                && a.Port == b.Port
                && a.Tls == b.Tls
                && a.User == b.User
                && a.Pass == b.Pass;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            msg_sender.Status = "Starting up...";

            try
            {
                await SetupRabbitConsumers(cancellationToken);
            }
            catch (Exception e)
            {
                logger.LogError(e, "Failed connecting to Rabbit.");
            }

            optionsChangeListener = options.OnChange(opts => OnOptionsChanged(opts));
        }

        IDisposable? optionsChangeListener = null;

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            logger.LogInformation("Stopping Rabbit Listener.");
            msg_sender.Status = "Stopping...";

            if (optionsChangeListener != null)
            {
                optionsChangeListener.Dispose();
                optionsChangeListener = null;
            }

            await CloseAsync(cancellationToken);

            logger.LogInformation("Stopped Rabbit Listener.");
            msg_sender.Status = "Stopped.";
        }
    }
}
