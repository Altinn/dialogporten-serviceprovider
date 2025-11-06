namespace Digdir.BDB.Dialogporten.ServiceProvider.Playbook;

public static class PlaybookCompiler
{
    public static Task<string> Compile(this PlaybookState playbookState, Command command)
    {
        playbookState.Update(command);
        return playbookState.EncodeToBase64();

    }
}
