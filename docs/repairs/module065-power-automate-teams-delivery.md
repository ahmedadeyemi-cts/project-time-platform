# Module 065: Power Automate Teams delivery

## Goal

Use one centrally managed Power Automate cloud flow to deliver Pulse notifications into
Microsoft Teams without requiring each recipient to install or configure a custom Pulse
Teams application.

Pulse remains the notification policy and recipient authority. Power Automate is only
the Teams delivery adapter.

## Architecture

Pulse notification event
-> Module 065 resolves authorized recipients and content
-> email delivery and Teams delivery are attempted independently
-> Teams delivery posts one OAuth-authenticated envelope to the Power Automate HTTP trigger
-> the flow uses Microsoft Teams "Post a message in a chat or channel"

The existing Module 065 Microsoft services connection supplies tenant ID, client ID and
client secret. No second Entra application or user-specific credential is introduced.

## Workflow envelope

The HTTP trigger receives:

- eventId / idempotencyKey
- destinationType: individual, group_chat or channel
- recipients
- optional conversationId for an existing group chat
- optional teamId and channelId for an existing Teams channel
- subject and message
- severity / notificationType / sourceModule
- Pulse URL

Routine reminders and assignments are sent individually. A multi-recipient cost alert is
marked group_chat so the centrally managed flow can route it to the approved project-cost
review conversation or channel.

## Microsoft configuration

Use an OAuth-protected "When an HTTP request is received" trigger. Set "Who can trigger
the flow" to Specific users in my tenant and allow the service principal object ID that
corresponds to the existing Module 065 services client ID.

Commercial-cloud audience:
https://service.flow.microsoft.com/

The flow uses the Microsoft Teams connector. Recipients do not create flows and do not
install PulseApp.

Recommended branches:

1. destinationType == individual
   - Apply to each recipient
   - Post a message in a chat or channel
   - Post as: Flow bot
   - Post in: Chat with Flow bot
   - Recipient: current recipient

2. destinationType == group_chat
   - In the Teams action, choose Group chat -> Enter custom value.
   - Bind the value to conversationId from the HTTP trigger.
   - Pulse can therefore route different events to different existing group chats without
     hard-coding one conversation in Power Automate.
   - When conversationId is empty, fail the branch or use an explicitly reviewed fallback;
     do not silently create a new chat for every event.

3. destinationType == channel
   - Bind Team and Channel to the supplied teamId/channelId custom values.
   - Use this for stable shared operational destinations where membership changes over time.

The Power Automate connection should be organization-owned with backup owners. Do not tie
the flow to an employee who may leave or lose access.

## Security and reliability

- HTTP trigger URL must use HTTPS on Microsoft Power Platform / Logic Apps hosts.
- Pulse obtains an Entra token using the existing Module 065 service principal.
- The flow should reject callers other than the approved service principal.
- No Teams app installation is required in Power Automate mode.
- Existing Graph-app delivery remains available only as a legacy fallback.
- Email success is not required before Teams delivery is attempted.
- Teams failure never replays a successful email.
- Durable Module 065 delivery claims prevent accidental duplicate sends.
- Production delivery still requires the existing production-governed recipient boundary.

## UAT

1. Configure the Test flow.
2. Paste its OAuth-protected trigger URL into Module 065 Test.
3. Select Power Automate Workflow and save.
4. Send one personal Test message to an authorized Test recipient.
5. Verify the flow run and Teams message.
6. Exercise one multi-recipient cost alert and verify it goes to the approved shared
   destination.
7. Verify email and Teams results independently.
8. Do not enable Production until Test evidence is complete.
