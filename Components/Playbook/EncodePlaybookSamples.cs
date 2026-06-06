namespace Digdir.BDB.Dialogporten.ServiceProvider.Components.Playbook;

internal static class EncodePlaybookSamples
{
    public const string PlaybookState = """
        {
          "DialogId": "00000000-0000-0000-0000-000000000000",
          "Cursor": 0,
          "Patches": [
            [
              {
                "op": "replace",
                "path": "/content/title/value/0/value",
                "value": "Stage 1"
              },
              {
                "op": "replace",
                "path": "/guiActions",
                "value": [
                  {
                    "action": "submit",
                    "url": "$next",
                    "isDeleteDialogAction": false,
                    "priority": "primary",
                    "title": [ { "languageCode": "en", "value": "Next" } ]
                  }
                ]
              }
            ]
          ]
        }
        """;
}
