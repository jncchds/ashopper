# ashopper — notes for coding agents

## Docs live in the repo
- Plans go in `docs/plans/NN-<topic>.md`, numbered and never overwritten. Add a new file for a new plan. Copy each approved plan here.
- Research and surveys go in `docs/research/`.
- Architecture is `docs/architecture.md`. Keep it current when the design changes.

## Layout
- `backend/`: .NET 10 (`Ashopper.slnx`). Domain, Infrastructure (EF Core, price sources, collector pipeline), Api, Collector, and tests.
- `frontend/`: React + TS + Vite. Its own nginx image.
- `infra/`: Bicep for Azure Container Apps and Azure SQL (free offer).
- `.github/workflows/`: CI plus independent API and UI deploys.

## Conventions
- **UI:** follows the user's shared app design language (see `docs/research/sibling-ui-design.md`), implemented the nopds way with Tailwind v4 `@utility` classes. Use its own palette, not one copied from a sibling.
- **API contract:** the build writes `backend/src/Ashopper.Api/openapi/ashopper.json`. Run `npm run gen:api` in `frontend/` after any contract change; CI checks that both generated files are current.
- **Migrations:** `dotnet ef migrations add <Name> -p backend/src/Ashopper.Infrastructure -s backend/src/Ashopper.Api -o Persistence/Migrations`.
- **Free tier:** never add DB-touching health probes, since they keep the free Azure SQL database from auto-pausing.

## Checks
- `dotnet test backend/Ashopper.slnx` (integration tests need Docker)
- In `frontend/`: `npm run lint && npm run lint:i18n && npm run typecheck && npm test && npm run build`
