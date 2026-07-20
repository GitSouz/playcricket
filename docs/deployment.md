# Deployment

The pipeline runs as a **scheduled Azure Container Apps Job**: on the 1st of
each month at 06:00 UTC it generates the previous month's fixture report PDFs
from Azure SQL, archives them to the file share, and uploads them to
Dotdigital. One container, no manual steps.

## One-off setup

1. **Azure Container Registry** (skip if one exists):
   `az acr create -g <rg> -n <acrName> --sku Basic`
2. **Container Apps environment** (skip if one exists):
   `az containerapp env create -g <rg> -n <envName>`
3. **Build the image**: `az acr build -r <acrName> -t playcricket-fixture-reports:latest .`
4. **Rotate the Dotdigital API credentials** (the old ones were exposed in the
   retired Logic App definition) and have the new values ready.
5. **Deploy the job**:

   ```bash
   az deployment group create -g <rg> -f infra/containerapp-job.bicep \
     -p acrName=<acrName> \
        environmentId=$(az containerapp env show -g <rg> -n <envName> --query id -o tsv) \
        sqlConnectionString='Server=tcp:<server>.database.windows.net;Database=PlaycricketV2;Authentication=Active Directory Default;' \
        archiveStorageConnection='<externallysharedfiles connection string>' \
        dotdigitalApiUser='<new api user>' \
        dotdigitalApiPassword='<new password>'
   ```

6. **Grant the job's managed identity access** (principal id is a deployment
   output): `AcrPull` on the registry, plus an Azure SQL user:

   ```sql
   CREATE USER [playcricket-fixture-reports] FROM EXTERNAL PROVIDER;
   ALTER ROLE db_datareader ADD MEMBER [playcricket-fixture-reports];
   ```

## Continuous deployment

`.github/workflows/ci.yml` builds and smoke-tests every push (generating the
sample PDFs as a build artifact). Setting the repo variables `ACR_NAME`,
`AZURE_RESOURCE_GROUP`, `ACA_JOB_NAME` and the `AZURE_CREDENTIALS` secret
enables the deploy stage, which rebuilds the image and points the job at it on
every push to `main`.

## Operations

- **Run for a specific month** (re-runs/backfill):
  `az containerapp job start -g <rg> -n playcricket-fixture-reports --args "--month","2026-04","--upload"`
- **Watch a run**: `az containerapp job execution list -g <rg> -n playcricket-fixture-reports -o table`
- **Logs**: Container Apps log stream / Log Analytics. The run ends with a
  `=== Run summary ===` block listing warnings (size outliers, the automated
  replacement for the manual file-size check) and failures; the job exits
  non-zero if any league failed, which surfaces as a failed job execution to
  alert on.
- **Alerting**: add an Azure Monitor alert on job execution failures
  (`Microsoft.App/jobs` — Failed executions metric) to email/Teams.

## Dotdigital notes

- The job finds or creates `FixtureReports{yyyy}/{Month}` in the Dotdigital
  document folders. Set `DOTDIGITAL_PARENT_FOLDER_ID` if the year folders live
  under a specific parent folder; otherwise the year folder is located by name
  anywhere in the tree (created at the root if missing).
- ⚠️ The folder-create endpoint (`POST /v2/document-folders/{parentId}`) and
  the folder-tree shape were written from API docs and the retired Logic App's
  upload call, but have not been exercised against a live account — verify on
  the first DEV run (see docs/PLAN.md Phase 3 parallel run).
