using Microsoft.Extensions.Logging;
using Moq;
using Task2;

namespace Tests.Task2
{
    using BenchmarkDotNet.Attributes;
    using System;
    using System.Threading;
    using System.Threading.Tasks;

    public class Benchmark
    {
        private Handler _handler;
        private CancellationTokenSource _cancellationTokenSource;
        private ILogger<Handler> _logger;

        [GlobalSetup]
        public void Setup()
        {
            var factory = LoggerFactory.Create(builder => builder.AddConsole());
            _logger = factory.CreateLogger<Handler>();
            var fakeEvent = new Event(new List<Address>
            {
                new("dc1", "n1"),
                new("dc2", "n2"),
                new("dc3", "n3"),
            }, new Payload("test1", new byte[]
            {
                1, 2, 3
            }));
            var publisherMock = new Mock<IPublisher>();
            publisherMock.SetupSequence(p => p.SendData(It.IsAny<Address>(), It.IsAny<Payload>()))
                         .ReturnsAsync(SendResult.Accepted)
                         .ReturnsAsync(SendResult.Rejected);
            var consumerMock = new Mock<IConsumer>();
            consumerMock.Setup(c => c.ReadData()).ReturnsAsync(fakeEvent);
            _handler = new Handler(TimeSpan.FromSeconds(1), consumerMock.Object, publisherMock.Object, _logger);

            _cancellationTokenSource = new CancellationTokenSource();
        }

        [Benchmark]
        public async Task PerformOperationBenchmark()
        {
            _cancellationTokenSource.CancelAfter(TimeSpan.FromSeconds(10));
            await _handler.PerformOperation(_cancellationTokenSource.Token);
        }
    }
}