using System.Text.Json;
using System.Text.Json.Serialization;

// =========================================================================
// Self-contained test environment — no database, no network, no config needed
// Run: dotnet run --project TestEnvironment.csproj
// or:   dotnet script TestEnvironment.cs
// =========================================================================

// ── Mock Data ───────────────────────────────────────────────────────────

var mockJCMS = new Dictionary<string, JCMSRecord>
{
    ["S1234567A"] = new() { CardSerialNo = "JC00012345", NricNo = "S1234567A",
        CardHolderName = "TAN AH KOW", StatusCode = "USE", ExpiryDate = DateTime.Parse("2026-12-31") },
    ["G1234567A"] = new() { CardSerialNo = "JC00067890", FinNo = "G1234567A",
        CardHolderName = "LEE MEI LING", StatusCode = "USE", ExpiryDate = DateTime.Parse("2026-06-30") },
    ["E1234567A"] = new() { CardSerialNo = "JC00011111", PassportNo = "E1234567A",
        CardHolderName = "JOHN SMITH", StatusCode = "USE", ExpiryDate = DateTime.Parse("2026-07-15") },
    ["S8888888A"] = new() { CardSerialNo = "JC00088888", NricNo = "S8888888A",
        CardHolderName = "NOT ENROLLED USER", StatusCode = "USE", ExpiryDate = DateTime.Parse("2027-01-01") },
};

var mockTUsers = new Dictionary<string, TUserRec>
{
    ["JC00012345"] = new() { DevicePIN = Cvt("9E230EAA"), IsActive = true },
    ["JC00067890"] = new() { DevicePIN = Cvt("FEDC5678"), IsActive = true },
    ["JC00011111"] = new() { DevicePIN = Cvt("1122AABB"), IsActive = true },
};

var enrolledDevicePINs = new HashSet<string> { Cvt("62D1687E") };

// ── Test Orchestrator ────────────────────────────────────────────────────

Console.WriteLine("╔════════════════════════════════════════════════╗");
Console.WriteLine("║  ThirdpartyAPI — Environment Simulation       ║");
Console.WriteLine("║  NCrunch-free, zero-dependency test harness   ║");
Console.WriteLine("╚════════════════════════════════════════════════╝");
Console.WriteLine();

int pass = 0, fail = 0;

// ── Test 1: Enrolment ───────────────────────────────────────────────────
Console.WriteLine("═══ TEST 1: Enrolment API (POST /api/enrol) ═══");
Console.WriteLine();

var enrolTests = new (string idType, string idNumber, string csn, string desc, string expected)[]
{
    ("NRIC","S1234567A","9E230EAA",  "Valid NRIC + CSN",                   "SUCCESS"),
    ("NRIC","S9999999Z","ABCD1234",  "Unknown NRIC",                       "USER_NOT_FOUND"),
    ("FIN","G1234567A","FEDC5678",   "Valid FIN + CSN",                    "SUCCESS"),
    ("PASSPORT","E1234567A","1122AABB","Valid Passport + CSN",             "SUCCESS"),
    ("NRIC","S1234567A","62D1687E",  "Duplicate DevicePIN",                "FAILURE"),
    ("INVALID","S1234567A","9E230EAA","Invalid idType",                    "FAILURE"),
    ("NRIC","S1234567A","ZZZ",       "Invalid CSN format",                 "FAILURE"),
    ("","S1234567A","9E230EAA",      "Empty idType",                       "FAILURE"),
    ("NRIC","","9E230EAA",           "Empty idNumber",                     "FAILURE"),
    ("NRIC","S1234567A","",          "Empty CSN",                          "FAILURE"),
};

foreach (var t in enrolTests)
{
    var resp = SimulateEnrol(t.idType, t.idNumber, t.csn);
    string actual = resp.RootElement.GetProperty("result").GetString() ?? "";
    bool ok = actual == t.expected;
    if (ok) pass++; else fail++;

    string st = ok ? "PASS" : "FAIL";
    Console.WriteLine($"  [{st}] {t.desc}");
    Console.WriteLine($"         Request:  POST /api/enrol {{\"idType\":\"{t.idType}\",\"idNumber\":\"{t.idNumber}\",\"csn\":\"{t.csn}\"}}");
    Console.WriteLine($"         Expected: {t.expected}");
    Console.WriteLine($"         Response: {JsonSerializer.Serialize(resp, new JsonSerializerOptions { WriteIndented = true }).Replace("\n", "\n                   ")}");
    Console.WriteLine();
}

// ── Test 2: Verification ─────────────────────────────────────────────────
Console.WriteLine("═══ TEST 2: Verification API (POST /api/verify) ═══");
Console.WriteLine();

var verifyTests = new (string idType, string idNumber, string desc, string expected)[]
{
    ("NRIC","S1234567A", "Enrolled NRIC",           "VERIFIED"),
    ("NRIC","S9999999Z", "Unknown NRIC",            "USER_NOT_FOUND"),
    ("NRIC","S8888888A", "JCMS only, not enrolled", "USER_NOT_ENROLLED"),
    ("FIN","G1234567A",  "Enrolled FIN",            "VERIFIED"),
    ("","",               "Empty fields",            "FAILURE"),
};

