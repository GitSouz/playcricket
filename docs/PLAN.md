# Play Cricket — Monthly Fixture Report Automation Plan

Replaces the manual 9-step SSRS/SSIS process with a single, fully automated,
scheduled pipeline that generates the monthly fixture report PDFs and uploads
them to Dotdigital. No SSRS, no SSIS, no SQL Agent, no manual Postman calls,
no per-month edits to stored procedures or Logic Apps.

## Current state (what we are replacing)

| Step today | Manual action | Replaced by |
|---|---|---|
| 1. SSIS package | Edit destination folder per month, redeploy | Path computed at runtime (`FixtureReports{yyyy}/{MM-MMMM}`) |
| 2–4. SQL Agent `PlaycricketV2_SSRS_Refresh` + SSRS render | Run job steps by hand, create month folder | Scheduled job: refresh proc + code-based PDF rendering |
| 5. Validate by eyeballing file sizes | Manual | Automated validation (counts + size outlier detection) with alerting |
| 6. Copy PDFs to Azure file share | Manual copy | Job writes directly to storage |
| 7. Create Dotdigital folder via Postman | Manual API call | Job calls Dotdigital API (`POST /v2/document-folders`) |
| 8. Edit `[dbo].[FixtureReportList]` per month | Manual proc edit | Report list derived by query with month parameter |
| 9. Edit folder ID in Logic App and run it | Manual | Job uploads documents directly (`POST /v2/document-folders/{id}/documents`); Logic App retired |

Constraints that shaped the design:

- The database now runs on **Azure SQL Database** — there is no SQL Agent and
  no SSRS/SSIS hosting, so the old approach cannot simply be lifted and shifted.
- Reports contain styled tables **and bar charts** (monthly + season-to-date,
  2-year comparison), one PDF per league (potentially hundreds per run).

## Proposed architecture

```
                    ┌─────────────────────────────────────────────┐
  monthly schedule  │  Azure Container Apps Job (.NET 8)          │
  (cron, 1st of ───▶│                                             │
   month)           │  1. Run data refresh proc (Azure SQL)       │
                    │  2. Query league list + report datasets     │
                    │  3. Render HTML template ──▶ headless       │
                    │     Chromium ──▶ PDF (one per league)       │
                    │  4. Write PDFs to Blob/File storage         │
                    │     (archive, computed monthly path)        │
                    │  5. Create Dotdigital month folder (API)    │
                    │  6. Upload each PDF to Dotdigital (API)     │
                    │  7. Validate + send summary/alert           │
                    └─────────────────────────────────────────────┘
             auth: managed identity ──▶ Azure SQL, Storage, Key Vault
             secrets: Dotdigital API creds in Key Vault
```

### Key technology choices

