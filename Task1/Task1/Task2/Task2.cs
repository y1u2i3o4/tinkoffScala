using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using Microsoft.Extensions.Logging;

namespace Task2
{
    interface IHandler
    {
        TimeSpan Delay { get; }

        Task PerformOperation(CancellationToken cancellationToken);
    }

    public class Handler : IHandler
    {
        private readonly IConsumer _consumer;
        private readonly IPublisher _publisher;
        private readonly ILogger<Handler> _logger;
        private readonly int _maxDegreeOfParallelism;

        public TimeSpan Delay { get; }

        public Handler(
            TimeSpan delay,
            IConsumer consumer,
            IPublisher publisher,
            ILogger<Handler> logger)
        {
            Delay = delay;
            _consumer = consumer;
            _publisher = publisher;
            _logger = logger;
            _maxDegreeOfParallelism = Environment.ProcessorCount;
        }

        public async Task PerformOperation(CancellationToken cancellationToken)
        {
            var options = new ExecutionDataflowBlockOptions
            {
                CancellationToken = cancellationToken,
                MaxDegreeOfParallelism = _maxDegreeOfParallelism
            };

            var actionBlock = new ActionBlock<Event>(async eventItem =>
            {
                foreach (var recipient in eventItem.Recipients)
                {
                    await SendToRecipient(recipient, eventItem.Payload, cancellationToken);
                }
            }, options);

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    var eventItem = await _consumer.ReadData();
                    actionBlock.Post(eventItem);
                }
                catch (OperationCanceledException)
                {
                    _logger.LogInformation("Operation was cancelled.");
                    break;
                }
                catch (Exception exception)
                {
                    _logger.LogError($"Exception occurred during processing message:{exception}");
                }
            }

            actionBlock.Complete();
            await actionBlock.Completion; 
        }

        private async Task SendToRecipient(Address recipient, Payload payload, CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var sendResult = await _publisher.SendData(recipient, payload);

                if (sendResult == SendResult.Accepted)
                {
                    _logger.LogDebug($"Data success sent to {recipient.NodeId}.");
                    break;
                }
                else if (sendResult == SendResult.Rejected)
                {
                    _logger.LogWarning($"Sending to {recipient.NodeId} was rejected, retrying after {Delay}.");
                    await Task.Delay(Delay, cancellationToken);
                }
            }
        }
    }

    public record Payload(string Origin, byte[] Data);

    public record Address(string DataCenter, string NodeId);

    public record Event(IReadOnlyCollection<Address> Recipients, Payload Payload);

    public enum SendResult
    {
        Accepted,
        Rejected
    }

    public interface IConsumer
    {
        Task<Event> ReadData();
    }

    public interface IPublisher
    {
        Task<SendResult> SendData(Address address, Payload payload);
    }
}