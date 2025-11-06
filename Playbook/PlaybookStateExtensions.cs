using System.Text;
using System.Text.Json;

namespace Digdir.BDB.Dialogporten.ServiceProvider.Playbook;

public static class PlaybookStateExtensions
{
    public static PlaybookState Update(this PlaybookState state, Command command)
    {
        switch (command.Type)
        {
            case CommandType.Next:
                state.Cursor += 1;
                break;
            case CommandType.Previous:
                state.Cursor -= 1;
                break;
            case CommandType.Goto:
                state.Cursor = command.Value;
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }

        return state;
    }



}
