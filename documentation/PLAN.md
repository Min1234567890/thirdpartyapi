# ThirdpartyAPI — Development Plan

**Version**: 3.0 (.NET Core)
**Date**: 18 June 2026
**Status**: Completed

---

## Context

Build a REST API that allows third-party enrolment stations to enrol pass holders
using QR codes instead of MIFARE cards. The API communicates with the VPX system
exclusively through the TTask command queue.

**Key decisions:**
- ASP.NET Core (.NET 10) with Kestrel hosting
- Zero external NuGet dependencies beyond System.Data.SqlClient
- Built-in System.Text.Json (no Newtonsoft)
- IConfiguration with appsettings.json
- Dependency injection for all services
- Self-hosted (Kestrel) with optional IIS deployment

## API Endpoints

| Method | Path | Purpose |
|---|---|---|
| `POST` | `/api/enrol` | Enrol pass holder by NRIC/FIN/Passport + CSN |
| `POST` | `/api/verify` | Trigger VPX hand vascular verification by ID |
| `GET` | `/api/health` | System health: DB + XAgent + VPX devices |

## Architecture

```
Program.cs (entry point)
  └── builder.Services.AddSingleton<Services>()
  └── builder.Services.AddControllers()
  └── app.MapControllers()
  └── app.Run()                          ← Kestrel on port 5000

Controllers/EnrolmentController.cs
  ├── POST /api/enrol  → EnrolmentService
  ├── POST /api/verify → VerifyService
  └── GET  /api/health → HealthService

Services/
  ├── DevicePINConverter.cs   (static — CSN ↔ PIN ↔ DevicePIN)
  ├── JCMSLookup.cs           (IConfiguration → SmartPass DB)
  ├── EnrolmentService.cs     (TTask 159, TUser_Info, JCMS update)
  ├── VerifyService.cs        (TTask 161, TEvent_InOut)
  └── HealthService.cs        (TAgent, TDevice)
```

## Development Phases

| Phase | Duration | Tasks |
|---|---|---|
| P1: Project setup | 0.5 day | dotnet new webapi, appsettings.json, DI registration |
| P2: DevicePIN converter | 0.5 day | Port verified VB.NET logic to C# |
| P3: JCMS lookup | 1 day | SQL query by NRIC/FIN/Passport |
| P4: Enrolment service | 2 days | Validation, TTask 159, poll, TUser_Info, JCMS, log |
| P5: Verify service | 1 day | TUser lookup, TTask 161, TEvent_InOut |
| P6: Health service | 0.5 day | DB, XAgent, Device checks |
| P7: Controller | 0.5 day | 3 endpoints, error handling |
| P8: Testing | 1 day | TestEnvironment.cs — 26 test scenarios |

**Total: ~7 days for 1-2 developers.**

## Key SDK References

| Item | SDK Section | Detail |
|---|---|---|
| DevicePIN format | §1.4.2 | 52 chars: PINType(2) + BitLength(2) + Code(48) |
| PIN is display-only | §1.4.3 | VPX uses DevicePIN as key |
| XAgent owns TUser | §2.4.1 | API must not INSERT TUser directly |
| UserEnroll command | §3.17 | Type 159, 12-field InputData |
| UserVerify command | §3.19 | Type 161, "FunctionKey;DevicePIN;" |
| TTask lifecycle | §2.2.2 | Status 0→1→3 |
| User deletion | §2.4.3 | UPDATE TUser SET DeleteDT = GETDATE() |

## Project Files

```
ThirdpartyAPI/
├── Program.cs                     ← Entry point (Kestrel)
├── appsettings.json               ← Connection strings
├── ThirdpartyAPI.csproj           ← SDK-style project (net10.0)
├── Controllers/
│   └── EnrolmentController.cs
├── Models/
│   └── EnrolmentModels.cs
├── Services/
│   ├── DevicePINConverter.cs
│   ├── JCMSLookup.cs
│   ├── EnrolmentService.cs
│   ├── VerifyService.cs
│   └── HealthService.cs
├── TestEnvironment.cs             ← 26-test harness (no DB needed)
├── _test/                         ← Test project (dotnet run)
└── documentation/                 ← All docs
```
