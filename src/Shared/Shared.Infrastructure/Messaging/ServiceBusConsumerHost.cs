using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Shared.Infrastructure.Messaging;

/// <summary>
/// Receives from every queue/subscription with a registered handler and maps the dispatch result to Service Bus:
/// completed/duplicate → complete, retry → abandon (Service Bus dead-letters after its max delivery count),
/// dead letter → dead-letter with a content-free reason code.
/// </summary>
public sealed partial class ServiceBusConsumerHost(
    ServiceBusClient client,
    IEnumerable<MessageHandlerRegistry> registries,
    IEnumerable<MessageDispatcher> dispatchers,
    MessagingOptions options,
    ILogger<ServiceBusConsumerHost> logger) : IHostedService, IAsyncDisposable
{
    private readonly List<ServiceBusProcessor> _processors = [];

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var registry = registries.FirstOrDefault();
        var dispatcher = dispatchers.FirstOrDefault();
        if (registry is null || dispatcher is null)
        {
            return;
        }

        var processorOptions = new ServiceBusProcessorOptions
        {
            AutoCompleteMessages = false,
            MaxConcurrentCalls = options.MaxConcurrentCalls,
        };

        foreach (var source in registry.Registrations.Select(r => r.Source).Distinct())
        {
            var processor = source.SubscriptionName is null
                ? client.CreateProcessor(source.EntityPath, processorOptions)
                : client.CreateProcessor(source.EntityPath, source.SubscriptionName, processorOptions);
            processor.ProcessMessageAsync += args => ProcessAsync(dispatcher, args);
            processor.ProcessErrorAsync += args =>
            {
                LogReceiveError(source.ToString(), args.ErrorSource.ToString(), args.Exception.GetType().Name);
                return Task.CompletedTask;
            };
            _processors.Add(processor);
            await processor.StartProcessingAsync(cancellationToken);
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        foreach (var processor in _processors)
        {
            await processor.StopProcessingAsync(cancellationToken);
        }
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var processor in _processors)
        {
            await processor.DisposeAsync();
        }
    }

    private static async Task ProcessAsync(MessageDispatcher dispatcher, ProcessMessageEventArgs args)
    {
        var message = args.Message;
        var result = await dispatcher.DispatchAsync(
            new InboundMessage(message.MessageId, message.Subject, message.Body.ToString(), message.DeliveryCount),
            args.CancellationToken);

        switch (result.Outcome)
        {
            case DispatchOutcome.Completed or DispatchOutcome.Duplicate:
                await args.CompleteMessageAsync(message, args.CancellationToken);
                break;
            case DispatchOutcome.DeadLetter:
                await args.DeadLetterMessageAsync(message, result.Reason ?? "Unknown", "See service logs (no content).", args.CancellationToken);
                break;
            default:
                await args.AbandonMessageAsync(message, cancellationToken: args.CancellationToken);
                break;
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Receiving from {Source} failed in {ErrorSource}: {ExceptionType}")]
    private partial void LogReceiveError(string source, string errorSource, string exceptionType);
}
