# SWF Crypt

> Where dead Flash rests, and rises.

SWF Crypt is a Flash preservation museum. Bury a SWF file, get an **autopsy**
(parsed metadata and a Ruffle compatibility report), then **resurrect** it in your
browser.

Built for the **Kiroween 2026** hackathon, **Resurrection** category, as a solo
project, using Kiro.

> This is a preservation and museum tool. It does not unpack or bypass any
> protection on SWF files.

## Features
- Upload a SWF and get a structured autopsy report (header, compression, tags, assets, ActionScript version)
- Ruffle compatibility prediction
- Tombstone-card museum with search, filters and provenance (source + license)
- In-browser playback with Ruffle

## Tech stack
C# / ASP.NET Core, EF Core + SQLite, React + TypeScript (Vite), Tailwind CSS,
Ruffle, xUnit + FsCheck, Docker, GitHub Actions.

## Getting started
_To be filled in as the project is built._

```bash
# backend
cd backend
dotnet run --project SwfCrypt.Api

# frontend
cd frontend
npm install
npm run dev
```

## How Kiro was used
See the `.kiro/` directory (steering docs, specs, hooks). A full write-up is in
`docs/kiro-writeup.md`.

## Sample files and legal
Only public-domain, self-made or explicitly licensed SWF files are included.
See `samples/LICENSES.md` for the source and license of each file.

## Credits
- [Ruffle](https://ruffle.rs): Flash Player emulator (MIT / Apache-2.0)

## License
See the LICENSE file.