| Concern | Choice | Rationale |
|---|---|---|
| Compute | **Azure Container Apps Job** (scheduled trigger) | Native cron scheduling, no SQL Agent needed, no Function timeout limits (hundreds of PDFs can exceed Consumption-plan limits), supports headless Chromium in the image |
| Language | **.NET 8** console worker | Matches existing Microsoft/SQL estate and team skills |
| PDF rendering | **HTML template + Playwright (headless Chromium) print-to-PDF** | The report is tables + bar charts; Chart.js reproduces the charts faithfully, and layout lives in an HTML/CSS template that can be tweaked without touching rendering code. Closest workflow to editing an RDL |
| Charts | **Chart.js** inline in the template | Renders in Chromium during PDF print; no server-side image generation needed |
| Data access | `Microsoft.Data.SqlClient` + **Entra ID managed identity** auth to Azure SQL | No SQL passwords anywhere |
| Storage | Existing `externallysharedfiles` account, path `development/PlayCricket/FixtureReports{yyyy}/{month}` computed at runtime | Keeps the current archive convention; folder "creation" is implicit in blob/file naming |
| Dotdigital | Direct REST calls: `POST /v2/document-folders` (create month folder) then `POST /v2/document-folders/{folderId}/documents` (upload, base64 payload — same as today's Logic App) | Removes Postman step, Logic App, and the hardcoded folder ID |
| Secrets | **Azure Key Vault** via managed identity | Dotdigital API user/password currently sit in plaintext in the Logic App definition — must move and be rotated |
| Scheduling | Container Apps Job cron (e.g. `0 6 1 * *`) | One schedule, no human trigger |
| Alerting | Application Insights + email/Teams summary at end of run | Replaces manual file-size eyeballing |
| CI/CD | GitHub Actions in this repo → build image → push to ACR → update job | Repeatable deployments to DEV/PROD |

### Report generation detail

One PDF per league (as today, e.g.
`FixtureReport__Warwickshire_Cricket_Foundation_Women_and_Girls_Leagues_29287_20260511.pdf`),
each containing:

1. **Headlines** — reporting month vs. prior year: completed count and
   played/cancelled/abandoned/conceded/short-sided percentages, plus bar chart.
2. **Season to date** — same table + chart, cumulative.
3. **Divisional headlines** — month and season-to-date per division.
4. **Watch list #1** — clubs/teams with ≥2 cancellations in the month.
5. **Watch list #2** — clubs/teams with ≥2 conceded/short-sided games in the month.

Implementation:

- Port each SSRS dataset query into versioned SQL (stored procs or Dapper
  queries in the repo) parameterised by `@LeagueId`, `@ReportMonth`.
- Single Razor/Handlebars-style HTML template with the Play-Cricket branding;
  Playwright prints to A4 PDF with repeating page header.
- Filename convention preserved: `FixtureReport__{SanitisedLeagueName}_{LeagueId}_{yyyyMMdd}.pdf`
  (reusing the existing name-sanitising rules — `&`→`and`, `/`, `:`, `+`, `u0026` etc.).

### Data refresh

The old `SSRS.RefreshFixtureData` proc did two jobs: (a) copy fixture data
from PROD to DEV over a linked server, and (b) apply reporting cleanups (name
sanitisation, county renames, league ID remap 8205→15363, Northumberland
league rename). The PROD→DEV copy is **not needed going forward** — the job
reads the Azure SQL database directly.

The cleanup rules are still required, but rather than mutating `Fact.Fixture`
and `SSRS.League_Sites` in place each run, they move into the reporting layer:
either views the report queries select from, or inline in the dataset queries.
That makes the pipeline read-only against the source tables and removes the
truncate/reload step entirely.

### Validation & alerting (replaces step 5 eyeballing)

At end of run the job checks:

- PDF count == league count from the report list query; any render failures listed.
- File-size outliers: flag files below a minimum threshold or > N standard
  deviations from the run's median (the automated version of "check smallest/largest").
- Dotdigital upload responses all 2xx; retry with backoff on transient failures.
- Summary (leagues processed, failures, folder link) emailed/posted to Teams;
  non-zero exit + alert on failure so a bad run is impossible to miss.

## Security fixes (do these regardless of the rest)

1. **Rotate the Dotdigital API credentials now.** The current password is
   embedded in plaintext in the `PlayCricketDotmailerUploadFixtureReportsDEV`
   Logic App definition and has been shared around in exported JSON.
2. Store the new credentials in Key Vault; the job reads them via managed
   identity. No secrets in code, config or Logic Apps.
3. Use Entra ID (managed identity) auth for Azure SQL and Storage — remove
   connection-string passwords.

## Delivery phases

**Phase 0 — Confirm inputs (blocking questions below)**
Resolve data-source question, get DEV Azure SQL details, Dotdigital sandbox/
account confirmation, rotate creds.

**Phase 1 — Report generator core (biggest chunk)**
Port SSRS dataset queries; build HTML template + Chart.js; Playwright PDF
rendering; match the existing PDF pixel-close; unit/snapshot tests against a
known month's data. Deliverable: CLI run locally produces correct PDFs for a
given month.

**Phase 2 — Pipeline & integrations** *(code complete — deployment pending)*
Storage upload, Dotdigital folder-create + document upload with retries,
containerise, deploy as scheduled Container Apps Job to DEV, GitHub Actions
CI/CD. See docs/deployment.md.

**Phase 3 — Validation, alerting, parallel run**
Automated checks + notifications. Run one month in parallel with the old
process; diff outputs against SSRS PDFs.

**Phase 4 — Cutover & decommission**
Point at PROD, disable/delete: SSIS package, SQL Agent job (already gone with
Azure SQL), SSRS reports, `[dbo].[FixtureReportList]` manual edits, the Logic
App, and the Postman step. Update the runbook to "it runs itself; here's how to
re-run a month manually" (job supports an optional month parameter for reruns).

## Resolved decisions

1. **Data source:** the old refresh proc's truncate/reload was a PROD→DEV
   linked-server copy — not needed going forward; the job queries Azure SQL
   directly. Cleanup rules move into the reporting layer (see Data refresh).
2. **Report list:** the set of reports per run is whatever
   `[dbo].[FixtureReportList]` returns; the pipeline keeps that proc as the
   single source of truth (parameterised by month rather than edited monthly).
3. **Dotdigital folder structure:** `FixtureReports{yyyy}/{month}` — the job
   looks up (or creates) the `FixtureReports{yyyy}` parent folder, then creates
   the month folder under it and uploads there.
4. **Credential rotation:** Dotdigital API credentials to be rotated by the
   team; new credentials go straight into Key Vault. The old
   `PlayCricketDotmailerUploadFixtureReportsDEV` Logic App (which embeds the
   old password in its definition) should be deleted from the
   `RG-UKS-DEV-LOGICAPPS` resource group.

## Open questions (Phase 0)

1. Confirm target environments/subscriptions for DEV and PROD, and whether an
   Azure Container Registry already exists.
2. Any consumers of the file-share copies besides Dotdigital (i.e. must the
   `externallysharedfiles` archive be kept)?
