# Baseline v0.1 검증 영수증

## 상태

**완료 — build/test 기준선과 Windows Session 1의 초기 Idle 화면을 모두 확인했다.**

이 문서는 P0-T1 기준선 작업의 fresh 실행 결과를 기록한다. 자동 테스트와 GUI 증거는 아래에 명시된 동일한 검증 대상 SHA의 Debug output을 기준으로 한다.

## 실행 환경

| 항목 | 값 |
| --- | --- |
| 실행 시각 | 2026-08-15T17:31:16+09:00 |
| 장비 | `GYEOM-LAPTOP` |
| 사용자 | `GYEOM-LAPTOP\rlaeh` |
| OS | Microsoft Windows 11 Pro 10.0.26200, Build 26200 |
| RID | `win-x64` |
| .NET SDK | 10.0.400 |
| MSBuild | 18.9.6 |
| .NET Host | 10.0.11, x64 |
| 브랜치 | `main` |
| 검증 대상 SHA | `a4b9ff97f47f6213166e88da66e4791115932f88` |
| 검증 시작 전 worktree | clean |
| 검증 종료 후 worktree | clean |

## 실행 명령

```powershell
dotnet --info
dotnet restore ChamberControlSimulator.slnx
dotnet build ChamberControlSimulator.slnx --configuration Debug --no-restore --nologo
dotnet test ChamberControlSimulator.slnx --configuration Debug --no-build --no-restore --nologo
```

## 결과

### Restore

```text
복원할 모든 프로젝트가 최신 상태입니다.
```

### Debug build

```text
Build: PASS
Warnings: 0
Errors: 0
Elapsed: 00:00:04.54
```

확인된 출력 assembly:

- `ChamberControlSimulator.Core/bin/Debug/net10.0/ChamberControlSimulator.Core.dll`
- `ChamberControlSimulator/bin/Debug/net10.0-windows/ChamberControlSimulator.dll`
- `ChamberControlSimulator.Core.Tests/bin/Debug/net10.0/ChamberControlSimulator.Core.Tests.dll`
- `ChamberControlSimulator.Presentation.Tests/bin/Debug/net10.0-windows/ChamberControlSimulator.Presentation.Tests.dll`

### Tests

| Test project | Passed | Failed | Skipped | Duration |
| --- | ---: | ---: | ---: | ---: |
| `ChamberControlSimulator.Core.Tests` | 22 | 0 | 0 | 76 ms |
| `ChamberControlSimulator.Presentation.Tests` | 5 | 0 | 0 | 32 ms |
| **합계** | **27** | **0** | **0** | — |

```text
Test: PASS
Total: 27
Passed: 27
Failed: 0
Skipped: 0
```

## GUI runtime 확인

상태: **PASS**

- 확인 시각: 2026-08-15T19:11:46+09:00
- 실행 프로세스: `ChamberControlSimulator.exe`, Windows Session 1
- 실행 대상: Debug `net10.0-windows` output
- 오류 대화상자: 없음
- 개인정보가 포함된 전체 데스크톱 캡처는 저장하지 않고 앱 창 영역만 분리했다.

확인한 초기 상태:

```text
Controller State: IDLE
Equipment State: Idle
Recipe: Standard 250C
Current Temperature: 20.00 °C
Target Temperature: 250.00 °C
Progress Stage: Idle
Door: Closed
Sensor Feedback: Active
Active Alarm: None
Recovery Ready: No
Start: Enabled
Stop: Enabled
Acknowledge: Disabled
Reset: Disabled
Event / Alarm Log: Empty
```

앱 전용 증거:

- 이미지: [`images/baseline-v0.1-idle.png`](images/baseline-v0.1-idle.png)
- 크기: 643 × 682 PNG
- SHA-256: `8394c9ed1ce641a5685c0a9fa103ec3f80986a2af72b88be865acfe6f6d311b4`

## 현재 결론

- 소스 기준 Debug build와 27개 자동 테스트는 `a4b9ff97f47f6213166e88da66e4791115932f88`에서 fresh 통과했다.
- 동일 Debug output의 WinForms 초기 Idle 상태를 Windows Session 1에서 확인했다.
- build/test/runtime 증거가 모두 갖춰졌으므로 P0-T1은 완료다.
