# P7-T2 clean Release verification receipt

## Authority

- Repository: Windows `C:\Users\rlaeh\source\repos\chamber-control-simulator`
- Branch: `main`
- Source HEAD at verification: `f600f2a846193216c6ac5d2bf89bc11fb99a0f37`
- Git status before verification: clean tracked; `docs/image/` untracked operator staging
- OS: Windows_NT 10.0.26200 (Windows 11 Pro)
- .NET SDK: `10.0.400`
- Configuration: Release

Historical SHA `2d29933` remains the pre-Abort Release baseline (190/190). This receipt supersedes it for current HEAD.

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
- test: PASS, **209/209** (Abstractions 22, Core 47, Application 80, Presentation 33, Simulation 27)

If HEAD changes after this verification, re-run Release before claiming these totals.

## Nonclaims

v1.0 tag/push, Reset success, Modbus, real equipment, safety-rated, hardware E-Stop.
