using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Task1
{
    public interface IClient
    {
        Task<IResponse> GetApplicationStatus(string id, CancellationToken cancellationToken);
    }

    public interface IResponse
    {
    }

    public record SuccessResponse(string Id, string Status) : IResponse;

    public record FailureResponse() : IResponse;

    public record RetryResponse(TimeSpan Delay) : IResponse;

    public interface IHandler
    {
        Task<IApplicationStatus> GetApplicationStatus(string id);
    }

    public class Handler : IHandler
    {
        private readonly TimeSpan _timeout;
        private readonly IClient _client1;
        private readonly IClient _client2;
        private readonly ILogger<Handler> _logger;
        private readonly IDateTimeProvider _dateTimeProvider;

        public Handler(
            IClient client1,
            IClient client2,
            ILogger<Handler> logger,
            IDateTimeProvider dateTimeProvider,
            TimeSpan timeout)
        {
            _timeout = timeout;
            _client1 = client1;
            _client2 = client2;
            _logger = logger;
            _dateTimeProvider = dateTimeProvider;
        }
        
        public async Task<IApplicationStatus> GetApplicationStatus(string id)
        {
            // P.S. в продакшен коде я бы стал реализовывать подобное через библиотеки  вроде Polly, но тут захотелось самому написать реализацию
            var cts = new CancellationTokenSource(_timeout);
            var retriesCount = 0;
            DateTime? lastRequestTime = null;
            try
            {
                while (true)
                {
                    retriesCount++;
                    lastRequestTime = _dateTimeProvider.Now;
                    var response = await GetApplicationStatus(id, cts.Token);
                    if (response is RetryResponse retryResponse)
                    {
                        _logger.LogError($"Retry response was received, retrying after {retryResponse.Delay}.");
                        await Task.Delay(retryResponse.Delay, cts.Token);
                    }
                    else
                    {
                        _logger.LogDebug($"{response} was received");
                        return response switch
                        {
                            FailureResponse => new FailureStatus(lastRequestTime, retriesCount),
                            SuccessResponse success => new SuccessStatus(success.Id, success.Status),
                            _ => throw new NotSupportedException("Response is not supported")
                        };
                    }
                }
            }
            catch (Exception exception)
            {
                _logger.LogError($"Exception occurred during get application status for application with id {id}: {exception}");
                return new FailureStatus(lastRequestTime, retriesCount);
            }
        }

        private async Task<IResponse> GetApplicationStatus(string id, CancellationToken token)
        {
            var cts = CancellationTokenSource.CreateLinkedTokenSource(token);
            var tasks = new[]
            {
                _client1.GetApplicationStatus(id, cts.Token), _client2.GetApplicationStatus(id, cts.Token),
            };
            var task = await Task.WhenAny(tasks);
            //отменяем вторую задачу т.к. ответ мы уже порлучили
            try
            {
                cts.Cancel();
            }
            catch (OperationCanceledException exception) when (exception.CancellationToken != token)
            {
            }

            return task.Result;
        }
    }

    public interface IDateTimeProvider
    {
        DateTime Now { get; }
    }

    public class DefaultDateTimeProvider : IDateTimeProvider
    {
        public DateTime Now => DateTime.Now;
    }

    public interface IApplicationStatus
    {
    }

    public record SuccessStatus(string ApplicationId, string Status) : IApplicationStatus;

    public record FailureStatus(DateTime? LastRequestTime, int RetriesCount) : IApplicationStatus;
}