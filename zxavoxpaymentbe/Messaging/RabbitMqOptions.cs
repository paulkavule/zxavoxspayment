using RabbitMQ.Client;

namespace zxavoxpaymentbe.Messaging;

/// <summary>Where the broker is, from the RABBITMQ_ variables (set in .env like the DB_ ones).</summary>
public record RabbitMqOptions(string Host, int Port, string UserName, string Password, string VirtualHost)
{
    public static RabbitMqOptions FromEnvironment() => new(
        Value("RABBITMQ_HOST", "localhost"),
        int.TryParse(Environment.GetEnvironmentVariable("RABBITMQ_PORT"), out var port) ? port : 5672,
        Value("RABBITMQ_USER", "guest"),
        Value("RABBITMQ_PASSWORD", "guest"),
        Value("RABBITMQ_VHOST", "/"));

    public ConnectionFactory Factory() => new()
    {
        HostName = Host, Port = Port, UserName = UserName, Password = Password, VirtualHost = VirtualHost,
        ClientProvidedName = "zxavoxpayments",
    };

    private static string Value(string name, string fallback) =>
        Environment.GetEnvironmentVariable(name) is { Length: > 0 } value ? value : fallback;
}
