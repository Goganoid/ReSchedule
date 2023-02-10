using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using ReSchedule.Entities;

namespace ReSchedule;

public static class CacheOptions
{
    private const int HoursGetGroupsExpiration = 12;
    private const int HoursGetScheduleExpiration = 6;

    public static MemoryCacheEntryOptions GroupsExpEntryOptions => new()
    {
        AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(HoursGetGroupsExpiration)
    };

    public static MemoryCacheEntryOptions ScheduleExpEntryOptions => new()
    {
        AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(HoursGetScheduleExpiration)
    };
}

public class ApiClient
{
    private readonly HttpClient _httpClient;
    private readonly IMemoryCache _cache;
    private readonly ILogger _logger;
    private readonly string _getGroupsUri = "schedule/groups";
    private readonly Func<string, string> _getScheduleUri = (groupId) => $"schedule/lessons?groupId={groupId}";

    public ApiClient(IHttpClientFactory httpClientFactory, IMemoryCache cache, ILogger<ApiClient> logger)
    {
        _logger = logger;
        _logger.LogInformation("Creating ApiClient instance");
        string apiUri = "https://schedule.kpi.ua/api/";
        _httpClient = httpClientFactory.CreateClient();
        if (apiUri.Last() != '/') apiUri += '/';
        _httpClient.BaseAddress = new Uri(apiUri);
        _cache = cache;
    }

    private void LogCachingProcess(string key)
    {
        var stats = _cache.GetCurrentStatistics();
        _logger.LogInformation("Caching {Key}\n\tCurrent MemoryConsumption {}\n\tEntries {}",
            key,
            stats?.CurrentEstimatedSize?.ToString() ?? "none",
            stats?.CurrentEntryCount.ToString() ?? "none");
    }

    private async Task<HttpResponseMessage> RequestGroups()
    {
        var response = await _httpClient.GetAsync(_getGroupsUri);
        if (response.IsSuccessStatusCode)
        {
            LogCachingProcess(_getGroupsUri);
            _cache.Set(_getGroupsUri, response, CacheOptions.GroupsExpEntryOptions);
        }

        return response;
    }

    private async Task<HttpResponseMessage> RequestSchedule(string groupId)
    {
        var uri = _getScheduleUri(groupId);
        var response = await _httpClient.GetAsync(uri);
        if (response.IsSuccessStatusCode)
        {
            LogCachingProcess(uri);
            _cache.Set(uri, response, CacheOptions.ScheduleExpEntryOptions);
        }

        return response;
    }

    public async Task<Response<List<Group>>> GetGroups()
    {
        var response = _cache.TryGetValue(_getGroupsUri, out HttpResponseMessage? cacheResponse) switch
        {
            true => cacheResponse!,
            false => await RequestGroups()
        };
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("Get groups request was not successfull. Status Code {}",response.StatusCode);
            return new Response<List<Group>>() {StatusCode = response.StatusCode};
        }

        var content = await response.Content.ReadAsStringAsync();
        var jGroups = JObject.Parse(content)["data"]!.Children();
        var groups = jGroups.Select(jGroup => jGroup.ToObject<Group>()!).ToList();

        return new Response<List<Group>>() {StatusCode = response.StatusCode, Data = groups};
    }

    public async Task<Response<Schedule>> GetSchedule(string groupId)
    {
        var response = _cache.TryGetValue(_getScheduleUri(groupId), out HttpResponseMessage? cacheResponse) switch
        {
            true => cacheResponse!,
            false => await RequestSchedule(groupId)
        };

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("GetSchedule request was not successful.\nStatus Code:{}",
                response.StatusCode);
            return new Response<Schedule>() {StatusCode = response.StatusCode};
        }

        var content = await response.Content.ReadAsStringAsync();
        var data = JObject.Parse(content)["data"]!;
        var firstWeek = data["scheduleFirstWeek"]!.Children();
        var secondWeek = data["scheduleSecondWeek"]!.Children();
        var schedule = new Schedule
        {
            ScheduleFirstWeek = firstWeek.Select(jDay => jDay.ToObject<WeekDay>()!).ToList(),
            ScheduleSecondWeek = secondWeek.Select(jDay => jDay.ToObject<WeekDay>()!).ToList()
        };
        return new Response<Schedule>() {StatusCode = response.StatusCode, Data = schedule};
    }

    public async Task<Response<ScheduleTime>> GetCurrentTime()
    {
        var response = await _httpClient.GetAsync($"time/current");
        if (response.IsSuccessStatusCode)
        {
            var content = await response.Content.ReadAsStringAsync();
            var data = JObject.Parse(content)["data"]!;
            var time = data.ToObject<ScheduleTime>();
            return new Response<ScheduleTime>() {StatusCode = response.StatusCode, Data = time};
        }
        _logger.LogError("Time request was not successfull. Status Code {}",response.StatusCode);
        return new Response<ScheduleTime>() {StatusCode = response.StatusCode};
    }
}