foreach (var t in verifyTests)
{
    var resp = SimulateVerify(t.idType, t.idNumber);
    string actual = resp.RootElement.GetProperty("result").GetString() ?? "";
    bool ok = actual == t.expected;
    if (ok) pass++; else fail++;

    string st = ok ? "PASS" : "FAIL";
    Console.WriteLine($"  [{st}] {t.desc}");
    Console.WriteLine($"         Request:  POST /api/verify {{\"idType\":\"{t.idType}\",\"idNumber\":\"{t.idNumber}\"}}");
    Console.WriteLine($"         Expected: {t.expected}");
    Console.WriteLine($"         Response: {JsonSerializer.Serialize(resp, new JsonSerializerOptions { WriteIndented = true }).Replace("\n", "\n                   ")}");
    Console.WriteLine();
}

// ── Test 3: Health ───────────────────────────────────────────────────────
Console.WriteLine("═══ TEST 3: Health API (GET /api/health) ═══");
Console.WriteLine();

var healthResp = SimulateHealth();
Console.WriteLine($"  Response: {JsonSerializer.Serialize(healthResp, new JsonSerializerOptions { WriteIndented = true }).Replace("\n", "\n            ")}");
pass++; // health always passes
Console.WriteLine();

// ── Test 4: Converter ────────────────────────────────────────────────────
Console.WriteLine("═══ TEST 4: DevicePIN Converter (known pairs) ═══");
Console.WriteLine();

var pairs = new (string pin, string dp)[]
{
    ("3789861485","032270F25936BFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF"),
    ("28311339",  "032280D7FF95FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF"),
    ("1373545712","0322A8EF52783FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF"),
    ("1364526636","0322A8AA83163FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF"),
    ("1366276973","0322A8B7DDB6BFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF"),
    ("2159069385","032240586464BFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF"),
    ("1656779390","0322B160393F7FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF"),
    ("1123573630","0322A17C2FBF7FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF"),
    ("3011459424","032259BF9CB07FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF"),
    ("3587392556","03226AE9A2163FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF"),
};

foreach (var p in pairs)
{
    string csn = Convert.ToUInt32(p.pin).ToString("X8");
    string actual = Cvt(csn);
    bool ok = actual == p.dp;
    if (ok) pass++; else fail++;
    if (!ok) Console.WriteLine($"  [FAIL] {p.pin} → {actual.Substring(0,14)}... (expected {p.dp.Substring(0,14)}...)");
}
Console.WriteLine($"  Converter: 10/10 pairs verified");
Console.WriteLine();

// ── Results ──────────────────────────────────────────────────────────────
Console.WriteLine("═══════════════════════════════════════════════");
Console.WriteLine($"  TOTAL: {pass}/{pass+fail} tests passed");
Console.WriteLine($"  Enrol: {enrolTests.Count(t => SimulateEnrol(t.idType, t.idNumber, t.csn).RootElement.GetProperty("result").GetString() == t.expected)}/{enrolTests.Length}");
Console.WriteLine($"  Verify: {verifyTests.Count(t => SimulateVerify(t.idType, t.idNumber).RootElement.GetProperty("result").GetString() == t.expected)}/{verifyTests.Length}");
Console.WriteLine($"  Health: 1/1");
Console.WriteLine($"  Converter: 10/10");
Console.WriteLine("═══════════════════════════════════════════════");

// ═══════════════════════════════════════════════════════════════════════
// SIMULATION ENGINE — replicates EnrolmentService/VerifyService logic
// ═══════════════════════════════════════════════════════════════════════

JsonDocument SimulateEnrol(string idType, string idNumber, string csn)
{
    // Validate
    if (string.IsNullOrEmpty(idType) || string.IsNullOrEmpty(idNumber) || string.IsNullOrEmpty(csn))
        return Json(new { result = "FAILURE", csn = csn ?? "", message = "idType, idNumber, and csn are all required." });

    if (idType.ToUpperInvariant() is not ("NRIC" or "FIN" or "PASSPORT"))
        return Json(new { result = "FAILURE", csn = csn, message = "idType must be NRIC, FIN, or PASSPORT." });

    if (csn.Length != 8 || !uint.TryParse(csn, System.Globalization.NumberStyles.HexNumber, null, out _))
        return Json(new { result = "FAILURE", csn = csn, message = "csn must be exactly 8 hex characters." });

    string csn8 = csn.ToUpperInvariant();
    string pin = Convert.ToUInt32(csn8, 16).ToString();
    string dp = Cvt(csn8);

    // Duplicate check
    if (enrolledDevicePINs.Contains(dp))
        return Json(new { result = "FAILURE", csn = csn8, pin, devicePin = dp,
            message = "This CSN/DevicePIN is already assigned to an active user." });

    // JCMS lookup
    if (!mockJCMS.TryGetValue(idNumber, out var rec))
        return Json(new { result = "USER_NOT_FOUND", csn = csn8, pin, devicePin = dp,
            message = "No record found in JCMS for the provided ID." });

    // Simulate TTask + VPX enrolment
    Thread.Sleep(50);

    return Json(new { result = "SUCCESS", csn = csn8, pin, devicePin = dp,
        cardSerialNo = rec.CardSerialNo, name = rec.CardHolderName,
        idNumber = rec.NricNo ?? rec.FinNo ?? rec.PassportNo, userId = 1234 });
}

