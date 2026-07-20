# Play Cricket — Monthly Fixture Reports

Automated replacement for the manual SSRS/SSIS monthly fixture report process:
generates the per-league PDF reports and (Phase 2) uploads them to Dotdigital.
See [docs/PLAN.md](docs/PLAN.md) for the full architecture and delivery plan.

## Project layout

```
src/PlayCricket.FixtureReports/   .NET 8 console app
  Models/          Report data model (headlines, divisions, watch lists)
  Data/            IReportDataSource + SampleDataSource (SQL source in Phase 2)
  Templates/       HTML report template (the SSRS RDL replacement)
  Assets/          Vendored Chart.js (reports render fully offline)
  Templating/      Scriban HTML builder
  Rendering/       Playwright (headless Chromium) HTML → PDF
```

## Running locally

Requires the .NET 8 SDK and a Chromium install (either `playwright install
chromium` or point `CHROMIUM_EXECUTABLE` at an existing binary).

```bash
cd src/PlayCricket.FixtureReports
dotnet run -- --month 2026-04 --sample --output ./output
```

| Flag | Meaning |
|---|---|
| `--month yyyy-MM` | Reporting month (defaults to previous calendar month) |
| `--sample` | Use built-in sample data — no database needed |
| `--output dir` | Where to write the PDFs (default `./output`) |
| `--upload` | Archive PDFs to the Azure file share and upload to Dotdigital |

Without `--sample` the Azure SQL connection string comes from
`PLAYCRICKET_SQL_CONNECTION`; `--upload` additionally needs
`ARCHIVE_STORAGE_CONNECTION`, `DOTDIGITAL_API_USER` and
`DOTDIGITAL_API_PASSWORD` (full list in `Program.cs`). Deployment as a
scheduled Azure Container Apps Job is described in
[docs/deployment.md](docs/deployment.md).

Output naming follows the existing convention:
`FixtureReport__{League_Name}_{LeagueId}_{yyyyMMdd}.pdf`.

## Editing the report layout

The report layout lives in `Templates/fixture-report.html` (+ `header.html`)
— plain HTML/CSS with [Scriban](https://github.com/scriban/scriban)
placeholders, charts drawn by Chart.js. Change the template, re-run with
`--sample`, and inspect the PDF; no code changes needed for layout tweaks.
