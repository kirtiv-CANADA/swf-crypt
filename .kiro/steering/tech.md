---
inclusion: always
---

# SWF Crypt - Technology

## Stack
- Backend: C# / ASP.NET Core Web API, EF Core with SQLite
- Parser: pure C# class library (SwfCrypt.Core), no web dependencies
- Tests: xUnit, plus property-based tests with FsCheck for the parser
- Frontend: React + TypeScript (Vite), Tailwind CSS, React Router, TanStack Query
- Player: Ruffle (self-hosted npm package)
- API contract: OpenAPI. Frontend types are generated from the OpenAPI document.
- DevOps: Docker, GitHub Actions

## Rules
- Everything must run for free. Never add paid services, paid APIs or API keys.
- The parser must never crash on arbitrary bytes. It returns a typed error instead.
- Public API changes require updating the OpenAPI document and regenerating
  frontend types.
- Keep the SWF parser independent of ASP.NET so it can be tested in isolation.
- Provide a static demo mode (pre-parsed catalog as JSON) so the public URL works
  even when the live backend is unavailable.

## Conventions
- C#: nullable reference types enabled, async all the way for I/O, small classes.
- TypeScript: strict mode, no `any`.
- Every new behaviour gets a test; parser behaviours also get a property.
