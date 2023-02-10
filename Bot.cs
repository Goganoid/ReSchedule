using System.Net;
using Microsoft.Extensions.Logging;
using ReSchedule.Entities;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace ReSchedule;

public class Bot
{
    private readonly ILogger _logger;
    private readonly ApiClient _apiClient;
    private readonly ChatStorageClient _storageClient;

    private readonly TimeZoneInfo _kyivTimezone;
    private readonly TelegramBotClient _botClient;


    public Bot(ILogger<Bot> logger, ApiClient apiClient, ChatStorageClient chatStorageClient)
    {
        _logger = logger;
        _logger.LogInformation("Creating bot instance");
        try
        {
            _kyivTimezone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Kyiv");
        }
        catch (Exception e)
        {
            _logger.LogError("Caught exception during timezone configuration. Exception:{Exception}", e.Message);
            _logger.LogInformation("Didn't find timezone 'Europe/Kyiv'. Trying 'Europe/Kiev'");
            _kyivTimezone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Kiev");
        }

        var token = Environment.GetEnvironmentVariable("TelegramBotToken",
            EnvironmentVariableTarget.Process);
        if (token == null)
        {
            _logger.LogError("Telegram bot token is not set");
            throw new Exception("Token is not set!");
        }

        _botClient = new TelegramBotClient(token);
        _apiClient = apiClient;
        _storageClient = chatStorageClient;
        _storageClient.SetupTable();
    }

    public async Task BotOnMessageReceived(Message message)
    {
        System.Diagnostics.Trace.WriteLine("Message received");
        if (message.Type != MessageType.Text)
        {
            _logger.LogInformation("Received wrong message with type {MessageType}, instead of {Text}",
                message.Type, MessageType.Text);
            return;
        }

        if (string.IsNullOrEmpty(message.Text))
        {
            _logger.LogInformation("Received null or empty message");
            return;
        }

        var command = message.Text!.Split(' ')[0].Split('@')[0];

        try
        {
            var action = command switch
            {
                "/setgroup" => SetGroup(message),
                "/toggleweek" => ToggleWeek(message),
                "/schedule" => GetSchedule(message),
                "/week" => WeekSchedule(message, WeekOption.Current),
                "/nextweek" => WeekSchedule(message, WeekOption.Next),
                "/today" => DaySchedule(message, DayOption.Today),
                "/tomorrow" => DaySchedule(message, DayOption.Tomorrow),
                "/left" => TimeLeft(message),
                "/help" or "/start" => Usage(message),
                _ => Task.CompletedTask
            };
            await action;
        }
        catch (Exception ex)
        {
            _logger.LogError("Caught error while executing bot command\n{}", ex.Message);
        }
    }

    public async Task SetWebhook(string uri)
    {
        await _botClient.SetWebhookAsync(uri);
    }

    private async Task<Message> ToggleWeek(Message message)
    {
        var chat = await _storageClient.GetChatEntityAsync(message.Chat.Id);
        if (chat == null)
            return await GroupNotSetMessage(message);
        chat.WeekToggle = !chat.WeekToggle;
        await _storageClient.SetChatEntityAsync(chat);
        return await _botClient.SendTextMessageAsync(message.Chat.Id, text: "Порядок тижнів оновлено");
    }

