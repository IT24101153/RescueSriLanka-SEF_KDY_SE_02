using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;

namespace RescueSriLanka.Api.Services.Email;

public interface IResourceEmailQueue
{
    bool TryQueue(EmailMessage message);
}

public sealed class ResourceEmailQueue(
    IServiceScopeFactory scopeFactory,
    ILogger<ResourceEmailQueue> logger) : BackgroundService, IResourceEmailQueue
{
    private readonly Channel<EmailMessage> _messages = Channel.CreateUnbounded<EmailMessage>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });

    public bool TryQueue(EmailMessage message) => _messages.Writer.TryWrite(message);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var message in _messages.Reader.ReadAllAsync(stoppingToken))
            {
                try
                {
                    using var scope = scopeFactory.CreateScope();
                    var sender = scope.ServiceProvider.GetRequiredService<IEmailSender>();
                    await sender.SendAsync(message, stoppingToken);
                }
                catch (Exception exception)
                {
                    logger.LogError(exception, "Failed to send queued resource email to {Email}.", message.ToAddress);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }
}