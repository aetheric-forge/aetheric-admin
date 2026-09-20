# Runtime bootstrap integration review

Reviewed runtime `0889cc6c6d9a21408b2fbe75c202b8f18abc657a` (PR #30), updating admin from `f64e5922af10cf1864068ed7892dff1c0030b7d8`.

## Available wire contract

The Worker subscribes through RabbitMQ Post to domain `provisioning`, address `institution/deploy/bootstrap`, contract `institution-bootstrap-requested` version `1.0`, intent `Command`. The exchange is `aetheric.post.provisioning`; the routing key is `institution/deploy/bootstrap.institution-bootstrap-requested.1.0.command`.

`InstitutionBootstrapRequested` contains `RequestId`, `University`, `Campus`, `AdministrationFaculty`, `Decisions`, `RootCredentials`, and `RequestedAtUtc`. Each institution contains `DefinitionYaml` and `BindingsYaml`. Credentials are inline values, not store references. The Worker constructs RabbitMQ, MongoDB, Keycloak, and S3 providers from the corresponding `rabbitmq`, `mongo`, `keycloak`, and `s3` entries. It does not construct the Redis Workbench provider.

Results use address `institution/deploy/bootstrap/result`, contract `institution-bootstrap-completed` version `1.0`, intent `Event`. They contain the request ID, overall success, and four step results (`Succeeded`, `Failed`, or `NotAttempted`). Execution is sequential and stops deploying after a reported failure. Result Post metadata preserves envelope correlation and uses the incoming message ID as causation.

Source: `runtime/src/Tools/Aetheric.Provisioning.Worker/InstitutionBootstrapMessages.cs` and `InstitutionBootstrapRequestConsumer.cs`.

## Findings before enabling submission

1. **Existing admin deployment messages no longer reach this Worker.** Admin's `CampusDeploymentMessages.cs` still uses `campus/deploy` and the v1 Campus contract. Worker `Program.cs` now subscribes only to `institution/deploy` v2 and the new bootstrap route. Admin's old result subscription also needs migration. A successful publish from the legacy HTTP endpoint is not evidence of execution.
2. **Worker restart loses generated credentials while keeping checkpoints.** Worker `Program.cs` deletes the secrets directory and generates a fresh encryption key at every start. `ProvisioningEngine.ExecuteAsync` loads successful checkpoints and reads their secret references before skipping completed resources. After restart those reads fail; the RabbitMQ consumer requeues exceptions, so an identical request can repeatedly fail without a completion event. Persist the key and secrets together with run state before relying on resumable deployments.
3. **Transport does not establish durable acceptance.** `RabbitMqPostProvider` creates its channel without publisher confirms, publishes with `mandatory: false`, and does not set persistent delivery on messages. An absent binding can silently discard a request; a durable queue alone does not make its messages persistent. The same applies to completion events. Submission needs an explicit acceptance and recovery design.
4. **Admin's draft requires translation and additional inputs.** It contains names, profile references, credential-store keys, optional Talent, priority, and child request/causation IDs. Runtime requires four complete definition/bindings documents and actual root credentials. It has no Talent slot or explicit child request IDs/priority field. Admin currently collects Redis/RabbitMQ/Postgres/Mongo root credentials, while the runtime University Registry requires Keycloak credentials and S3 bindings require S3 credentials. Do not serialize the draft directly into the runtime command or silently discard unsupported selections.
5. **Validation is per institution, immediately before execution.** The bootstrap loop can provision University before discovering invalid Campus/Faculty/Decisions YAML. If approval is intended to cover a fully validated hierarchy, preflight every document and resolved binding before publishing. Provisioning is not transactional and does not roll back earlier successful levels.

This pin update does not enable University submission. The next admin integration needs YAML generation/review, explicit credential resolution, the new request/result contracts, and durable request/result tracking; the runtime restart and delivery findings also need resolution.

## Validation

- Admin Release build and all 34 admin tests passed using an isolated Redis fixture.
- Runtime bootstrap resolver/inline-document checks: 6 passed. Eight worker integration tests were skipped by their fixture attributes because isolated RabbitMQ/provider connection settings were not supplied. No live hierarchy deployment was attempted.
- `git diff --check` passed.
