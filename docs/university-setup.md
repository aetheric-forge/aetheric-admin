# University setup draft

Normal admin mode exposes `/university` through the home page and navigation. It contains the University and Campus forms, an initial Administration Faculty designer, required Decisions, and optional Talent. Bootstrap mode deliberately does not expose this route. Its existing Keycloak/infrastructure credential collection remains unchanged.

## Current behavior

Enter the names and the existing Keycloak authority, realm, client ID, and root administrator subject ID. Normal admin's Keycloak settings prefill the connection identifiers when configured. These are editable draft values, not proof of identity or permission. The page never retrieves bootstrap passwords or claims to have verified the selected account.

Private resource profile references for University, Campus, and Faculty, and the University IAM definition reference, are optional pending the agreed definitions. Empty values appear as unresolved in review; typing a reference does not validate or provision it. Decisions' resource ownership is described from its existing definition, but this draft does not load that definition or resolve its bindings. Talent is omitted entirely until selected.

Review validates the required fields and produces an immutable snapshot. Editing any field clears the review and download until the operator reviews again. The responsive review shows containment, causality, and prerequisites separately and permits downloading a JSON draft without credential values. The UI explicitly says nothing has been queued or deployed.

Draft values and IDs live in the current interactive server circuit only. Reloading, leaving the page, expiry, or restarting the host may discard them. Download preserves a reference copy; importing it or resuming it after restart is not implemented. Separate operator circuits never share the draft.

## Envelope shape and causality

`aetheric-admin/university-draft/v1` is an **admin-owned draft schema**, not an executable runtime wire contract. Do not publish it on the existing `campus/deploy` address, which accepts a different payload.

| Request | Parent request | Caused by | Depends on |
| --- | --- | --- | --- |
| University | None | Operator submission (no preceding request ID) | None |
| Campus | University | University | University |
| Administration Faculty | Campus | University | Campus |
| Decisions Institution | Administration | University | Administration |
| Talent Institution (optional) | Administration | University | Administration |

The envelope has its own ID, the initiating University request ID, and `Standard` priority. Each request has a stable ID. Names can change without changing IDs; removing and restoring Talent within the same draft preserves its ID. Request dependencies are conservative parent prerequisites; Decisions and Talent have no dependency on each other. They are not provider-level resource plans.

Keycloak identity metadata and the IAM definition reference belong to the University envelope. Root credential references use the existing encrypted bootstrap store's system keys (`redis`, `rabbitmq`, `postgres`, `mongo`). They are local lookup keys, not universally resolvable secret URIs. Credential availability, transport, target scope, and authorization must be checked by the eventual submission integration. No Keycloak client secret is included or inferred.

## Runtime integration still required

The agreed receiving service is runtime-wide Operations, available before the University exists. It accepts one envelope as one standard-priority provisioning job, with runtime-built subscribers dispatching it to the runtime-wide provisioning service. Queueing and dispatch must preserve the initiating request and child causation IDs. Containment is independent of both message dispatch and causal provenance.

The pinned runtime now implements `InstitutionBootstrapRequested` at `provisioning` / `institution/deploy/bootstrap` (contract `institution-bootstrap-requested`, version `1.0`). It accepts four inline definition/bindings YAML pairs and root credential values; this admin draft is not that payload. Submission still needs a YAML adapter, credential resolution, durable queue acceptance/idempotency semantics, and job/result tracking. See the [runtime integration review](runtime-bootstrap-review.md). The disabled submission control intentionally does not fabricate success or send this draft to the Campus consumer. Likewise, the existing recurring/SSH maintenance job dispatcher is not used as a substitute.

Before enabling submission, agree the resource profiles, technology bindings, and IAM definitions, then validate the entire resolved envelope before approval. Execution, checkpoints, and retries belong to the provisioner. Persistent draft storage and post-bootstrap navigation into this workflow are separate remaining integrations.

## Verification

`UniversityDraftTests` covers direct University causation, containment, dependency ordering, optional Talent and stable IDs, immutable review snapshots, credential-reference serialization, invalid identity inputs, required fields, separate operator sessions, and rendering without infrastructure. The bootstrap smoke script checks that both maintenance and University routes remain inaccessible in bootstrap mode. Browser interaction and live University deployment are not claimed by these checks.
