using System.Globalization;
using ReSchedule.Entities;
namespace ReSchedule;

internal static class Helpers
{
    public static (string, List<string>) ParseCommand(string command)
    {
        var commandName = "";
        List<string> args = new();
        var split = command.Split(' ');
        switch (split.Length)
        {
            case 1:
                commandName = split[0];
                break;
            case > 1:
                (commandName, args) = (split[0], split[1..].ToList());
                break;
        }
        return (commandName, args);
    }

    public static DateTime ParseTime(string time)
    {
        return DateTime.ParseExact(time, "H.mm", CultureInfo.InvariantCulture,DateTimeStyles.None);
    }

    public static string WeekToString(IEnumerable<WeekDay> week)
    {
        return string.Join('\n', week.Where(w=>w.Pairs.Count!=0));
    }
}