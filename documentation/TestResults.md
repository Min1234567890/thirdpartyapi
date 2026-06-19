# ThirdpartyAPI — Test Documentation

**Date**: 18 June 2026
**Framework**: .NET 10 / ASP.NET Core

---

## Two Test Applications

| Test | File | DB Required | What It Tests |
|---|---|---|---|
| **Fully Simulated** | `TestEnvironment.cs` | No | All API logic with mock data. 26 scenarios. |
| **Integration** | `_test/Program.cs` | Yes (Oracle + MSSQL) | Real DB queries. Only VPX/XAgent simulated. |

---

## Test 1: Fully Simulated (no DB needed)

Tests all API logic using in-memory mock JCMS data and simulated VPX responses.

### How to Run

```bash
cd ThirdpartyAPI/_test
# Replace Program.cs with the simulated test:
cp ../TestEnvironment.cs Program.cs
dotnet run
```

### What It Covers (26 scenarios)

```
═══ Enrolment: 10/10 ═══
  ✅ Valid NRIC + CSN           → SUCCESS
  ✅ Valid FIN + CSN            → SUCCESS
  ✅ Valid Passport + CSN       → SUCCESS
  ✅ Unknown NRIC               → USER_NOT_FOUND
  ✅ Duplicate DevicePIN        → FAILURE
  ✅ Invalid idType             → FAILURE
  ✅ Invalid CSN format         → FAILURE
  ✅ Empty idType               → FAILURE
  ✅ Empty idNumber             → FAILURE
  ✅ Empty CSN                  → FAILURE

═══ Verification: 5/5 ═══
  ✅ Enrolled NRIC              → VERIFIED
  ✅ Unknown NRIC               → USER_NOT_FOUND
  ✅ JCMS only (not enrolled)   → USER_NOT_ENROLLED
  ✅ Enrolled FIN               → VERIFIED
  ✅ Empty fields               → FAILURE

═══ Health: 1/1 ═══
  ✅ DB OK, XAgent OK, 4 devices online

═══ Converter: 10/10 ═══
  ✅ 10 known decimal↔DevicePIN pairs verified
```

---

## Test 2: Integration (real Oracle + MSSQL, simulated VPX)

Connects to your actual databases. Reads real JCMS card data from Oracle.
Reads real TUser/TAgent/TDevice from HVPRX. Writes real TUser/TUser_Info
records. **Only the VPX/XAgent response is simulated.**

### Prerequisites

- .NET 10 SDK
- Network access to `192.168.65.111` (MSSQL: HVPRX + SmartPass)
- Network access to `192.168.107.66` (Oracle UAT) or `192.168.65.136` (Oracle Live)
- Valid database credentials in `appsettings.json`

### Configuration

Edit `../appsettings.json` — the test reads from the parent directory:

```json
{
  "ConnectionStrings": {
    "HVPRX": "Data Source=192.168.65.111;Initial Catalog=HVPRX;User ID=TNCXS;Password=?ts2000nx!;...",
    "SmartPass": "Data Source=192.168.65.111;Initial Catalog=SmartPass;User ID=SmartPass;Password=ABCD1234$;...",
    "JCMS_UAT": "Oracle connection string for UAT...",
    "JCMS_Live": "Oracle connection string for Live..."
  },
  "JCMS_Environment": "UAT",
  "VPX_DeviceId": 1
}
```

### How to Run

```bash
cd ThirdpartyAPI/_test

# Ensure the integration test Program.cs is active:
# (should already be there — check it has "Integration Test — Real DB, Fake VPX" in the header)

dotnet restore
dotnet run
```

### What Happens

```
1. DB CONNECTIVITY CHECK
   ├── HVPRX (MSSQL)           → OK / FAILED
   ├── SmartPass (MSSQL)       → OK / FAILED
   └── JCMS (Oracle/UAT)       → OK / FAILED (falls back to SmartPass mirror)

2. HEALTH CHECK (real DB queries)
   ├── TAgent table → XAgent status + heartbeat
   └── TDevice table → per-device online/offline status

3. JCMS LOOKUP (real Oracle or SmartPass)
   └── Queries JC_CARDDTL by NRIC, FIN, Passport

4. ENROLMENT (real DB writes, fake VPX)
   ├── Finds unenrolled card in JCMS (CSN_No is NULL)
   ├── Generates random CSN → derives PIN + DevicePIN
   ├── Checks TUser for duplicate DevicePIN
   ├── INSERT INTO TUser (simulating XAgent after VPX enrolment)
   ├── INSERT INTO TUser_Info
   └── UPDATE JC_CARDDTL SET CSN_No

5. VERIFICATION (real DB writes, fake VPX)
   ├── Finds enrolled TUser
   ├── Derives CSN from DevicePIN
   ├── INSERT INTO TEvent + TEvent_InOut (simulating VPX scan result)
   └── Reads back verification result
```

### Switching Between Tests

The `_test/Program.cs` file determines which test runs:

| File Content | Test Type |
|---|---|
| `_test/Program.cs` = `TestEnvironment.cs` | Fully simulated (no DB) |
| `_test/Program.cs` = integration test (default) | Real DB, fake VPX |

To switch:
```bash
cd ThirdpartyAPI/_test
cp ../TestEnvironment.cs Program.cs    # switch to simulated test
# OR
git checkout Program.cs                # switch back to integration test
```

### Expected Output (Integration Test)

```
═══════════════════════════════════════════
  Integration Test — Real DB, Fake VPX
  JCMS source: Oracle (UAT)
═══════════════════════════════════════════

HVPRX (MSSQL)... OK
SmartPass (MSSQL)... OK
JCMS (Oracle/UAT)... OK

═══ Test 1: Health (real DB) ═══
{ "status": "healthy", "databases": {...}, "xagent": {...}, "devices": {...} }

═══ Test 2: JCMS Lookup ═══
  NRIC S1234567A: FOUND → TAN AH KOW (JC00012345)
  NRIC S9999999Z: NOT FOUND
  FIN G1234567A: FOUND → LEE MEI LING (JC00067890)
  PASSPORT E1234567A: FOUND → JOHN SMITH (JC00011111)

═══ Test 3: Enrolment (real DB, fake VPX) ═══
  [PASS] Found: JC00012345 (TAN AH KOW)
  [PASS] CSN=7F3A2B1C PIN=2134567890
  [PASS] DevicePIN not in use
  [PASS] VPX: TUser.Id=1234
  [PASS] TUser_Info insert
  [PASS] JCMS CSN update
  [PASS] ENROLMENT COMPLETE

═══ Test 4: Verification (real DB, fake VPX) ═══
  [PASS] User: TAN AH KOW
  [PASS] CSN: 7F3A2B1C
  [PASS] VPX verification: Verification success (simulated)
```
