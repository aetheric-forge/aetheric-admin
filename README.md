# Aetheric Admin

Backend integration host for the Aetheric Forge. The current Blazor app hosts Workbench and Operations → Maintenance, scheduled/manual jobs, and shared membership checks.

Initialize pinned dependencies with `git submodule update --init --recursive`, then build with `dotnet build --configuration Release`.

Normal admin mode also includes [University setup](docs/university-setup.md) at `/university`: an initial University → Campus → Administration designer with Decisions, optional Talent, and a downloadable envelope draft. Runtime Operations submission is pending its University contract.

See [Redis persistence](docs/redis-persistence.md) for run-history and Data Protection configuration, migration, deployment limits, and test commands. Job definitions, encrypted credentials, and membership applications also require the MongoDB settings in `appsettings.json`. Outside Development, authentication uses the configured Keycloak client.

Admin also hosts the shared platform bootstrap workflow in explicit bootstrap mode. See [bootstrap setup and migration](docs/bootstrap.md) for initialization, configuration, container commands, and the transition back to normal admin mode.
