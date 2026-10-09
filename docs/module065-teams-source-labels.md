# Module 065: readable Teams notification sources

## Purpose and scope

Teams notifications should identify their originating workspace instead of showing only a numeric module code. This change adds the presentation-only `sourceModuleLabel` property to the existing Power Automate envelope. It does not replace `sourceModule`, change recipients or destinations, request different permissions, introduce migrations, or modify OAuth token acquisition.

Initial explicit display mappings:

| Original source | Display label |
| --- | --- |
| `065` | `Module 065 - Teams Integration` |
| `025` | `Module 025 - SOW & GSD Workspace` |
| Other one-to-three-digit code, such as `999` | `Module 999` |
| Named source, such as `Project FlowHive` | The source name, trimmed, limited to 128 characters, and with control characters removed |
| Missing or whitespace-only source | `Pulse` |

For display only, numeric codes are left-padded to three digits. The original `sourceModule` is always preserved exactly. Labels derive from the envelope's originating source, not from the fact that Module 065 transports the notification. Unknown codes must not be guessed to belong to Teams Integration. Further named mappings should be added only with verified module names and regression cases.

The property is computed by the shared envelope, so personal, group-chat, and channel notifications receive the same label logic. There is no database lookup or extra HTTP call. These are plain-text labels, not HTML.

Example of the additive fields for a SOW notification:

```json
{
  "sourceModule": "025",
  "sourceModuleLabel": "Module 025 - SOW & GSD Workspace"
}
```

## Power Automate adoption: one shared Compose change

A repository PR does not edit the tenant's existing cloud flow. The current Compose reads `sourceModule`, so it will continue to display the original code until the flow owner makes the following explicit configuration update. All three Teams actions should continue consuming the same `Build Teams Message` output.

1. In the HTTP trigger's existing JSON schema, add this optional entry inside `properties`. Keep the original `sourceModule` definition and existing required fields. Do not replace the entire schema with this fragment or add the label to `required`.

   ```json
   "sourceModuleLabel": { "type": "string" }
   ```

2. In `Build Teams Message`, use the Expression editor to replace only the existing source-value expression:

   ```text
   if(empty(triggerBody()?['sourceModule']), 'Pulse', triggerBody()?['sourceModule'])
   ```

   with this backward-compatible, HTML-escaped display expression:

   ```text
   replace(
     replace(
       replace(
         if(
           empty(triggerBody()?['sourceModuleLabel']),
           if(empty(triggerBody()?['sourceModule']), 'Pulse', triggerBody()?['sourceModule']),
           triggerBody()?['sourceModuleLabel']
         ),
         '&', '&amp;'
       ),
       '<', '&lt;'
     ),
     '>', '&gt;'
   )
   ```

   Leave the surrounding `concat(...)`, subject, message body, severity, and Pulse link unchanged. Escaping is only for insertion into the HTML message; never store escaped text as the module identity. This displays the ampersand in `SOW & GSD Workspace` correctly and prevents source text from introducing HTML tags.

3. Keep each Teams action's Message bound to the Compose Outputs dynamic-content token. Do not type `outputs('Build_Teams_Message')` as literal message text. Selecting Outputs avoids dependence on guessed internal action names.

4. Save the flow. Existing senders without `sourceModuleLabel` continue to display `sourceModule`, or `Pulse` if both are empty. Deploy the PR only after normal review and explicit deployment approval. A strict schema that disallows additional properties must be updated before deploying the sender.

No change is needed to Allowed users, the services client ID or secret, tenant ID, token audience, delivery mode, or recipient authorization.

## Acceptance and rollback

Run the existing synthetic transport suite:

```sh
dotnet run --project tests/MicrosoftTeamsDeliveryTests/MicrosoftTeamsDeliveryTests.csproj --configuration Release
```

It checks named/numeric/missing/long source cases, confirms the actual workflow POST contains the friendly label alongside the raw code, and verifies envelope serialization for all three destinations without changing event IDs, idempotency keys, content, recipients, or destination IDs. Existing token, authorization, rate-limit, and secret-redaction assertions remain in place. The suite makes no live Microsoft calls.

After separately approved deployment and the flow update, send one authorized Module 065 personal test. Verify `sourceModule` remains `065` in the trigger input and the Teams message displays `Source: Module 065 - Teams Integration`. For an authorized SOW notification, verify the source remains `025` and the display names the SOW & GSD Workspace. Capture flow-run status and the actual visible Teams message; HTTP acceptance alone is not final message-delivery evidence.

Group-chat and channel delivery still need their own authorized end-to-end tests. Synthetic serialization coverage is not a claim that either shared destination has been tested in the tenant.

To roll back the presentation, restore the prior source-value expression in Compose. To roll back the backend, use the approved release process. The updated Compose already tolerates senders without the additive label, so rollback does not require changing recipients or authentication.

## Destination-expression verification

This is separate from the label change. Enter dynamic destination values using the Expression editor or matching dynamic-content tokens, not as literal custom strings. The saved action Code view should contain evaluated expressions such as:

```json
"@triggerBody()?['conversationId']"
"@triggerBody()?['teamId']"
"@triggerBody()?['channelId']"
```

These are individual example values, not a complete JSON object. The Switch case value is `group_chat` with an underscore, even if the visible branch title says `group chat`. Do not use a personal-test result to infer shared-destination success, and do not change a destination to an arbitrary chat or channel merely to make a test pass.

## Microsoft references

- Send a message in Teams using Power Automate: https://learn.microsoft.com/en-us/power-automate/teams/send-a-message-in-teams
- Workflow expression reference: https://learn.microsoft.com/en-us/azure/logic-apps/workflow-definition-language-functions-reference
