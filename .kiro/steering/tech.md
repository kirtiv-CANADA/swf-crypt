---
inclusion: always
---

# SWF Crypt - Technology

## Stack

**Backend:** C# / ASP.NET Core Web API (.NET 10), EF Core with SQLite, EF Core migrations.

**Parser:** Pure C# class library (`SwfCrypt.Core`), no web dependencies.

**Auth:** ASP.NET Core Identity (roles: `Member`, `Curator`, `Admin`) with policy-based authorization.

**Background work:** `Channel<T>` + `BackgroundService`, no external queue.

**Tests:** xUnit v3 on Microsoft Testing Platform, FsCheck for property-based tests, `WebApplicationFactory` for API integration tests.

**Frontend:** React + TypeScript (Vite), Tailwind CSS, React Router, TanStack Query.

**Player:** Ruffle (self-hosted npm package).

**API contract:** OpenAPI; frontend types are generated from it.

**DevOps:** Docker, GitHub Actions.

## Rules

Everything must run for free. Never add paid services, paid APIs or API keys.

Database is SQLite via EF Core migrations. Do NOT claim PostgreSQL support.

Every protected endpoint uses an authorization policy. Hiding a button in React is not security.

Background analysis uses `Channel<T>` + `BackgroundService` with status tracking: `Queued -> Running -> Succeeded | Failed`. Transient failures retry with backoff (max 3 attempts); permanent failures (for example an invalid SWF) fail immediately without retry.

On startup, jobs left `Queued` or `Running` are re-enqueued.

Duplicate uploads are detected by SHA-256 and handled idempotently (return the existing record).

All list endpoints are paginated (`page`, `pageSize` with a maximum) and support filtering.

All errors are returned as `ProblemDetails`. Never leak stack traces.

Validate every upload: size limit, SWF signature check, required source and license.

Rate limit login and upload endpoints.

The parser must never throw on arbitrary bytes. It returns a typed error result.

Keep `SwfCrypt.Core` independent of ASP.NET so it can be tested in isolation.

`Program` must stay `public partial class Program` so `WebApplicationFactory` can host the API.

Provide a static demo mode (pre-analysed catalog as JSON) so the public URL works even when the live backend is unavailable.

## Domain model (relational)

Users and Roles (Identity).

`SwfFile`: owner, sha256 (unique), title, author, year, license, source, review status, size, created at.

`AnalysisJob`: swf file, state, attempts, last error, started, finished.

`AutopsyReport`: swf file, version, compression, size, frame rate, frame count, ActionScript kind, tag summary, compatibility verdict.

`ReviewDecision`: swf file, reviewer, decision, note, time.

`AuditLog`: user, action, entity type, entity id, time, details.

## Conventions

**C#:** Nullable reference types enabled, async all the way for I/O, small classes.

**TypeScript:** Strict mode, no `any`.

Every new behaviour gets a test; parser behaviours also get a property.