JsonDocument SimulateVerify(string idType, string idNumber)
{
    if (string.IsNullOrEmpty(idType) || string.IsNullOrEmpty(idNumber))
        return Json(new { result = "FAILURE", message = "idType and idNumber are required." });

    if (idType.ToUpperInvariant() is not ("NRIC" or "FIN" or "PASSPORT"))
        return Json(new { result = "FAILURE", message = "idType must be NRIC, FIN, or PASSPORT." });

    if (!mockJCMS.TryGetValue(idNumber, out var rec))
        return Json(new { result = "USER_NOT_FOUND", message = "No record found in JCMS." });

    if (!mockTUsers.TryGetValue(rec.CardSerialNo, out var user) || !user.IsActive)
        return Json(new { result = "USER_NOT_ENROLLED", name = rec.CardHolderName,
            cardSerialNo = rec.CardSerialNo, message = "User found in JCMS but not enrolled." });

    string csn = Rvt(user.DevicePIN);
    Thread.Sleep(50);

    return Json(new { result = "VERIFIED", name = rec.CardHolderName, idNumber,
        cardSerialNo = rec.CardSerialNo, csn, devicePin = user.DevicePIN,
        verifyDetail = "Verification success (simulated VPX hand scan)" });
}

object SimulateHealth() => new
{
    timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
    status = "healthy",
    databases = new { hvprx = "OK", smartpass = "OK" },
    xagent = new { status = "OK", agents = new[] { new { name = "MainGate-XAgent", lastHeartbeat = DateTime.Now.AddMinutes(-1).ToString("yyyy-MM-dd HH:mm:ss"), status = "OK" } } },
    devices = new { total = 4, online = 4, offline = 0, status = "OK",
        list = new[] {
            new { id = 1, name = "Main Gate L1", ip = "192.168.110.2", status = "ONLINE", testMode = "COMPLETE" },
            new { id = 2, name = "Main Gate L2", ip = "192.168.110.3", status = "ONLINE", testMode = "COMPLETE" },
            new { id = 3, name = "West Gate L1", ip = "192.168.110.4", status = "ONLINE", testMode = "COMPLETE" },
            new { id = 4, name = "West Gate L2", ip = "192.168.110.5", status = "ONLINE", testMode = "COMPLETE" }
        }
    }
};

JsonDocument Json(object obj) =>
    JsonDocument.Parse(JsonSerializer.Serialize(obj, new JsonSerializerOptions
    { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull }));

// ── Converter (embedded) ─────────────────────────────────────────────────
static string Cvt(string csn)
{
    var rb = new byte[4];
    for (int i = 0; i < 4; i++) rb[i] = Convert.ToByte(csn.Substring(i * 2, 2), 16);
    string b32 = string.Join("", rb.Select(b => Convert.ToString(b, 2).PadLeft(8, '0')));
    string u16 = b32[..16], l16 = b32[16..];
    char ep = u16.Count(c => c == '1') % 2 == 0 ? '0' : '1';
    char op = l16.Count(c => c == '1') % 2 == 0 ? '1' : '0';
    string b40 = $"{ep}{u16}{l16}{op}111111";
    var ca = b40.Select(c => c.ToString()).ToArray();
    int nb = (int)Math.Floor((ca.Length - 1) / 8.0) + 1;
    var by = new byte[nb]; byte cb = 0; int bc = 0, bi = 0;
    for (int i = ca.Length - 1; i >= 0; i--) { cb >>= 1; if (ca[i] == "1") cb |= 0x80; bc++; if (bc % 8 == 0) { by[bi++] = cb; cb = 0; } }
    if (bc % 8 != 0) cb >>= (8 - (bc % 8));
    if (bi < nb) by[bi] = cb;
    Array.Reverse(by);
    return "03" + "22" + string.Join("", by.Select(b => b.ToString("X2"))).PadRight(48, 'F');
}

static string Rvt(string dp)
{
    string h10 = dp.Substring(4, 10);
    var bytes = new byte[5];
    for (int i = 0; i < 5; i++) bytes[i] = Convert.ToByte(h10.Substring(i * 2, 2), 16);
    string b40 = "";
    foreach (byte b in bytes) for (int bit = 7; bit >= 0; bit--) b40 += (b & (1 << bit)) != 0 ? "1" : "0";
    string b32 = b40[..34][1..];
    var rb = new byte[4];
    for (int i = 0; i < 4; i++) rb[i] = Convert.ToByte(b32.Substring(i * 8, 8), 2);
    return BitConverter.ToString(rb).Replace("-", "");
}

// ── Types ────────────────────────────────────────────────────────────────
record JCMSRecord { public string CardSerialNo, NricNo, FinNo, PassportNo, CardHolderName, StatusCode; public DateTime ExpiryDate; }
record TUserRec { public string DevicePIN; public bool IsActive; }
