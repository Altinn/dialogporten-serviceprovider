namespace Digdir.BDB.Dialogporten.ServiceProvider.Playbook;

public enum CommandType
{
    Next,
    Previous,
    Goto,
    GotoIfProgress
}

public record Command(object Value, CommandType Type);
public record GotoIfProgressValue(int Goto, int Progress, int Else);

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
            "$gotoIfProgress" => new Command(ParseGotoIfProgressCommand(temp[1]), CommandType.GotoIfProgress),
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

}
