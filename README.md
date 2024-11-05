# Dialogporten Service Provider

This is a preliminary implementation of a subset of the functionality that should be offered by service providers integrating with [Dialogporten](https://github.com/digdir/dialogporten). 

For now, its main use is providing a tool for the testing of [dialogporten-frontend](https://github.com/digdir/dialogporten-frontend), but eventually this can serve as a reference implementation for service provider systems.

The application implements a ID-Porten client, which is used on the endpoints meant for direct usar navigation (read actions and attachments). As ID-porten has a single circle of trust, being logged in elsewhere (eg. Arbeidsflate) should cause requests to these URLs to be resolved without user interaction (just a redirect pass via ID-porten). There are no local session handling in this application (which a real application would)

## Usage

This application exposes several endpoints that can be used with dialogs for read/write actions, attachments and FCEs (front channel embeds).

### Read actions
A read action is a simple GET request that should cause the user-agent to navigate to it. This simply returns a page with a message that the user has been authenticated. 

This endpoint will require ID-porten authentication, and the user will be redirected to the ID-porten login page if not already authenticated.

Example:
```
GET {baseUrl}/guiaction/read
```

### Write actions
A write action is either a POST or DELETE request, which should be requested with a `Authorization: Bearer`-header containing a dialog token. Failing to do that will cause a 403 Forbidden response. These endpoints implement the CORS protocol with pre-flights, allow all origins and methods.

Both POST and DELETE actions perform mutations on the dialog referred to in the dialog token. By default, the XACML action `write` has to be present in the dialog token for the request to be successful, but the action to check for can be overridden by setting a `xacmlAction` query parameter.

Requests for POST and DELETE actions can be configured to return immediately with a `202 Accepted` response, and let the backchannel request to Dialogporten to be handle asynchronously (and after a small delay to emulate queuing behavior). Alternatively, the request can be handled synchronously, and a 204 No Content response will be returned after the backchannel request has been resolved. If the backchannel request fails, a 500 Internal Server Error response will be returned.

This behavior can be controlled by setting the `queueInBackground` query parameter to `true` or `false`.

#### Using POST

Use this endpoint to perform various mutations on the dialog to test the behavior of the dialog frontend. Any request body provided is ignored.

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

Use this endpoint to perform a soft deletion of the dialog to test deletion handling behavior in the dialog frontend. No parameters besides `queueInBackground` are supported; the dialog id is taken from the dialog token.

Example:
```
DELETE {baseUrl}/guiaction/write?queueInBackground=true
```

### Attachments
This application can provide sample attachments for dialogs. Any filename can be provided using one of the following extensions: pdf, zip, docx. Correct MIME types are set for these extensions, and a valid file will be presented. By passing a boolean query parameter `inline`, the attachment can be set to be displayed inline in the browser using the `Content-Disposition` header. Otherwise, the attachment will be downloaded using the provided filename.

This endpoint will require ID-porten authentication, and the user will be redirected to the ID-porten login page if not already authenticated.

Example:
```
GET {baseUrl}/attachment/sample.pdf?inline=true
GET {baseUrl}/attachment/arbitrary-name.zip 
GET {baseUrl}/attachment/my-document.docx 
```

### Front Channel Embeds (FCEs)

Front channel embeds are akin to "iframes" that can be embedded in the dialog frontend. The FCEs are loaded using a GET request, using a dialog token and the content is returned is markdown, which then the frontend should map to HTML and render. An option to return HTML directly also exists, by setting the `html` query parameter to `true`.

The endpoint expects a dialog token to be provided in the `Authorization: Bearer` header. If the token is missing, the request will be rejected with a 403 Forbidden response. This endpoints supports CORS pre-flights, and allows all origins and methods.

No parameters are supported for this endpoint.

Example:
```
GET {baseUrl}/fce?html=false
```

### Current limitations
- Only works in the test (not staging) environment.
- Assumes that the service owner for the dialog is Digdir.

### License
MIT
