using FastEndpoints;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using zxavoxpaymentapi.Security;
using zxavoxpaymentbe;
using zxavoxpaymentbe.Helpers;
using zxavoxpaymentbe.Security;
using zxavoxpaymentbe.Services;

// Before CreateBuilder, so the variables reach builder.Configuration.
ServiceEnvironment.Load(".env");

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddDbContext<PaymentsDbContext>(o => o.UseNpgsql(ServiceEnvironment.DatabaseConnection()));
builder.Services.AddSingleton(new SecretCipher(Environment.GetEnvironmentVariable(SecretCipher.KeyVariable)));
builder.Services.AddScoped<ProviderAccountService>();
builder.Services.AddAuthentication(ApiKeyAuthentication.SchemeName)
    .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthentication>(ApiKeyAuthentication.SchemeName, null);
builder.Services.AddAuthorization();
builder.Services.AddFastEndpoints();

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
app.Run();

public partial class Program;
