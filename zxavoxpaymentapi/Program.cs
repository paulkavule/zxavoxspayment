using FastEndpoints;
using FastEndpoints.OpenApi;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi;
using Scalar.AspNetCore;
using zxavoxpaymentapi.Security;
using zxavoxpaymentapi.Workers;
using zxavoxpaymentbe;
using zxavoxpaymentbe.Helpers;
using zxavoxpaymentbe.Messaging;
using zxavoxpaymentbe.Providers;
using zxavoxpaymentbe.Security;
using zxavoxpaymentbe.Services;

// Before CreateBuilder, so the variables reach builder.Configuration.
ServiceEnvironment.Load(".env");

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddDbContext<PaymentsDbContext>(o => o.UseNpgsql(ServiceEnvironment.DatabaseConnection()));
builder.Services.AddSingleton(new SecretCipher(Environment.GetEnvironmentVariable(SecretCipher.KeyVariable)));
builder.Services.AddScoped<ProviderAccountService>();
builder.Services.AddSingleton(RabbitMqOptions.FromEnvironment());
builder.Services.AddSingleton<IMessagePublisher, RabbitMqPublisher>();
builder.Services.AddScoped<IOutbox, Outbox>();
builder.Services.AddScoped<IInbox, Inbox>();
builder.Services.AddScoped<OutboxDispatcher>();
builder.Services.AddScoped<DeadLetterService>();
builder.Services.AddHostedService<OutboxPublisherWorker>();
builder.Services.AddMemoryCache();
builder.Services.AddHttpClient<ProviderHttp>(c => c.Timeout = TimeSpan.FromSeconds(30));
builder.Services.AddScoped<IPaymentProvider, MtnProvider>();
builder.Services.AddScoped<IPaymentProvider, AirtelProvider>();
builder.Services.AddScoped<IPaymentProvider, UtilityProvider>();
builder.Services.AddScoped<PaymentProcessor>();
builder.Services.AddScoped<PaymentRequestHandler>();
builder.Services.AddScoped<BeneficiaryService>();
builder.Services.AddSingleton(PaymentRequestHandler.Definition);
builder.Services.AddHostedService<QueueConsumerHost<PaymentRequestHandler>>();
builder.Services.AddHostedService<PaymentWorker>();
if (int.TryParse(Environment.GetEnvironmentVariable("PAY_STATUS_CUTOFF_HOURS"), out var cutoffHours) && cutoffHours > 0)
    PaymentProcessor.Cutoff = TimeSpan.FromHours(cutoffHours);
builder.Services.AddAuthentication(ApiKeyAuthentication.SchemeName)
    .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthentication>(ApiKeyAuthentication.SchemeName, null);
builder.Services.AddAuthorization();
builder.Services.AddFastEndpoints().OpenApiDocument(o =>
{
    o.Title = "Zxavox Payments API";
    o.Version = "v1";
    o.EnableJWTBearerAuth = false;
    o.AddAuth(ApiKeyAuthentication.SchemeName, new() { Name = ApiKeys.Header, In = ParameterLocation.Header, Type = SecuritySchemeType.ApiKey });
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
    await ApiKeys.Bootstrap(scope.ServiceProvider.GetRequiredService<PaymentsDbContext>(),
        Environment.GetEnvironmentVariable("PAY_BOOTSTRAP_CLIENT"), Environment.GetEnvironmentVariable("PAY_BOOTSTRAP_API_KEY"));

app.Use(async (context, next) =>
{
    var correlation = context.Request.Headers["X-Correlation-Id"].FirstOrDefault() is { Length: > 0 and <= 64 } given ? given : Guid.NewGuid().ToString();
    context.Items["CorrelationId"] = correlation;
    context.Response.Headers["X-Correlation-Id"] = correlation;
    using (app.Logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = correlation }))
        await next();
});
app.UseAuthentication();
app.UseAuthorization();
app.UseFastEndpoints();
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}
app.Run();

public partial class Program;
