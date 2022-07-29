using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Security.Authentication;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace ESAWindowTracker
{
    public class ResponseMsg
    {
        [JsonPropertyName("event_short")]
        public string Eventshort { get; set; } = "";

        [JsonPropertyName("pc_id")]
        public string PCID { get; set; } = "";

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        [JsonPropertyName("window_title")]
        public string? WindowTitle { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        [JsonPropertyName("window_left")]
        public int? WindowLeft { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        [JsonPropertyName("window_right")]
        public int? WindowRight { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        [JsonPropertyName("window_top")]
        public int? WindowTop { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        [JsonPropertyName("window_bottom")]
        public int? WindowBottom { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        [JsonPropertyName("error")]
        public string? Error { get; set; }
    }

    public class RequestMsg
    {
        [JsonPropertyName("method")]
        public string Method { get; set; } = "";
    }

    public class RabbitStatus
    {
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
            services.AddSingleton<RabbitStatus>();
            services.AddHostedService<RabbitService>();
        }

        private readonly ILogger logger;
        private readonly IOptionsMonitor<Config> options;
        private readonly IServiceProvider services;
        private readonly RabbitStatus rabbitStatus;

        private IConnection? mqCon;
        private IModel? channel;

        public RabbitService(ILogger<RabbitService> logger, IOptionsMonitor<Config> options, IServiceProvider services, RabbitStatus rabbitStatus)
        {
            this.logger = logger;
            this.options = options;
            this.services = services;
            this.rabbitStatus = rabbitStatus;
        }

        private Task CloseAsync(CancellationToken cancellationToken)
        {
            return Task.Run(() =>
            {
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

                DispatchConsumersAsync = false
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

            var opts = options.CurrentValue;
            rpcQueue = channel.QueueDeclare($"windowtracker-{opts.EventShort}-{opts.PCID}-rpc", false, false, true);

            logger.LogInformation("Connected to MQ service at {0}.", mqCon.Endpoint.HostName);
            rabbitStatus.Status = $"Connected to MQ service at {mqCon.Endpoint.HostName}.";
        }

        private QueueDeclareOk? rpcQueue;

        private async Task SetupRabbitConsumers(CancellationToken cancellationToken)
        {
            try
            {
                await Task.Run(() =>
                {
                    InitRabbitMQ();

                    if (channel == null)
                        throw new Exception("No channel after init!");
                    if (rpcQueue == null)
                        throw new Exception("No queues after init!");

                    var sceneChangeConsumer = new EventingBasicConsumer(channel);
                    sceneChangeConsumer.Received += (_, args) => ReceivedRPC(args, cancellationToken);
                    channel.BasicConsume(rpcQueue.QueueName, false, sceneChangeConsumer);
                }, cancellationToken);

                logger.LogInformation("Rabbit up and running.");
            }
            catch (Exception e)
            {
                logger.LogWarning($"Failed establishing Rabbit connection: {e.Message}");
                rabbitStatus.Status = $"Failed establishing Rabbit connection: {e.Message}";
            }
        }

        private void ReceivedRPC(BasicDeliverEventArgs args, CancellationToken cancellationToken)
        {
            if (channel == null)
                return;

            logger.LogInformation("Got RPC Request");

            if (!args.BasicProperties.IsReplyToPresent())
            {
                logger.LogError("No ReplyTo present");
                channel.BasicNack(args.DeliveryTag, false, false);
                return;
            }

            try
            {
                var req = Encoding.UTF8.GetString(args.Body.ToArray());
                var reqMsg = JsonSerializer.Deserialize<RequestMsg>(req);

                if (reqMsg?.Method?.ToLower() != "get_info")
                    throw new Exception($"Invalid request: {reqMsg}");

                using var scope = services.CreateScope();
                var windowTracker = scope.ServiceProvider.GetRequiredService<WindowTracker>();

                ResponseMsg? msg = windowTracker.GetCurrentInfo();
                if (msg == null)
                    throw new Exception("Failed getting Window Info.");

                var cfg = options.CurrentValue;
                string msg_json = JsonSerializer.Serialize(msg);

                IBasicProperties props = channel.CreateBasicProperties();
                props.CorrelationId = args.BasicProperties.CorrelationId;
                props.ContentType = "application/json";
                props.ContentEncoding = "UTF-8";

                channel.BasicPublish(
                    "",
                    args.BasicProperties.ReplyTo,
                    props,
                    Encoding.UTF8.GetBytes(msg_json));
                channel.BasicAck(args.DeliveryTag, false);

                logger.LogInformation("RPC Reply Success");
            }
            catch(Exception e)
            {
                logger.LogError(e, "RPC Request failed");

                var opts = options.CurrentValue;
                string msg_json = JsonSerializer.Serialize(new ResponseMsg
                {
                    Eventshort = opts.EventShort,
                    PCID = opts.PCID,

                    Error = e.Message
                });

                IBasicProperties props = channel.CreateBasicProperties();
                props.CorrelationId = args.BasicProperties.CorrelationId;
                props.ContentType = "application/json";
                props.ContentEncoding = "UTF-8";

                channel.BasicPublish(
                    "",
                    args.BasicProperties.ReplyTo,
                    props,
                    Encoding.UTF8.GetBytes(msg_json));
                channel.BasicNack(args.DeliveryTag, false, false);

                logger.LogInformation("Error response sent.");
            }
        }

        private readonly SemaphoreSlim rabbitLock = new SemaphoreSlim(1);

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
                    rabbitStatus.Status = $"Failed connecting to Rabbit: {e.Message}";
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
            rabbitStatus.Status = "Starting up...";

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
            rabbitStatus.Status = "Stopping...";

            if (optionsChangeListener != null)
            {
                optionsChangeListener.Dispose();
                optionsChangeListener = null;
            }

            await CloseAsync(cancellationToken);

            logger.LogInformation("Stopped Rabbit Listener.");
            rabbitStatus.Status = "Stopped.";
        }
    }
}
