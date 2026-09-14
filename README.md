# SnapFuel

Phase 1 scaffolding for importing historical fuel-stop photos, grouping related images, and capturing fuel-stop details through a manual web workflow.

## Solution layout

- `FuelImport.Core` – domain models plus image grouping services for manual review.
- `FuelImport.Data` – EF Core `FuelImportDbContext` schema + migrations for source images, grouped fuel events, and review history.
- `FuelImport.Aws` – legacy AWS OCR integration kept only as a reference during the transition away from OCR.
- `FuelImport.Worker` – folder-first ingestion worker for image metadata extraction and heuristic image tagging.
- `FuelImport.Web` – manual-entry and export API endpoints with the browser UI.
- `FuelImport.Tests` – focused unit/integration tests for pairing, confidence, validation, and dedup idempotency.

## Run locally

```bash
dotnet restore SnapFuel.slnx
dotnet build SnapFuel.slnx
dotnet test FuelImport.Tests/FuelImport.Tests.csproj
```

### Worker

Configure `FuelImport.Worker/appsettings.json`:

- `Import:RootFolder` to your photo root path
- `Import:DryRun` to scan without persisting source images

Run:

```bash
dotnet run --project FuelImport.Worker
```

The worker processes the configured folder once and then exits. It saves source images with timestamps, any available GPS coordinates, and a lightweight filename-based image tag so the web app can group nearby images for manual entry.

### Manual entry and export API

```bash
dotnet run --project FuelImport.Web
```

Endpoints:

- `GET /api/events?needsReview=true|false`
- `GET /api/events/{id}`
- `POST /api/events/{id}/review`
- `GET /api/manual/groups`
- `POST /api/manual/groups/split`
- `POST /api/manual/groups/merge`
- `POST /api/manual/groups/save`
- `GET /api/images/{id}`
- `GET /api/vehicles`
- `GET /vehicles.html`
- `GET /api/events/export/csv`

If the worker ran with `Import:DryRun=false`, open the web UI to work through grouped image sets. Each group is built from nearby timestamps and matching GPS coordinates from EXIF when available. Saving a group creates or updates one fuel event linked to every image in that group.

## Notes

- This implementation is intentionally practical for personal use.
- OCR is no longer part of the primary workflow; manual entry is now the source of truth.
- It does not require training custom ML models.
