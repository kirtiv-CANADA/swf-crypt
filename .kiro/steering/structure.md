---
inclusion: always
---

# SWF Crypt - Repository structure

```
swf-crypt/
  .kiro/            steering docs, specs, hooks (must be committed, never ignored)
  backend/
    SwfCrypt.Core/  SWF parser and compatibility predictor (no web dependencies)
    SwfCrypt.Api/   ASP.NET Core Web API, EF Core, SQLite
    SwfCrypt.Tests/ xUnit + FsCheck tests
  frontend/         React + TypeScript app
  samples/          public-domain or self-made SWF files + LICENSES.md
  docs/             architecture notes, demo script, Kiro write-up notes
  .github/workflows CI
```

- Keep the dependency direction: Api -> Core. Core never references Api.
- Sample SWF files live only in `samples/` and every file is listed in
  `samples/LICENSES.md`.
