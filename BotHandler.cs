using System.Globalization;
using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
public enum WeekOption
{
    Current,
    Next
}

public enum DayOption
{
    Today,
    Tomorrow,
}

// ReSharper disable once InconsistentNaming
public record HealthCheck(string? type);

namespace ReSchedule
{
    public class BotHandler
    {
        private const string SetUpFunctionName = "setup";
        private const string UpdateFunctionName = "handleupdate";

        private readonly ILogger _logger;
        private readonly Bot _bot;
        private readonly string _accessKey;

        public BotHandler(ILogger<BotHandler> logger, Bot bot)
        {
            _logger = logger;
            _bot = bot;
            var accessKey = Environment.GetEnvironmentVariable("AccessKey",
                EnvironmentVariableTarget.Process);
            if (accessKey == null)
            {
                _logger.LogError("Access key is not set");
                throw new Exception("Access key is not set!");
            }

            _accessKey = accessKey;
        }
        
        [Function("setup")]
        public async Task<HttpResponseData> Setup(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post",Route="setup/{key}")] HttpRequestData req,
            string key)
        {
            _logger.LogInformation($"Called {nameof(Setup)}");
            var response = req.CreateResponse(HttpStatusCode.OK);
            if (key != _accessKey)
            {
                _logger.LogWarning("Keys didn't match");

                response = req.CreateResponse(HttpStatusCode.Forbidden);
                response.Headers.Add("Content-Type", "text/plain; charset=utf-8");
                await response.WriteStringAsync($"Your key {key} was not accepted");
                return response;

            }
            var ngrokUrl = Environment.GetEnvironmentVariable("NGROK");
            var isDevelopment = !string.IsNullOrEmpty(ngrokUrl);
            if (isDevelopment) System.Diagnostics.Trace.WriteLine("Using ngrok");
            var handleUpdateFunctionUrl = isDevelopment
                ? $"{ngrokUrl}/api/handleupdate/{_accessKey}"
                : req.Url.ToString().Replace(SetUpFunctionName, UpdateFunctionName,
                    ignoreCase: true, culture: CultureInfo.InvariantCulture);
            System.Diagnostics.Trace.WriteLine(handleUpdateFunctionUrl);
            _logger.LogInformation("{}",handleUpdateFunctionUrl);
            await _bot.SetWebhook(handleUpdateFunctionUrl);

            
            response.Headers.Add("Content-Type", "text/plain; charset=utf-8");
            await response.WriteStringAsync($"Your key was accepted");
            return response;
        }


        [Function(UpdateFunctionName)]
        public async Task Run([HttpTrigger(AuthorizationLevel.Anonymous, "post",Route="handleupdate/{key}")] HttpRequestData req, string key)
        {
            if (key != _accessKey)
            {
                _logger.LogInformation("Key was not accepted");
                return;
            }
            var request = await req.ReadAsStringAsync();
            if (request == null)
            {
                _logger.LogInformation("Got null request");
                return;
            }

           
            if (JsonConvert.DeserializeObject<HealthCheck>(request) is var healthCheck && healthCheck?.type!=null)
            {
                _logger.LogInformation("Received healthcheck request");
                return;
            }
            var update = JsonConvert.DeserializeObject<Update>(request);
            if (update==null)
            {
                _logger.LogInformation("Can't deserialize request");
                return;
            }
            try
            {
                var handler = update.Type switch
                {
                    UpdateType.Message => _bot.BotOnMessageReceived(update.Message!),
                    _ => Bot.UnknownUpdateHandlerAsync(update)
                };
                await handler;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception,"Error while calling handler");
            }
        }

    }
}