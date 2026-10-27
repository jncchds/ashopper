namespace Ashopper.Api.Auth;

public interface IAppEmailSender
{
    Task SendAsync(string to, string subject, string body, CancellationToken ct);
}

/// <summary>Dev stand-in: writes emails to the log. Swap for Azure Communication Services Email in production.</summary>
public sealed class LoggingEmailSender(ILogger<LoggingEmailSender> logger) : IAppEmailSender
{
    public Task SendAsync(string to, string subject, string body, CancellationToken ct)
    {
        logger.LogWarning("EMAIL to {To}: {Subject}\n{Body}", to, subject, body);
        return Task.CompletedTask;
    }
}
