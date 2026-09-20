# University draft to runtime bootstrap v1

The authoritative [runtime interoperability profile](../runtime/docs/provisioning/contracts/bootstrap-v1/README.md) and JSON fixtures define PR #1 of the bootstrap integration. Admin declares independent wire types in `InstitutionBootstrapMessages.cs`; tests consume the canonical files from the pinned runtime. There is no runtime Worker project dependency in the admin application.

`BootstrapDraftIdentity.Create` checks schema, priority, the four kinds, containment, causation, prerequisites, and distinct IDs before creating metadata. It does not establish deployment readiness.

This change settles the wire mapping; it does not convert a draft into executable YAML or enable publishing. Decisions' actual definition and capabilities remain owned by its definition work. Fixture YAML is not a substitute.

| Draft input | Executable mapping / readiness condition |
| --- | --- |
| Schema | `aetheric-admin/university-draft/v1` is local draft format; never send as the runtime payload. Reject unsupported draft schemas. |
| EnvelopeId | Runtime payload RequestId and Post MessageId. Freeze on approval; a new changed submission needs a new envelope ID. |
| InitiatingRequestId | Must equal the University request ID; Post CorrelationId and University identity header. |
| Priority | Must be Standard; named Post header. Other priorities are unsupported. This does not enable broker prioritization. |
| Four RequestIds / Kinds | Map University, Campus, Faculty, Decisions into fixed slots and named identity headers. Reject missing, extra, duplicate, or mismatched kinds/IDs. |
| ParentRequestId / DependsOn | Must match University → Campus → Faculty → Decisions, each child depending on its immediate parent. Fixed topology preserves these relationships. |
| CausedByRequestId | Null for University; University request ID for all children, as specified by the profile. Reject other graphs. |
| Names | Resolve into the corresponding definition's display name before review; retain stable definition/resource identifiers according to the approved definition. Do not use labels as provider identifiers. |
| PrivateResourceProfileReference | Resolve University/Campus/Faculty profiles into exact definition/bindings text. A typed reference or an unresolved/empty profile is not approval. Block until a supported profile or explicit approved configuration resolves it. |
| Authority / Realm / ClientId | Identity configuration inputs. Resolve into the approved IAM configuration and applicable bindings; these are not root credentials. Block if the selected IAM implementation cannot represent/apply them. |
| RootAdministratorSubjectId | Intended existing administrator identity, not proof of authorization. It must be checked and represented by a supported IAM assignment operation before submission; current generic bootstrap must not claim to have assigned it. |
| IamDefinitionReference | Resolve and review actual IAM operations/configuration. Block if absent/unresolved or if runtime cannot execute the requested identity setup. |
| RootCredentialReferences | Server-local lookup keys only. Resolve exactly the credentials required by resolved bindings and parent resolvers. Extend credential collection for Keycloak/S3 separately; never infer root access from normal admin sign-in configuration. |
| Talent | Unsupported for initial bootstrap. No form control; selecting it in an old/programmatic draft produces a validation error. Never silently omit a selected institution. |

All resolution, authorization, credential collection, and execution-readiness conditions above are requirements for the later submission adapter. Current draft review is still a draft-only operation and does not assert those conditions have passed. The four configuration snapshots and identity mapping must survive approval unchanged; secret values must never be added to draft downloads.

A result is associated with the approved envelope by payload RequestId. Its step labels map back to the four stable request IDs retained in Post headers and, later, the durable submission record. The existing legacy Campus endpoint/result subscriber remains unchanged until the dedicated migration PR; these new types are not registered as a live submission path.

## Validation

Admin Release build and all 42 tests passed with an isolated Redis fixture. Runtime Release build and 13 focused contract/bootstrap tests passed without live infrastructure. No deployment or broker durability test is claimed by this contract change.
