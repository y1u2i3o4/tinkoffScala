using Microsoft.Extensions.Logging;
using Moq;
using Task1;

namespace Tests;

public class Task1Tests
{
    private const string ApplicationId = "1";
    private readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(15);
    private ILogger<Handler> _logger;
    private Mock<IClient> _client1;
    private Mock<IClient> _client2;
    private Mock<IDateTimeProvider> _dateTimeProvider;

    [SetUp]
    public void SetUp()
    {
        var factory = LoggerFactory.Create(builder => builder.AddConsole());
        _logger = factory.CreateLogger<Handler>();
        _client1 = new Mock<IClient>();
        _client2 = new Mock<IClient>();
        _client2.Setup(c => c.GetApplicationStatus(ApplicationId, It.IsAny<CancellationToken>()))
                .Returns(async (string _, CancellationToken token) =>
                {
                    await Task.Delay(Timeout.Infinite, token).ConfigureAwait(false);
                    return new FailureResponse();
                });
        _dateTimeProvider = new Mock<IDateTimeProvider>();
    }

    [Test]
    public async Task GetApplicationStatus_SuccessResult_ShouldBeSuccessStatus()
    {
        var successResponse = new SuccessResponse(ApplicationId, "success");
        _client1.Setup(c => c.GetApplicationStatus(ApplicationId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(successResponse);
        var internalToken = CancellationToken.None;
        _client2.Setup(c => c.GetApplicationStatus(ApplicationId, It.IsAny<CancellationToken>()))
                .Returns(async (string _, CancellationToken token) =>
                {
                    await Task.Delay(Timeout.Infinite, token).ConfigureAwait(false);
                    return successResponse;
                }).Callback((string _, CancellationToken token) => internalToken = token);

        var handler = new Handler(_client1.Object, _client2.Object, _logger, _dateTimeProvider.Object, DefaultTimeout);
        var result = await handler.GetApplicationStatus(ApplicationId);
        
        Assert.That(new SuccessStatus(successResponse.Id, successResponse.Status), Is.EqualTo(result));
        //проверяем что отменили вторую таску
        Assert.That(internalToken.IsCancellationRequested);
    }
    
    [Test]
    public async Task GetApplicationStatus_TimeoutHappened_ShouldBeFailureStatus()
    {
        var date = DateTime.Now;
        _dateTimeProvider.Setup(d => d.Now).Returns(date);
        var failResponse = new FailureStatus(date, 1);
        _client1.Setup(c => c.GetApplicationStatus(ApplicationId, It.IsAny<CancellationToken>()))
                .Callback((string _, CancellationToken token) => token.ThrowIfCancellationRequested());
        
        var handler = new Handler(_client1.Object, _client2.Object, _logger, _dateTimeProvider.Object, TimeSpan.FromSeconds(0));
        var result = await handler.GetApplicationStatus(ApplicationId);
        Assert.That(result, Is.InstanceOf<FailureStatus>());
        Assert.That(result, Is.EqualTo(failResponse));
    }
    
    [Test]
    public async Task GetApplicationStatus_FailResponse_ShouldBeFailStatus()
    {
        var date = DateTime.Now;
        _dateTimeProvider.Setup(d => d.Now).Returns(date);
        var failResponse = new FailureResponse();
        _client1.Setup(c => c.GetApplicationStatus(ApplicationId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(failResponse);
        
        var handler = new Handler(_client1.Object, _client2.Object, _logger, _dateTimeProvider.Object, DefaultTimeout);
        var result = await handler.GetApplicationStatus(ApplicationId);
        Assert.That(result, Is.InstanceOf<FailureStatus>());
        Assert.That(result, Is.EqualTo(new FailureStatus(date, 1)));
    }
    
    [Test]
    public async Task GetApplicationStatus_FailWithRetry_ShouldBeFailStatusWithOneMoreRetries()
    {
        var date = DateTime.Now;
        _dateTimeProvider.Setup(d => d.Now).Returns(date);
        var retryResponse = new RetryResponse(TimeSpan.FromSeconds(0));
        var failResponse = new FailureResponse();
        _client1.SetupSequence(c => c.GetApplicationStatus(ApplicationId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(retryResponse)
                .ReturnsAsync(failResponse);
        
        var handler = new Handler(_client1.Object, _client2.Object, _logger, _dateTimeProvider.Object, DefaultTimeout);
        var result = await handler.GetApplicationStatus(ApplicationId);
        Assert.That(result, Is.InstanceOf<FailureStatus>());
        Assert.That(result, Is.EqualTo(new FailureStatus(date, 2)));
    }
    
    [Test]
    public async Task GetApplicationStatus_ClientThrowException_ShouldBeFailStatus()
    {
        var date = DateTime.Now;
        _dateTimeProvider.Setup(d => d.Now).Returns(date);
        _client1.SetupSequence(c => c.GetApplicationStatus(ApplicationId, It.IsAny<CancellationToken>()))
                .Throws(new ArgumentException());
        
        var handler = new Handler(_client1.Object, _client2.Object, _logger, _dateTimeProvider.Object, DefaultTimeout);
        var result = await handler.GetApplicationStatus(ApplicationId);
        Assert.That(result, Is.InstanceOf<FailureStatus>());
        Assert.That(result, Is.EqualTo(new FailureStatus(date, 1)));
    }
    
    
}