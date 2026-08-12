# Ask Celar AI Public-Question Validation Evidence

This source-controlled validation note records the guarded public-question correction associated with PR #644.

The implementation and automated tests require all of the following:

- a public officeholder question such as `Who is the president of Jordan?` is classified as public general knowledge rather than as an internal Pulse subject;
- the private Celar AI route receives only the public question and a public-only system instruction;
- private documents, attachments, tool results, customer/project context, people records, financial values, credentials, and internal technical inventory are excluded;
- public output is bounded and uses the governed Module 064 evidence contract;
- unavailable targets return an HTTP-success, evidence-limited answer rather than a generic supporting-service failure;
- named organizations or enterprise context remain fail-closed and are not automatically treated as public;
- Migration 084 validation uses password authentication in disposable CI and does not configure PostgreSQL trust authentication;
- no Test or Production deployment is performed by PR #644 itself.

This file contains no customer data, prompt text, credentials, bearer tokens, document content, model output, or embedding values.
