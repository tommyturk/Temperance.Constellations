using Microsoft.Extensions.Logging;
using Temperance.Services.Services.Interfaces;

namespace Temperance.Services.Services.Implementations
{
    public class HangfireTestService : IHangfireTestService
    {
        private readonly ILogger<HangfireTestService> _logger;
        public HangfireTestService(ILogger<HangfireTestService> logger) { _logger = logger; }

        public void PrintMessage(string message)
        {
            // Log at a high level and write directly to the console
            // to ensure the message is seen.
            _logger.LogWarning("********** HANGFIRE MINIMAL JOB EXECUTED: {Message} **********", message);
            Console.WriteLine($"********** HANGFIRE MINIMAL JOB EXECUTED: {message} **********");
        }
    }
}
