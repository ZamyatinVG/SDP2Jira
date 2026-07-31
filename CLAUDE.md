# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

A .NET 8 console app (single project, no test suite) that bridges Askona's ManageEngine ServiceDesk Plus (SDP) and Jira. It runs as a CLI tool invoked with different arguments for different jobs: creating Jira issues from SDP requests, and exporting worklog/time-tracking reports to Excel.

## Build

```
dotnet build SDP2Jira.csproj -c Debug
dotnet build SDP2Jira.csproj -c Release
```

The `.vscode/tasks.json` `build-debug`/`build-release` tasks wrap these. Output lands in `bin/<Configuration>/net8.0-windows/`.

There is no test project and no lint config — this is a small internal ops tool, not a library.

`ExportToExcel` (used by `-wl`/`-itp`) generates the workbook with `ClosedXML` (pure .NET, no Office/Excel installation or COM required) and then opens the saved file via `Process.Start(..., UseShellExecute = true)`, which launches whatever app is associated with `.xlsx` on the machine (normally Excel, if installed) — but nothing about the build or the export itself requires Excel to be present.

## Debugging in VS Code

`.vscode/launch.json` has two configurations, **SDP2Jira (Debug)** and **SDP2Jira (Release)**, each wired to its matching build task and to `bin/<Configuration>/net8.0-windows/SDP2Jira.exe`. Both prompt for the CLI args to run with (e.g. `-r 12345`, `-wl`, `-itp`) via an input box — there's no default mode, so you must supply one.

## Running

The compiled exe takes one primary mode as `args[0]`:

- `SDP2Jira.exe -r <request_id> [-proj <PROJECT_KEY>]` — pull one request from SDP and create a corresponding Jira issue (defaults to project `ERP`). This is the mode invoked in production, from `SDP2Jira_ERP.ps1`, which SDP calls as a webhook/business-rule action, passing a JSON payload with the request id.
- `SDP2Jira.exe -wl` — export a worklog report (time spent per person/task, RUN vs CHANGE vs TECH DEBT classification, linked ITP initiative) across projects ERP/AT/ITP to Excel.
- `SDP2Jira.exe -itp` — export a quarterly-style report specifically for ITP (initiative) issues, following linked epics/subtasks and pulling ITP-specific custom fields (effect, Galaktika estimate, coefficient, EBITDA).

All configuration (SDP server URL, technician API key, Jira server/login/password, AD domains, support-list names, worklog date range) lives in `App.config` under `appSettings`. **`App.config` in this repo contains live/production-looking credentials** — treat edits to it carefully and don't widen its exposure (e.g. don't echo its contents into logs, issues, or commit messages beyond what's already tracked).

Logging goes through NLog (`NLog.config`): info+ to `c:/Logs/SDP2Jira/nlog-all-<date>.log`, debug+ to console.

## Architecture

Everything lives in two classes at the repo root (no folder structure):

- **`SDP.cs`** — thin REST client for the SDP Plus API (`/api/v3/requests/...`), using `RestSharp` + `Newtonsoft.Json`. Static methods only (`GetRequest`, `GetNotes`, `GetNote`, `DowloadFile`, `CloseRequest`, `GetRequestUrl`). Deserializes into nested POCOs (`Request`, `Note`, `Response_status`, etc.) that mirror the SDP JSON shape, including SDP's own field naming (snake_case-ish: `Requester`, `Udf_fields`, `Content_url`).
- **`Program.cs`** — everything else: argument dispatch in `Main`, AD lookups, Jira issue creation, and the two Excel report builders. Uses `Atlassian.SDK` (`Atlassian.Jira`) for all Jira access via a single static `Jira` client (`jira.CreateRestClient(...)`).

Key flows to understand before changing `SyncRequest` (the `-r` path):

