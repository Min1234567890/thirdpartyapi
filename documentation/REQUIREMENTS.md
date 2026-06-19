# ThirdpartyAPI — Requirements & Specification

**Version**: 3.0 (.NET Core)
**Date**: 18 June 2026
**Status**: Implemented & Tested (26/26)

---

## 1. Background

Jurong Port SmartPass uses Techsphere VP-II X hand vascular pattern scanners at
gate lanes. The VPX communicates through a database command queue (TTask table)
managed by XAgent — there is no direct API to the hardware.

The ThirdpartyAPI replaces physical MIFARE cards with QR codes. A QR code provides
an 8-hex CSN. The API derives all VPX identifiers (PIN, DevicePIN) from this CSN
using the same Wiegand pipeline the card system uses, maintaining full backward
compatibility with gate lane hardware.

## 2. Reference Documents

| Document | Location |
|---|---|
| NetControl-X SDK Manual V2.0 | `SPS Documents and Files/NetControl-X SDK Manual V2.txt` |
| VP-II X User Manual | `JP_Techsphere/techsphere/Manual/` |
| DevicePIN Converter | `Others Tools/DevicePIN_To_CSN_Converter.vb` |

## 3. System Context

```
QR Scanner → ThirdpartyAPI (Kestrel/IIS) → TTask table → XAgent → VP-II X
```

## 4. Functional Requirements

### R1 — Enrolment (POST /api/enrol)

| # | Requirement | Status |
|---|---|---|
| R1.1 | Accept idType (NRIC/FIN/PASSPORT), idNumber, csn | ✓ |
| R1.2 | Validate CSN is 8 hex chars | ✓ |
| R1.3 | Derive decimal PIN from CSN | ✓ |
| R1.4 | Derive 52-char DevicePIN per SDK §1.4.2 | ✓ |
| R1.5 | Reject duplicate DevicePIN | ✓ |
| R1.6 | Look up cardholder in JCMS | ✓ |
| R1.7 | If not found → USER_NOT_FOUND (404) | ✓ |
| R1.8 | If multiple rows → take first | ✓ |
| R1.9 | TTask Type 159 (SDK §3.17) | ✓ |
| R1.10 | Poll TTask completion (30s timeout) | ✓ |
| R1.11 | XAgent creates TUser; API inserts TUser_Info | ✓ |
| R1.12 | Update JCMS CSN_No | ✓ |
| R1.13 | Log enrolment | ✓ |
| R1.14 | Return SUCCESS with full details | ✓ |

### R2 — Verification (POST /api/verify)

| # | Requirement | Status |
|---|---|---|
| R2.1 | Accept idType + idNumber | ✓ |
| R2.2 | JCMS lookup → USER_NOT_FOUND | ✓ |
| R2.3 | TUser lookup (by card_sn or Number) → USER_NOT_ENROLLED | ✓ |
| R2.4 | TTask Type 161 (SDK §3.19) | ✓ |
| R2.5 | Read TEvent_InOut for detail | ✓ |
| R2.6 | Return VERIFIED / NOT_VERIFIED | ✓ |

### R3 — Health (GET /api/health)

| # | Requirement | Status |
|---|---|---|
| R3.1 | HVPRX + SmartPass DB connectivity | ✓ |
| R3.2 | XAgent heartbeat (TAgent.Timestamp < 5 min) | ✓ |
| R3.3 | Per-device status from TDevice | ✓ |
| R3.4 | Aggregate: healthy / degraded | ✓ |

### R4 — CSN Backward Compatibility

| # | Requirement | Verified |
|---|---|---|
| R4.1 | CSN = 8 hex chars matching card system | ✓ 10 pairs |
| R4.2 | DevicePIN = 52 chars per SDK | ✓ |
| R4.3 | Deterministic — same CSN always same output | ✓ |
| R4.4 | Old VPX templates accessible | ✓ Same namespace |

## 5. Technical Stack

| Layer | Technology |
|---|---|
| Runtime | .NET 10 (ASP.NET Core) |
| Hosting | Kestrel (self-hosted) or IIS |
| Database | SQL Server (HVPRX + SmartPass) |
| JSON | System.Text.Json (built-in) |
| Config | appsettings.json |
| DI | Built-in Microsoft.Extensions.DependencyInjection |

## 6. Constraints

- Must not change gate lane code
- Must not change VPX firmware
- Must use existing XAgent
- Must produce card-compatible DevicePINs
- Must use existing HVPRX/SmartPass databases

## 7. Out of Scope

- Authentication (sandbox)
- Rate limiting
- TLS (internal network)
- Card reading/writing (QR code replaces)
- VPX firmware modification
- New databases

## 8. Test Coverage (26/26)

```
Enrolment:   10/10  (3 success + 7 correctly rejected)
Verification: 5/5   (2 verified + 3 correctly rejected)
Health:       1/1   (healthy state)
Converter:   10/10  (known pairs forward + reverse)
```
