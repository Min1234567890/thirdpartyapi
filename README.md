# ThirdpartyAPI

ASP.NET Core REST API for third-party pass holder enrolment into Jurong Port SmartPass.

## Quick Start

```bash
cd ThirdpartyAPI
dotnet run
# → http://localhost:5000
```

## Endpoints

| Method | Path | Purpose |
|---|---|---|
| `POST` | `/api/enrol` | Enrol pass holder by NRIC/FIN/Passport + CSN |
| `POST` | `/api/verify` | Trigger VPX hand vascular verification by ID |
| `GET` | `/api/health` | System health: databases, XAgent, VPX devices |

## Example

```bash
curl -X POST http://localhost:5000/api/enrol \
  -H "Content-Type: application/json" \
  -d '{"idType":"NRIC","idNumber":"S1234567A","csn":"9E230EAA"}'
```

## Configuration

Edit `appsettings.json`:
```json
{
  "ConnectionStrings": {
    "HVPRX": "Data Source=...;Initial Catalog=HVPRX;...",
    "SmartPass": "Data Source=...;Initial Catalog=SmartPass;..."
  },
  "VPX_DeviceId": 1
}
```

## Test

```bash
cd _test
dotnet run        # 26/26 tests, no database needed
```

## Project Structure

```
ThirdpartyAPI/
├── Program.cs                 ← Entry point
├── appsettings.json           ← Config
├── Controllers/               ← API endpoints
├── Models/                    ← Request/response DTOs
├── Services/                  ← Business logic
├── TestEnvironment.cs         ← Test harness
├── _test/                     ← Test project
└── documentation/             ← Full documentation
    ├── API-Reference.md
    ├── REQUIREMENTS.md
    ├── PLAN.md
    └── TestResults.md
```

## Deploy to IIS

```bash
dotnet publish -c Release -o C:\inetpub\ThirdpartyAPI
```

IIS: Add Application → .NET CLR: No Managed Code → point to publish folder.

## Requirements

- .NET 10 SDK or runtime
- SQL Server with HVPRX + SmartPass databases
- XAgent service running
- VP-II X devices registered in TDevice
