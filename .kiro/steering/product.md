---
inclusion: always
---

# SWF Crypt - Product

SWF Crypt is a Flash preservation museum built for the Kiroween 2026 hackathon
(Resurrection category). It is a solo project.

## What it does
1. **Bury**: a user uploads a SWF file (or picks one from the bundled sample library).
2. **Autopsy**: the backend parses the SWF (header, compression, tags, assets,
   ActionScript 1/2 vs 3) and produces a structured report.
3. **Compatibility report**: from the parsed data, predict how well the file is
   likely to run in the Ruffle emulator.
4. **Tombstone card**: every file is shown as a tombstone card with title, year,
   author, license, source, epitaph and a status.
5. **Resurrect**: play the file in the browser using Ruffle.
6. **Museum**: search, filter and browse the collection with provenance info.

## Status vocabulary
- Resurrected: expected to run well in Ruffle
- Haunted: runs, but with likely problems
- Beyond repair: unsupported or unparseable

## What is original work
The SWF parser, the autopsy report, the compatibility predictor and the museum UI.
Ruffle is only the playback engine and must be credited. Do not describe the
project as "a Ruffle wrapper".

## Goals for any change
- Keep the Must-have flow working end to end (upload -> autopsy -> resurrect).
- Prefer a small, polished feature over a large, half-finished one.
