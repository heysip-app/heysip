# HeySip

Desktop water-reminder companion built with Avalonia and .NET 10.

## Build

Requires the .NET 10 SDK (see `global.json`).

```bash
dotnet build HeySip.slnx -c Release
dotnet test HeySip.slnx -c Release
dotnet run --project src/HeySip.App
```

## Structure

- `src/HeySip.Core` – UI-free logic, one folder per feature
- `src/HeySip.App` – Avalonia app
- `tests/HeySip.Tests` – xUnit tests
- `scripts` – build and publish helpers

## License

[MIT](LICENSE)
