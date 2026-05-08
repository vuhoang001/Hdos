using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;

namespace Hdos.Common.Messaging;

public sealed class RabbitMqConnection : IDisposable
{
    private readonly RabbitMqOptions _options;
    private readonly ILogger<RabbitMqConnection> _logger;
    private readonly ConnectionFactory _factory;
    private IConnection? _connection;
    private readonly object _lock = new();
    private bool _disposed;

    public RabbitMqConnection(IOptions<RabbitMqOptions> options, ILogger<RabbitMqConnection> logger)
    {
        _options = options.Value;
        _logger = logger;
        _factory = new ConnectionFactory
        {
            HostName = _options.Host,
            Port = _options.Port,
            UserName = _options.UserName,
            Password = _options.Password,
            VirtualHost = _options.VirtualHost,
            DispatchConsumersAsync = true,
            AutomaticRecoveryEnabled = true
        };
    }

    public bool IsConnected => _connection is { IsOpen: true } && !_disposed;

    public IConnection EnsureConnected()
    {
        if (IsConnected) return _connection!;

        lock (_lock)
        {
            if (IsConnected) return _connection!;

            var attempts = 0;
            while (attempts < _options.RetryCount)
            {
                try
                {
                    _connection = _factory.CreateConnection();
                    _logger.LogInformation("RabbitMQ connection established to {Host}:{Port}",
                        _options.Host, _options.Port);
                    return _connection;
                }
                catch (BrokerUnreachableException ex)
                {
                    attempts++;
                    var delay = TimeSpan.FromSeconds(Math.Pow(2, attempts));
                    _logger.LogWarning(ex,
                        "RabbitMQ connection failed (attempt {Attempt}/{Max}). Retrying in {Delay}s",
                        attempts, _options.RetryCount, delay.TotalSeconds);
                    Thread.Sleep(delay);
                }
            }

            throw new InvalidOperationException("Could not connect to RabbitMQ after retries.");
        }
    }

    public IModel CreateChannel() => EnsureConnected().CreateModel();

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _connection?.Close(); _connection?.Dispose(); }
        catch (Exception ex) { _logger.LogWarning(ex, "Error closing RabbitMQ connection."); }
    }
}
