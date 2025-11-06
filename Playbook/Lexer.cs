using System.Diagnostics;

namespace Digdir.BDB.Dialogporten.ServiceProvider.Playbook;

public enum CommandType
{
    Next,
    Previous,
    Goto,
}

public record Command(int Value, CommandType Type);

public static class Lexer
{

    public static Command? ParseCommand(string value)
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
            _ => null
        };

        return command;
    }

}
