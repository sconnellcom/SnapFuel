# SnapFuel

Phase 1 scaffolding for importing historical fuel-stop photos, grouping related images, and capturing fuel-stop details through a manual web workflow.

## Solution layout

- `FuelImport.Core` – domain models, options, image metadata extraction, and grouping services for manual review.
- `FuelImport.Data` – EF Core `FuelImportDbContext` schema + migrations for source images, grouped fuel events, and review history.
- `FuelImport.Aws` – legacy AWS OCR integration kept only as a reference during the transition away from OCR.
- `FuelImport.Web` – manual-entry UI, folder scanning endpoint, export API endpoints, and single database management.
- `FuelImport.Tests` – focused unit/integration tests for pairing, confidence, validation, and dedup idempotency.

## Run locally

```bash
dotnet restore SnapFuel.slnx
dotnet build SnapFuel.slnx
dotnet test FuelImport.Tests/FuelImport.Tests.csproj
```

### Manual Entry & Photo Ingestion Web App

Configure `FuelImport.Web/appsettings.json`:

- `Import:RootFolder` to your photo root path (e.g. `C:\Users\steph\Downloads\SnapFuelPhotos`)
- `Import:DryRun` to scan without persisting source images

Run:

```bash
dotnet run --project FuelImport.Web
```

Open the web app in your browser (e.g., `http://localhost:5000` or `https://localhost:7001`).

Click **Scan Photos** in the header toolbar to scan your configured photo folder into the database on-demand.

Endpoints:

- `POST /api/import/scan`
- `GET /api/import/config`
- `GET /api/events?needsReview=true|false`
- `GET /api/events/{id}`
- `POST /api/events/{id}/review`
- `GET /api/manual/groups`
- `POST /api/manual/groups/split`
- `POST /api/manual/groups/merge`
- `POST /api/manual/groups/save`
- `GET /api/images/{id}`
- `GET /api/vehicles`
- `GET /api/events/report`
- `GET /api/events/log`
- `GET /vehicles.html`
- `GET /report.html`
- `GET /log.html`
- `GET /api/events/export/csv`

Open the web UI to work through grouped image sets. Each group is built from nearby timestamps and matching GPS coordinates from EXIF when available. Saving a group creates or updates one fuel event linked to every image in that group.

## Notes

- This implementation is intentionally practical for personal use.
- OCR is no longer part of the primary workflow; manual entry is now the source of truth.
- It does not require training custom ML models.
