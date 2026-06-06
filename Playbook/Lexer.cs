using System.Diagnostics.CodeAnalysis;

namespace Digdir.BDB.Dialogporten.ServiceProvider.Playbook;

public enum CommandType
{
    Next,
    Previous,
    Goto,
    GotoIfProgress,
    Random
}

public record Command(object Value, CommandType Type);
public record GotoIfProgressValue(int Goto, int Progress, int Else);
public record RandomValue(IReadOnlyList<(int Cursor, int Weight)> Choices);

public static class Lexer
{

    public static bool TryParseCommand([NotNullWhen(true)] string? value, [NotNullWhen(true)] out Command? command)
    {
        command = null;
        if (value is null)
        {
            return false;
        }
        
        command = ParseCommand(value);
        return command is not null;

    }
    private static Command? ParseCommand(string value)
    {
        if (!value.StartsWith('$'))
        {
            return null;
        }

        var temp = value.Split('=', 2);
        var command = temp[0] switch
        {
            "$next" => new Command(0, CommandType.Next),
            "$previous" => new Command(0, CommandType.Previous),
            "$goto" => new Command(int.Parse(temp[1]), CommandType.Goto),
            "$gotoIfProgress" => new Command(ParseGotoIfProgressCommand(temp[1]), CommandType.GotoIfProgress),
            "$random" => new Command(ParseRandomCommand(temp[1]), CommandType.Random),
            _ => null
        };

        return command;
    }

    private static GotoIfProgressValue ParseGotoIfProgressCommand(string value)
    {
        var temp = value.Split('-');
        var @goto = int.Parse(temp[0]);
        var progress = int.Parse(temp[1]);
        var @else = int.Parse(temp[2]);
        return new GotoIfProgressValue(@goto, progress, @else);
    }

    private static RandomValue ParseRandomCommand(string value)
    {
        var choices = new List<(int, int)>();
        foreach (var part in value.Split('|'))
        {
            var sub = part.Split(':');
            choices.Add((int.Parse(sub[0]), sub.Length > 1 ? int.Parse(sub[1]) : 1));
        }
        return new RandomValue(choices);
    }

}
