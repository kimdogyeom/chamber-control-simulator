# P7-T2 clean Release verification receipt

## Authority

- Repository: Windows `C:\Users\rlaeh\source\repos\chamber-control-simulator`
- Branch: `main`
- Frozen SHA: `2d2993338eae7bf193c78e3f5ccc8b05722e80bb`
- Git status before verification: clean (tracked)
- OS: Windows_NT 10.0.26200 (Windows 11 Pro)
- .NET SDK: `10.0.400`
- Configuration: Release

## Commands

```powershell
git status --short
git rev-parse HEAD
dotnet restore ChamberControlSimulator.slnx
dotnet build ChamberControlSimulator.slnx --configuration Release --no-restore
dotnet test ChamberControlSimulator.slnx --configuration Release --no-build --no-restore --verbosity minimal
```

## Results

- restore: PASS
- build: PASS, 0 warnings, 0 errors
- test: PASS, **190/190** (Abstractions 22, Core 44, Application 75, Presentation 29, Simulation 20)

If HEAD changes, this receipt is invalid.

## Nonclaims

App captures (P7-T4), README rewrite (P7-T3), v1.0 tag/push, Reset success, Modbus, real equipment, safety-rated.
