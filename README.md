# SnapFuel

Phase 1 scaffolding for importing historical fuel-stop photos, extracting structured fuel data, validating with confidence scoring, and routing low-confidence records for review.

## Solution layout

- `FuelImport.Core` – domain models, pairing logic, confidence scoring, validation, and vehicle resolution contracts/services.
- `FuelImport.Data` – EF Core `FuelImportDbContext` schema + migrations for SourceImage, FuelEvent, OCR, validation, and review tables.
- `FuelImport.Aws` – Phase 1 AWS integration layer contracts for Textract AnalyzeExpense, Rekognition DetectText, and A2I routing.
- `FuelImport.Worker` – folder-first ingestion worker (metadata, classification, pairing, OCR, validation, persistence).
- `FuelImport.Web` – review/export API endpoints.
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
- `Import:DryRun` to analyze without persisting events
- `AwsVision:EnableAwsApis` to `true` to call Textract/Rekognition/A2I
- `AwsVision:Region` to your AWS region (for example `us-east-1`)
- `AwsVision:A2iFlowDefinitionArn` to your flow ARN (optional for review routing)

Run:

```bash
dotnet run --project FuelImport.Worker
```

### Review/export API

```bash
dotnet run --project FuelImport.Web
```

Endpoints:

- `GET /api/events?needsReview=true|false`
- `GET /api/events/{id}`
- `POST /api/events/{id}/review`
- `GET /api/events/export/csv`

## Notes

- This implementation is intentionally practical for personal use.
- It uses AWS managed APIs (Textract, Rekognition, A2I) and fallback heuristics.
- It does not require training custom ML models.