1. Fetch the SDP request (`SDP.GetRequest`), then resolve both the requester's and technician's Jira/Windows login by looking them up in Active Directory by email (`IsLoginExists`). Two LDAP domains are tried — `LDAP_ASKONA` first, falling back to `LDAP_PMT` — because Askona has two AD forests/holdings.
2. Duplicate check: query Jira for an existing issue with the same "Номер заявки SD" custom field (created after 2025-02-01); if found, skip creation and log an error instead of raising.
3. Create the Jira issue, mapping SDP fields to Jira fields/custom fields. Project `ERP` requires the "Business unit" UDF to be filled (`Udf_pick_615`) or creation aborts; issue `Type` differs by project (`10206` Analysis for ERP, `10301` Task otherwise) — these are hardcoded Jira type IDs specific to the Askona Jira instance.
4. SDP notes become Jira comments; SDP inline `<img>` attachments and file attachments are downloaded (`SDP.DowloadFile` into a local `files\` dir) and re-uploaded to the Jira issue, then deleted locally.
5. On success, the SDP request is closed via `SDP.CloseRequest` (hardcoded status id `303`).

Key flows for the report modes (`-wl` / `-itp`, in `GetWorkLog`/`ProcessIssue` and `GetWorkLogITP`/`ProcessIssueITP`): both pull issues via Jira JQL (`GetIssuesFromJqlAsync`), then fan out per-issue work across a fixed thread pool (`Parallel.ForEach`, `threadCount = 5`) to fetch worklogs/links/subtasks concurrently, aggregating rows into a shared `DataTable` under `lockObject`, then hand the `DataTable` to `ExportToExcel` (`ClosedXML.Worksheets.Add(DataTable, sheetName)`, which builds it as a native Excel Table — header row bold/centered and the autofilter come from the table style, not manual formatting; don't also call `SetAutoFilter()` on that range, it's already filtered and will throw). The workbook is saved under `reports\` (created next to the exe if missing) as `worklog_<timestamp>.xlsx`, then opened via `Process.Start(UseShellExecute = true)`.

`-wl` classifies each issue's effective type by walking up to its parent (subtask → parent) and mapping parent issue type → `CHANGE`/`TECH DEBT`/`RUN`, and separately tries to resolve a linked ITP initiative either directly or via the issue's Epic Link.

`-itp` walks outward from an ITP issue: linked non-ITP issues, epics linked to those, and subtasks of all of the above, summing worklogs across that whole set per ITP issue.

## Notes

- `ServiceDesk.sql` is a legacy SQL Server Agent job script (linked-server query against an Oracle "SERVICEDESK" system) from when this tool logged stats to a database; that DB logging was removed in v0.9.28 (see `CHANGELOG.md`) and the file is now historical/reference only.
- Custom field names/IDs (`"Номер заявки SD"`, `"Business unit"`, `"Epic Link"`, issue type ids `10206`/`10301`, status id `303`) are specific to the Askona Jira/SDP instances and will not resolve against a different Jira/SDP install.
- Follow `CHANGELOG.md` conventions (Keep a Changelog style, Russian entries) when making notable changes, and bump `<Version>` in `SDP2Jira.csproj` accordingly.
- Migrated from net6.0 to net8.0; `NLog`, `System.Configuration.ConfigurationManager`, and `System.DirectoryServices` were bumped to net8-era versions (5.x/8.0.0) at the same time. `Atlassian.SDK` and `RestSharp` were left as-is (still netstandard2.0, resolve fine under net8.0).
- `TargetFramework` is `net8.0-windows`, not plain `net8.0`, because `IsLoginExists` (AD lookup) uses `System.DirectoryServices` (`DirectoryEntry`/`DirectorySearcher`), which is Windows-only; the `-windows` TFM tells the platform-compatibility analyzer the whole app is Windows-only, silencing `CA1416` warnings instead of suppressing them. This doesn't require the Windows Desktop SDK/workload — it still builds with plain `dotnet build`.
- The `Microsoft.Office.Interop.Excel` COM reference was removed in favor of `ClosedXML`, specifically so the project builds with plain `dotnet build`/`dotnet run` — the COM reference's `ResolveComReference` MSBuild task only worked under the full-framework MSBuild bundled with Visual Studio, not the dotnet-CLI one. Don't reintroduce Excel Interop without re-checking this constraint.