    private async Task<Message> TimeLeft(Message message)
    {
        _logger.LogInformation("Called TimeLeft");
        var chat = await _storageClient.GetChatEntityAsync(message.Chat.Id);
        if (chat == null)
            return await GroupNotSetMessage(message);

        var (schedule, time, errorMessage) = await GetScheduleAndTime(message, chat.GroupId);
        if (errorMessage != null) return await errorMessage;

        var week = SelectWeek(schedule!, time!.CurrentWeek, chat.WeekToggle, WeekOption.Current);
        var weekDay = time.CurrentDay - 1;

        if (week.Count <= weekDay)
        {
            _logger.LogInformation("Week day is {}, less than {} days in week", weekDay, week.Count);
            return await _botClient.SendTextMessageAsync(message.Chat.Id,
                text: "Не можу порахувати час. Зараз точно пара?");
        }

        var userTime = TimeZoneInfo.ConvertTime(message.Date, _kyivTimezone).TimeOfDay;
        TimeSpan? prevPairEnd = null;
        foreach (var pair in week[weekDay].Pairs.OrderBy(p => Helpers.ParseTime(p.Time)))
        {
            var pairDateTime = Helpers.ParseTime(pair.Time);
            var pairStart = pairDateTime.TimeOfDay;
            var pairEnd = pairDateTime.AddMinutes(95).TimeOfDay;
            if (userTime > pairStart && userTime < pairEnd)
            {
                var timeDiff = pairEnd - userTime;
                return await _botClient.SendTextMessageAsync(message.Chat.Id,
                    text: $"До кінця пари залишилося {Math.Round(timeDiff.TotalMinutes)} хв");
            }

            if (userTime > prevPairEnd && userTime < pairStart)
            {
                var timeDiff = pairStart - userTime;
                return await _botClient.SendTextMessageAsync(message.Chat.Id,
                    text: $"До кінця перерви залишилося {Math.Round(timeDiff.TotalMinutes)} хв");
            }

            prevPairEnd = pairEnd;
        }

        return await _botClient.SendTextMessageAsync(message.Chat.Id,
            text: "Не можу порахувати час. Зараз точно пара?");
    }

    private async Task<Message> DaySchedule(Message message, DayOption dayOption)
    {
        var chat = await _storageClient.GetChatEntityAsync(message.Chat.Id);
        if (chat == null)
            return await GroupNotSetMessage(message);
        var (schedule, time, errorMessage) = await GetScheduleAndTime(message, chat.GroupId);
        if (errorMessage != null) return await errorMessage;
        var week = SelectWeek(schedule!, time!.CurrentWeek, chat.WeekToggle, WeekOption.Current);
        var dayIndex = dayOption switch
        {
            DayOption.Today => time.CurrentDay - 1,
            DayOption.Tomorrow => time.CurrentDay,
            _ => throw new ArgumentOutOfRangeException(nameof(dayOption), dayOption, null)
        };
        switch (time.CurrentDay)
        {
            // Saturday
            case 6 when dayOption == DayOption.Tomorrow:
                return await _botClient.SendTextMessageAsync(message.Chat.Id, text: "У неділю тільки чіл");
            // Sunday 
            case 7 when dayOption == DayOption.Tomorrow:
                dayIndex = 0;
                break;
            // ReSharper disable once ConditionIsAlwaysTrueOrFalse
            // Sunday
            case 7 when dayOption == DayOption.Today:
                return await _botClient.SendTextMessageAsync(message.Chat.Id, text: "У неділю тільки чіл");
        }
        if (dayIndex >= week.Count)
        {
            _logger.LogError("The dayIndex is out of range. DayOption={},Time.CurrentDay={},index={},week.Count={}",
                dayOption, time.CurrentDay, dayIndex, week.Count);
            return await _botClient.SendTextMessageAsync(message.Chat.Id, text: "Не можу отримати цей день");
        }
        if (week[dayIndex].Pairs.Count == 0)
            return await _botClient.SendTextMessageAsync(message.Chat.Id, text: "Пар у цей день нема");
        return await _botClient.SendTextMessageAsync(message.Chat.Id, text: $"{week[dayIndex]}",
            parseMode: ParseMode.Html);
    }

    private bool CheckScheduleAndTime(Message message, Response<Schedule>? scheduleResponse,
        Response<ScheduleTime>? timeResponse, out Task<Message>? errorMessage)
    {
        if (scheduleResponse is {Data: null})
        {
            errorMessage = ApiErrorMessage(message, scheduleResponse.StatusCode);
            return false;
        }

        if (timeResponse is {Data: null})
        {
            errorMessage = ApiErrorMessage(message, timeResponse.StatusCode);
            return false;
        }

        errorMessage = null;
        return true;
    }

    private async Task<(Schedule?, ScheduleTime?, Task<Message>?)> GetScheduleAndTime(
        Message message,
        string groupId,
        bool getTime = true,
        bool getSchedule = true)
    {
        var scheduleResponse = getSchedule ? await _apiClient.GetSchedule(groupId) : null;
        var timeResponse = getTime ? await _apiClient.GetCurrentTime() : null;
        if (!CheckScheduleAndTime(message, scheduleResponse, timeResponse, out var error))
        {
            return (null, null, error!);
        }

        return (scheduleResponse?.Data, timeResponse?.Data, null);
    }

