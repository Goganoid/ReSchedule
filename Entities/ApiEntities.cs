using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Text;

namespace ReSchedule.Entities;

public class Group
{
    public string Id { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string Faculty { get; set; } = null!;
}

public class Response<T>
{
    public T? Data;
    public HttpStatusCode StatusCode;
}

[SuppressMessage("ReSharper", "MemberCanBePrivate.Global")]
public class Pair
{
    public string Name { get; set; } = null!;
    public string Type { get; set; } = null!;
    public string Time { get; set; } = null!;

    public override string ToString()
    {
        return $"<i>{Time}</i> {Name}, {Type}";
    }
}

public class WeekDay
{
    public readonly string Day = null!; 
    // ReSharper disable once CollectionNeverUpdated.Global
    public readonly List<Pair> Pairs = new();

    public override string ToString()
    {
        var pairsStr = new StringBuilder();
        foreach (var pair in Pairs.OrderBy(p=> Helpers.ParseTime(p.Time)))
        {
            pairsStr.AppendLine($"{pair}");
        }

        return $"<b>{Day}</b>\n{pairsStr}";
    }
}

public class Schedule
{
    public List<WeekDay> ScheduleFirstWeek { get; set; } = new();
    public List<WeekDay> ScheduleSecondWeek { get; set; } = new();

    public override string ToString()
    {
        return $"Перший тиждень\n{Helpers.WeekToString(ScheduleFirstWeek)}Другий тиждень\n{Helpers.WeekToString(ScheduleSecondWeek)}";
    }
}

public class ScheduleTime
{
    public int CurrentWeek;
    public int CurrentDay;
    public int CurrentLesson;
}
