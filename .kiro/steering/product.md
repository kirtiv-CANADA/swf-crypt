---
inclusion: always
---

# SWF Crypt - Product

SWF Crypt is a Flash preservation museum. It is built for the Kiroween 2026 hackathon (Resurrection category) and doubles as a production-style full-stack portfolio project. Solo project.

## Roles

| Role | Can do |
| :--- | :--- |
| Visitor (not logged in) | Browse the public museum, open Autopsy reports, play approved files with Ruffle |
| Member | Everything a Visitor can do, plus register/login, upload SWF files, track the status of own uploads |
| Curator | Everything a Member can do, plus review pending uploads (approve or reject with a note) |
| Admin | Everything a Curator can do, plus manage user roles and read the audit log |

## Core workflow

`upload -> background analysis -> curator review -> public`

A Member uploads a SWF with a title and a mandatory source and mandatory license. Uploads without source or license are rejected.

The upload creates a `SwfFile` (`ReviewStatus = Pending`) and an `AnalysisJob` (`Queued`). Re-uploading identical bytes (same SHA-256) never creates a second record.

A background worker runs the SWF parser, stores the autopsy report and the compatibility verdict, and marks the job `Succeeded` or `Failed`.

A Curator approves or rejects the file. Every decision is written to the audit log.

Approved files appear in the public museum. Anyone can open the Autopsy page and Resurrect the file with Ruffle, without logging in.

## Vocabulary

**ReviewStatus:** `Pending`, `Approved`, `Rejected`

**JobState:** `Queued`, `Running`, `Succeeded`, `Failed`

**Compatibility verdict:** `Resurrected` (expected to run), `Haunted` (runs with likely problems), `Beyond repair` (unsupported or unparseable)

## What is original work

The SWF parser, the autopsy report, the compatibility predictor, the review workflow and the museum UI. Ruffle is only the playback engine and must be credited. Never describe the project as "a Ruffle wrapper".

## Goals for any change

Keep the core flow working end to end: `upload -> analysis -> review -> resurrect`.

Prefer a small, polished feature over a large, half-finished one.

Never trust the client: every protected operation is authorized on the backend.