    private static List<WeekDay> SelectWeek(Schedule schedule, int currentWeek, bool weekToggle,
        WeekOption weekOption)
    {
        var weekSelector = ((currentWeek == 1 ^ weekToggle) ^ weekOption != WeekOption.Current);
        return weekSelector
            ? schedule.ScheduleFirstWeek
            : schedule.ScheduleSecondWeek;
    }

    private async Task<Message> WeekSchedule(Message message, WeekOption weekOption)
    {
        var chat = await _storageClient.GetChatEntityAsync(message.Chat.Id);
        if (chat == null)
            return await GroupNotSetMessage(message);
        var (schedule, time, errorMessage) = await GetScheduleAndTime(message, chat.GroupId);
        if (errorMessage != null) return await errorMessage;
        var weekSchedule = SelectWeek(schedule!, time!.CurrentWeek, chat.WeekToggle, weekOption);
        return await _botClient.SendTextMessageAsync(message.Chat.Id, text: $"{Helpers.WeekToString(weekSchedule)}",
            parseMode: ParseMode.Html);
    }

    private async Task<Message> GetSchedule(Message message)
    {
        var chat = await _storageClient.GetChatEntityAsync(message.Chat.Id);
        if (chat == null)
            return await _botClient.SendTextMessageAsync(message.Chat.Id, text: "Група не встановлена");
        var (schedule, _, errorMessage) = await GetScheduleAndTime(message, chat.GroupId, getTime: false);
        if (errorMessage != null) return await errorMessage;
        return await _botClient.SendTextMessageAsync(message.Chat.Id, text: $"{schedule}",
            parseMode: ParseMode.Html);
    }

    private async Task<Message> SetGroup(Message message)
    {
        var (_, args) = Helpers.ParseCommand(message.Text!);
        if (args.Count == 0)
            return await _botClient.SendTextMessageAsync(message.Chat.Id, text: "Вкажіть групу, будь ласка");
        var response = await _apiClient.GetGroups();
        if (response.Data == null)
            return await _botClient.SendTextMessageAsync(message.Chat.Id,
                text: $"Помилка при запиті до API. Код:{response.StatusCode}");


        var groups = response.Data!;
        var groupName = args[0];
        var group = groups.Find(g => string.Equals(g.Name, groupName, StringComparison.OrdinalIgnoreCase));

        if (group == null)
            return await _botClient.SendTextMessageAsync(message.Chat.Id,
                text: $"Група {groupName} не була знайдена");

        var chatEntity = new ChatEntity(message.Chat.Id, group.Id);
        await _storageClient.SetChatEntityAsync(chatEntity);
        return await _botClient.SendTextMessageAsync(chatId: message.Chat.Id,
            text: $"Група встановлена",
            replyMarkup: new ReplyKeyboardRemove());
    }

    private async Task<Message> Usage(Message message)
    {
        const string bot = "@KPI_reschedule_bot";
        const string usage = @$"
Команди
/setgroup{bot} — Встановити групу
/toggleweek{bot} — Змінити порядок тижнів
/schedule{bot} — Повний розклад
/week{bot} — Розклад на тиждень
/nextweek{bot} — Розклад на наступний тиждень
/today{bot} — Розклад на сьогодні
/tomorrow{bot} — Розклад на завтра
/left{bot} — скільки часу лишилося до завершення
<a href='https://github.com/Goganoid/ReSchedule/tree/master'>Код</a>
При проблемах з ботом пишіть на пошту <span class=""tg-spoiler"">yehor.kardash.dev@gmail.com</span>
";

        return await _botClient.SendTextMessageAsync(chatId: message.Chat.Id,
            text: usage,
            replyMarkup: new ReplyKeyboardRemove(), parseMode: ParseMode.Html);
    }

    public static Task UnknownUpdateHandlerAsync(Update update)
    {
        Console.WriteLine($"Unknown update type: {update.Type}");
        return Task.CompletedTask;
    }

    async Task<Message> GroupNotSetMessage(Message message)
    {
        return await _botClient.SendTextMessageAsync(message.Chat.Id, text: "Група не встановлена");
    }

    async Task<Message> ApiErrorMessage(Message message, HttpStatusCode code)
    {
        return await _botClient.SendTextMessageAsync(message.Chat.Id,
            text: $"Помилка при запиті до API. Код:{code}");
    }
}