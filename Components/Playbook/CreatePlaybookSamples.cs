namespace Digdir.BDB.Dialogporten.ServiceProvider.Components.Playbook;

internal static class CreatePlaybookSamples
{
    public const string Patches = """
        [
          [
            {
              "op": "replace",
              "path": "/content/title/value/0/value",
              "value": "Stage 1: Start"
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
                  "title": [ { "languageCode": "en", "value": "Next: Step 2" } ]
                }
              ]
            }
          ],
          [
            {
              "op": "replace",
              "path": "/content/title/value/0/value",
              "value": "Stage 2: Collect Info"
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
                  "title": [ { "languageCode": "en", "value": "Next: Step 3" } ]
                },
                {
                  "action": "submit",
                  "url": "$goto=0",
                  "isDeleteDialogAction": false,
                  "priority": "secondary",
                  "title": [ { "languageCode": "en", "value": "Back to Step 1" } ]
                }
              ]
            }
          ],
          [
            {
              "op": "replace",
              "path": "/content/title/value/0/value",
              "value": "Stage 3: Review"
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
                  "title": [ { "languageCode": "en", "value": "Next: Step 4" } ]
                },
                {
                  "action": "submit",
                  "url": "$previous",
                  "isDeleteDialogAction": false,
                  "priority": "secondary",
                  "title": [ { "languageCode": "en", "value": "Back to Step 2" } ]
                }
              ]
            }
          ],
          [
            {
              "op": "replace",
              "path": "/content/title/value/0/value",
              "value": "Stage 4: Confirm"
            },
            {
              "op": "replace",
              "path": "/guiActions",
              "value": [
                {
                  "action": "submit",
                  "url": "$goto=0",
                  "isDeleteDialogAction": false,
                  "priority": "primary",
                  "title": [ { "languageCode": "en", "value": "Restart at Step 1" } ]
                },
                {
                  "action": "submit",
                  "url": "$previous",
                  "isDeleteDialogAction": false,
                  "priority": "secondary",
                  "title": [ { "languageCode": "en", "value": "Back to Step 3" } ]
                }
              ]
            }
          ]
        ]
        """;
}
