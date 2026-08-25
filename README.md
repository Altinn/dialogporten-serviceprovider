# Dialogporten Service Provider

This is a preliminary implementation of a subset of the functionality that should be offered by service providers
integrating with [Dialogporten](https://github.com/digdir/dialogporten).

For now, its main use is providing a tool for the testing
of [dialogporten-frontend](https://github.com/digdir/dialogporten-frontend), but eventually this can serve as a
reference implementation for service provider systems.

The application implements a ID-Porten client, which is used on the endpoints meant for direct usar navigation (read
actions and attachments). As ID-porten has a single circle of trust, being logged in elsewhere (eg. Arbeidsflate) should
cause requests to these URLs to be resolved without user interaction (just a redirect pass via ID-porten). There are no
local session handling in this application (which a real application would)

## Usage

This application exposes several endpoints that can be used with dialogs for read/write actions, attachments and FCEs (
front channel embeds).

### Read actions

A read action is a simple GET request that should cause the user-agent to navigate to it. This simply returns a page
with a message that the user has been authenticated.

This endpoint will require ID-porten authentication, and the user will be redirected to the ID-porten login page if not
already authenticated.

Example:

```
GET {baseUrl}/guiaction/read
```

### Write actions

A write action is either a POST or DELETE request, which should be requested with a `Authorization: Bearer`-header
containing a dialog token. Failing to do that will cause a 403 Forbidden response. These endpoints implement the CORS
protocol with pre-flights, allow all origins and methods.

Both POST and DELETE actions perform mutations on the dialog referred to in the dialog token. By default, the XACML
action `write` has to be present in the dialog token for the request to be successful, but the action to check for can
be overridden by setting a `xacmlAction` query parameter.

Requests for POST and DELETE actions can be configured to return immediately with a `202 Accepted` response, and let the
backchannel request to Dialogporten to be handle asynchronously (and after a small delay to emulate queuing behavior).
Alternatively, the request can be handled synchronously, and a 204 No Content response will be returned after the
backchannel request has been resolved. If the backchannel request fails, a 500 Internal Server Error response will be
returned.

This behavior can be controlled by setting the `queueInBackground` query parameter to `true` or `false`.

#### Using POST

Use this endpoint to perform various mutations on the dialog to test the behavior of the dialog frontend. Any request
body provided is ignored.

The following boolean flags may be set in the request body to test different scenarios:

- `addAttachment`: Add an attachment to the dialog.
- `addActivity`: Add an activity to the dialog.
- `addTransmission` Add a transmission to the dialog.
- `setDialogGuiActionsToDeleteOnly`: Set the dialog GUI actions to contain a single "Delete" action.

In addition, the status of the dialog can be set to any of the legal enum values by providing the following parameter:

- `setStatusTo`: Set the status of the dialog to the query parameter value.

Example:

```
POST {baseUrl}/guiaction/write?queueInBackground=true&addAttachment=true&addActivity=true&addTransmission=true&setDialogGuiActionsToDeleteOnly=true&setStatusTo=COMPLETED
```

#### Using DELETE parameters

Use this endpoint to perform a soft deletion of the dialog to test deletion handling behavior in the dialog frontend. No
parameters besides `queueInBackground` are supported; the dialog id is taken from the dialog token.

Example:

```
DELETE {baseUrl}/guiaction/write?queueInBackground=true
```

### Attachments

This application can provide sample attachments for dialogs. Any filename can be provided using one of the following
extensions: pdf, zip, docx. Correct MIME types are set for these extensions, and a valid file will be presented. By
passing a boolean query parameter `inline`, the attachment can be set to be displayed inline in the browser using the
`Content-Disposition` header. Otherwise, the attachment will be downloaded using the provided filename.

This endpoint will require ID-porten authentication, and the user will be redirected to the ID-porten login page if not
already authenticated.

Example:

```
GET {baseUrl}/attachment/sample.pdf?inline=true
GET {baseUrl}/attachment/arbitrary-name.zip 
GET {baseUrl}/attachment/my-document.docx 
```

### Front Channel Embeds (FCEs)

Front channel embeds are akin to "iframes" that can be embedded in the dialog frontend. The FCEs are loaded using a GET
request, using a dialog token and the content is returned is markdown, which then the frontend should map to HTML and
render. An option to return HTML directly also exists, by setting the `html` query parameter to `true`.

The endpoint expects a dialog token to be provided in the `Authorization: Bearer` header. If the token is missing, the
request will be rejected with a 403 Forbidden response. This endpoints supports CORS pre-flights, and allows all origins
and methods.

No parameters are supported for this endpoint.

Example:

```
GET {baseUrl}/fce?html=false
```

### Playbook

A playbook is a scripted, multi-step dialog: one YAML file describing a set of stages that are
compiled into JSON Patch operations and applied to a single Dialogporten dialog, with every GUI
action advancing to the next stage.

- **Authoring reference: [`docs/playbook-dsl.md`](docs/playbook-dsl.md)** — the full
  `*.playbook.yaml` format (stages, session variables, effects, conditional actions, routers,
  randomness, front channel embeds), plus the gotchas checklist.
- Worked examples: `sample-complex.playbook.yaml`, `sample-cyoa.playbook.yaml`,
  `sample-game.playbook.yaml`, `sample-dungeon.playbook.yaml`.
- Upload a file at `/playbook/create` in the running app, or
  `POST /playbook/create-from-dsl` with `Content-Type: text/yaml`.
- **Environment.** The create page has an environment picker (TT02, AT23, local); the API takes
  `?environment=<key>` (`create-from-dsl`) or an `environment` field in the JSON body (`create`).
  Omitting it uses `DialogportenEnvironments:Default`. `GET /playbook/environments` lists the
  configured environments. Whichever environment a playbook is created in is stored with its
  server-side state, so every later stage is patched into that same environment.
- Validate without starting the server:
  `dotnet run --project .claude/skills/playbook-author/lint -- <file.playbook.yaml>`

#### Commands

These are the low-level cursor commands the compiler emits. When authoring in YAML you use the
DSL's targets (`next`, `previous`, `restart`, stage names, `?(...)`, `@var(...)`) instead.

|           Command           | Params                             |                            Description                             |
|:---------------------------:|:-----------------------------------|:------------------------------------------------------------------:|
|            $next            | n/a                                |                        Moves the crusor +1                         |
|          $previous          | n/a                                |                        Moves the crusor -1                         |
|          $goto=(1)          | 1 = number                         |                Moves the cursor to a spesific index                |
| $gotoIfProgress=(1)-(2)-(3) | 1 = number, 2 = number, 3 = number | goto (param 1) if (param 2) == dialog.progress else goto (param 3) |

#### Environment configuration

The environments a playbook can target are configured in `appsettings.json`. Only the playbook
flows are environment-aware; the other endpoints and pages use the single client configured by
`DialogportenSettings:BaseUri`.

```json
"DialogportenEnvironments": {
  "Default": "tt02",
  "Environments": {
    "tt02": {
      "DisplayName": "TT02",
      "DialogportenBaseUri": "https://platform.tt02.altinn.no/dialogporten",
      "AfUri": "https://af.tt02.altinn.no/"
    },
    "local": {
      "DisplayName": "Local",
      "DialogportenBaseUri": "https://localhost:7214",
      "AfUri": "http://localhost:3000/",
      "UseMaskinporten": false
    }
  }
}
```

`DialogportenBaseUri` is the Dialogporten base URI up to but excluding `/api/v1`; `AfUri` is used
to build the "Open in Arbeidsflate" link.

**Callback URLs** (the GUI action and FCE URLs a playbook writes into its dialog) are based on the
first of: the environment's optional `MutateBaseUri`, `ServiceProvider:mutateBaseUri`, or — normally
— the URL the app itself is being browsed on. That last fallback is what makes a playbook created
through the deployed app call back to the deployed app; pin one of the settings only when neither is
reachable from the browser showing Arbeidsflate (eg. an ngrok tunnel). The create page shows the
resolved value, and it is stored with the playbook so later stages emit the same URLs. All environments share the Maskinporten settings under
`DialogportenSettings:Maskinporten` (Maskinporten test serves both TT02 and the AT environments);
set `UseMaskinporten: false` for a local Dialogporten running with authentication disabled.
Dialog tokens are accepted from every configured environment — the JWKS cache polls each one, and
an environment that is not reachable is logged and skipped.

### Current limitations

- Only works in the test (not staging) environment.
- Assumes that the service owner for the dialog is Digdir.

### License

MIT
