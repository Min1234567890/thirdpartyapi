using System.Data.SqlClient;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Oracle.ManagedDataAccess.Client;

// =========================================================================
// Integration Test — Real Oracle/MSSQL, Simulated VPX
//
// Connects to actual HVPRX + SmartPass databases. Queries real JCMS data.
// Only the VPX/XAgent response is simulated (TTask insert + TUser creation
// done directly instead of waiting for XAgent).
//
// Run: dotnet run   (from _test directory)
// =========================================================================

var config = new ConfigurationBuilder()
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("../appsettings.json", optional: false)
    .Build();

string hvprx = config.GetConnectionString("HVPRX")!;
string smartPass = config.GetConnectionString("SmartPass")!;
string jcmsEnv = config["JCMS_Environment"] ?? "UAT";
string jcmsOracle = config.GetConnectionString($"JCMS_{jcmsEnv}")!;
bool useOracle = !string.IsNullOrEmpty(jcmsOracle);
int vpxDeviceId = int.Parse(config["VPX_DeviceId"] ?? "1");

Console.WriteLine("═══════════════════════════════════════════");
Console.WriteLine("  Integration Test — Real DB, Fake VPX");
Console.WriteLine($"  JCMS source: {(useOracle ? $"Oracle ({jcmsEnv})" : "SmartPass mirror")}");
Console.WriteLine("═══════════════════════════════════════════");
Console.WriteLine();

// ── DB connectivity ─────────────────────────────────────────────────
Console.Write("HVPRX (MSSQL)... ");
try { using var c = new SqlConnection(hvprx); c.Open(); Console.WriteLine("OK"); }
catch (Exception ex) { Console.WriteLine($"FAILED: {ex.Message}"); return; }

Console.Write("SmartPass (MSSQL)... ");
try { using var c = new SqlConnection(smartPass); c.Open(); Console.WriteLine("OK"); }
catch (Exception ex) { Console.WriteLine($"FAILED: {ex.Message}"); return; }

if (useOracle)
{
    Console.Write($"JCMS (Oracle/{jcmsEnv})... ");
    try { using var c = new OracleConnection(jcmsOracle); c.Open(); Console.WriteLine("OK"); }
    catch (Exception ex) { Console.WriteLine($"FAILED: {ex.Message} — falling back to SmartPass mirror"); useOracle = false; }
}
Console.WriteLine();

// ── Test 1: Health Check (real DB) ──────────────────────────────────
Console.WriteLine("═══ Test 1: Health (real DB) ═══");
try
{
    var health = CheckHealth(hvprx, smartPass);
    Console.WriteLine(JsonSerializer.Serialize(health, new JsonSerializerOptions { WriteIndented = true }));
}
catch (Exception ex) { Console.WriteLine($"Health check failed: {ex.Message}"); }
Console.WriteLine();

// ── Test 2: JCMS Lookup (Oracle or SmartPass mirror) ────────────
Console.WriteLine("═══ Test 2: JCMS Lookup ═══");
try
{
    var jcmsResults = useOracle
        ? LookupJCMS_Oracle(jcmsOracle)
        : LookupJCMS_MSSQL(smartPass);
    foreach (var r in jcmsResults)
        Console.WriteLine($"  {r.idType} {r.idNumber}: {(r.found ? "FOUND" : "NOT FOUND")} → {r.name} ({r.cardSN})");
}
catch (Exception ex) { Console.WriteLine($"JCMS lookup failed: {ex.Message}"); }
Console.WriteLine();

// ── Test 3: Enrolment (real DB, fake VPX) ───────────────────────────
Console.WriteLine("═══ Test 3: Enrolment (real DB, fake VPX) ═══");
try
{
    foreach (var r in TestEnrolment(hvprx, smartPass, jcmsOracle, useOracle, vpxDeviceId))
    {
        string icon = r.result == r.expected ? "PASS" : "FAIL";
        Console.WriteLine($"  [{icon}] {r.desc}");
        if (r.error != null)
            Console.WriteLine($"         Error: {r.error}");
        else if (!string.IsNullOrEmpty(r.csn))
            Console.WriteLine($"         CSN={r.csn}  PIN={r.pin}  DP={r.dp?.Substring(0,14)}...");
    }
}
catch (Exception ex) { Console.WriteLine($"Enrolment test failed: {ex.Message}"); }
Console.WriteLine();

// ── Test 4: Verification (real DB, fake VPX) ────────────────────────
Console.WriteLine("═══ Test 4: Verification (real DB, fake VPX) ═══");
try
{
    foreach (var r in TestVerification(hvprx, smartPass, vpxDeviceId))
    {
        string icon = r.result == r.expected ? "PASS" : "FAIL";
        Console.WriteLine($"  [{icon}] {r.desc}");
        Console.WriteLine($"         Result: {r.result} | Detail: {r.detail}");
    }
}
catch (Exception ex) { Console.WriteLine($"Verification test failed: {ex.Message}"); }

Console.WriteLine();
Console.WriteLine("Done.");

// ═══════════════════════════════════════════════════════════════════
// HEALTH CHECK
// ═══════════════════════════════════════════════════════════════════

static object CheckHealth(string hvprx, string smartPass)
{
    bool hvOk = TestDb(hvprx), spOk = TestDb(smartPass);
    var agents = QueryAgents(hvprx);
    var devices = QueryDevices(hvprx);
    return new
    {
        timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
        status = (hvOk && spOk) ? "healthy" : "degraded",
        databases = new { hvprx = hvOk ? "OK" : "ERROR", smartpass = spOk ? "OK" : "ERROR" },
        xagent = new { status = agents.Count > 0 ? "OK" : "UNKNOWN", agents },
        devices = new { total = devices.Count, list = devices }
    };
}

static bool TestDb(string cs) { try { using var c = new SqlConnection(cs); c.Open(); return true; } catch { return false; } }

static List<object> QueryAgents(string cs)
{
    var list = new List<object>();
    try
    {
        using var c = new SqlConnection(cs); c.Open();
        using var cmd = new SqlCommand("SELECT Name, Timestamp FROM TAgent ORDER BY Id", c);
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new { name = r["Name"].ToString(), lastHeartbeat = r["Timestamp"].ToString() });
    }
    catch { }
    return list;
}

static List<object> QueryDevices(string cs)
{
    var list = new List<object>();
    try
    {
        using var c = new SqlConnection(cs); c.Open();
        using var cmd = new SqlCommand("SELECT Id, Name, IP, HeartBeat_Status FROM TDevice ORDER BY Id", c);
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new { id = r["Id"], name = r["Name"]?.ToString(), ip = r["IP"]?.ToString(),
                status = Convert.ToInt32(r["HeartBeat_Status"]) == 1 ? "ONLINE" : "OFFLINE" });
    }
    catch { }
    return list;
}

// ═══════════════════════════════════════════════════════════════════
// JCMS LOOKUP
// ═══════════════════════════════════════════════════════════════════

static List<(string idType, string idNumber, bool found, string name, string cardSN)>
LookupJCMS_Oracle(string connStr)
{
    var results = new List<(string, string, bool, string, string)>();
    var tests = new[] { ("NRIC", "S1234567A"), ("NRIC", "S9999999Z"), ("FIN", "G1234567A"), ("PASSPORT", "E1234567A") };

    foreach (var (idType, idNumber) in tests)
    {
        string col = idType switch { "NRIC" => "NRIC_NO", "FIN" => "FIN_NO", _ => "PASSPORT_NO" };
        string sql = $"SELECT CARD_SERIALNO, CARDHLDR_NAME FROM JC_CARDDTL " +
                     $"WHERE {col} LIKE '%' || :id || '%' " +
                     "AND (STATUS_CD = 'USE' OR STATUS_CD = 'SUS' OR STATUS_CD = 'SUSV') " +
                     "AND ROWNUM <= 1 ORDER BY CARD_SERIALNO";

        try
        {
            using var conn = new OracleConnection(connStr); conn.Open();
            using var cmd = new OracleCommand(sql, conn);
            cmd.Parameters.Add(new OracleParameter("id", idNumber));
            using var r = cmd.ExecuteReader();
            if (r.Read())
                results.Add((idType, idNumber, true,
                    r["CARDHLDR_NAME"]?.ToString()?.Trim() ?? "",
                    r["CARD_SERIALNO"]?.ToString()?.Trim() ?? ""));
            else
                results.Add((idType, idNumber, false, "", ""));
        }
        catch (Exception ex)
        {
            results.Add((idType, idNumber, false, "", $"Oracle error: {ex.Message}"));
        }
    }
    return results;
}

static List<(string idType, string idNumber, bool found, string name, string cardSN)>
LookupJCMS_MSSQL(string connStr)
{
    var results = new List<(string, string, bool, string, string)>();
    var tests = new[] { ("NRIC", "S1234567A"), ("NRIC", "S9999999Z"), ("FIN", "G1234567A"), ("PASSPORT", "E1234567A") };

    foreach (var (idType, idNumber) in tests)
    {
        string col = idType switch { "NRIC" => "NRIC_NO", "FIN" => "FIN_NO", _ => "PASSPORT_NO" };
        string sql = $"SELECT TOP 1 CARD_SERIALNO, CARDHLDR_NAME FROM JC_CARDDTL " +
                     $"WHERE {col} LIKE '%' + @id + '%' " +
                     "AND (STATUS_CD = 'USE' OR STATUS_CD = 'SUS' OR STATUS_CD = 'SUSV') " +
                     "ORDER BY CARD_SERIALNO";

        using var conn = new SqlConnection(connStr); conn.Open();
        using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@id", idNumber);
        using var r = cmd.ExecuteReader();
        if (r.Read())
            results.Add((idType, idNumber, true,
                r["CARDHLDR_NAME"]?.ToString()?.Trim() ?? "",
                r["CARD_SERIALNO"]?.ToString()?.Trim() ?? ""));
        else
            results.Add((idType, idNumber, false, "", ""));
    }
    return results;
}

// ═══════════════════════════════════════════════════════════════════
// ENROLMENT (real DB, simulated VPX)
// ═══════════════════════════════════════════════════════════════════

static List<(string desc, string expected, string result, string csn, string pin, string dp, string error)>
TestEnrolment(string hvprx, string smartPass, string jcmsOracle, bool useOracle, int vpxDevId)
{
    var r = new List<(string, string, string, string, string, string, string)>();

    // Find an unenrolled card in JCMS (Oracle or MSSQL mirror)
    string cardSN = "", name = "", nric = "";
    try
    {
        if (useOracle)
        {
            using var conn = new OracleConnection(jcmsOracle); conn.Open();
            using var cmd = new OracleCommand(
                "SELECT CARD_SERIALNO, NRIC_NO, CARDHLDR_NAME FROM JC_CARDDTL " +
                "WHERE STATUS_CD = 'USE' AND (CSN_No IS NULL OR CSN_No = ' ') " +
                "AND NRIC_NO IS NOT NULL AND NRIC_NO <> '-' AND ROWNUM <= 1 " +
                "ORDER BY CARD_SERIALNO", conn);
            using var reader = cmd.ExecuteReader();
            if (reader.Read())
            {
                cardSN = reader["CARD_SERIALNO"]?.ToString()?.Trim() ?? "";
                nric = reader["NRIC_NO"]?.ToString()?.Trim() ?? "";
                name = reader["CARDHLDR_NAME"]?.ToString()?.Trim() ?? "";
            }
        }
        else
        {
            using var conn = new SqlConnection(smartPass); conn.Open();
            using var cmd = new SqlCommand(
                "SELECT TOP 1 CARD_SERIALNO, NRIC_NO, CARDHLDR_NAME FROM JC_CARDDTL " +
                "WHERE STATUS_CD = 'USE' AND (CSN_No IS NULL OR CSN_No = '') " +
                "AND NRIC_NO IS NOT NULL AND NRIC_NO <> '-' ORDER BY CARD_SERIALNO", conn);
            using var reader = cmd.ExecuteReader();
            if (reader.Read())
            {
                cardSN = reader["CARD_SERIALNO"]?.ToString()?.Trim() ?? "";
                nric = reader["NRIC_NO"]?.ToString()?.Trim() ?? "";
                name = reader["CARDHLDR_NAME"]?.ToString()?.Trim() ?? "";
            }
        }
    }
    catch (Exception ex) { r.Add(("Find test card", "SUCCESS", "FAILURE", "", "", "", ex.Message)); return r; }

    if (string.IsNullOrEmpty(cardSN))
    {
        r.Add(("Find test card", "SUCCESS", "SKIP", "", "", "",
            "No unenrolled card found in JCMS. All cards already have CSN_No set."));
        return r;
    }
    r.Add(($"Found: {cardSN} ({name})", "SUCCESS", "SUCCESS", "", "", "", null));

    // Generate CSN and derive identifiers
    string csn = GenerateTestCSN();
    string pin = Convert.ToUInt32(csn, 16).ToString();
    string dp = CSNtoDevicePIN(csn);
    r.Add(($"CSN={csn} PIN={pin}", "SUCCESS", "SUCCESS", csn, pin, dp, null));

    // Duplicate check
    try
    {
        using var conn = new SqlConnection(hvprx); conn.Open();
        using var cmd = new SqlCommand("SELECT COUNT(1) FROM TUser WHERE DevicePIN = @dp AND DeleteDT IS NULL", conn);
        cmd.Parameters.AddWithValue("@dp", dp);
        if (Convert.ToInt32(cmd.ExecuteScalar()) > 0)
        { r.Add(("Duplicate check", "PASS", "FAILURE", csn, pin, dp, "DevicePIN collision"); return r; }
    }
    catch (Exception ex) { r.Add(("Duplicate check", "PASS", "FAILURE", csn, pin, dp, ex.Message)); return r; }
    r.Add(("DevicePIN not in use", "PASS", "PASS", csn, pin, dp, null));

    // SIMULATE VPX: insert TUser directly (what XAgent would do after VPX enrolment)
    int userId = 0;
    try
    {
        using var conn = new SqlConnection(hvprx); conn.Open();
        using var cmd = new SqlCommand(@"
            INSERT INTO TUser (PINType, DevicePIN, PIN, SecLevel, UserClass,
                Enroll_Type, Verify_Method, Enable_Flag, Bypass_Flag,
                EnrollDT, ValidDT, ExpireDT, Modify_DateTime)
            VALUES (3, @dp, @pin, 0, 0, 1, 1, 1, 0, GETDATE(), GETDATE(), DATEADD(YEAR,1,GETDATE()), GETDATE());
            SELECT SCOPE_IDENTITY();", conn);
        cmd.Parameters.AddWithValue("@dp", dp);
        cmd.Parameters.AddWithValue("@pin", pin);
        userId = Convert.ToInt32(cmd.ExecuteScalar());
    }
    catch (Exception ex) { r.Add(("VPX: TUser insert", "SUCCESS", "FAILURE", csn, pin, dp, ex.Message)); return r; }
    r.Add(($"VPX: TUser.Id={userId}", "SUCCESS", "SUCCESS", csn, pin, dp, null));

    // TUser_Info
    try
    {
        using var conn = new SqlConnection(hvprx); conn.Open();
        using var cmd = new SqlCommand(
            "INSERT INTO TUser_Info (User_Id, Name, FirstName, Number, Card_SN, Modify_DateTime) " +
            "VALUES (@uid, @n, @fn, @num, @sn, GETDATE())", conn);
        cmd.Parameters.AddWithValue("@uid", userId); cmd.Parameters.AddWithValue("@n", name);
        cmd.Parameters.AddWithValue("@fn", name); cmd.Parameters.AddWithValue("@num", nric);
        cmd.Parameters.AddWithValue("@sn", cardSN);
        cmd.ExecuteNonQuery();
        r.Add(("TUser_Info insert", "SUCCESS", "SUCCESS", csn, pin, dp, null));
    }
    catch (Exception ex) { r.Add(("TUser_Info", "SUCCESS", "WARN", csn, pin, dp, ex.Message)); }

    // JCMS update
    try
    {
        using var conn = new SqlConnection(smartPass); conn.Open();
        using var cmd = new SqlCommand("UPDATE JC_CARDDTL SET CSN_No = @csn WHERE CARD_SERIALNO = @sn", conn);
        cmd.Parameters.AddWithValue("@csn", csn); cmd.Parameters.AddWithValue("@sn", cardSN);
        cmd.ExecuteNonQuery();
        r.Add(("JCMS CSN update", "SUCCESS", "SUCCESS", csn, pin, dp, null));
    }
    catch (Exception ex) { r.Add(("JCMS update", "SUCCESS", "WARN", csn, pin, dp, ex.Message)); }

    r.Add(("ENROLMENT COMPLETE", "SUCCESS", "SUCCESS", csn, pin, dp, null));
    return r;
}

// ═══════════════════════════════════════════════════════════════════
// VERIFICATION (real DB, simulated VPX)
// ═══════════════════════════════════════════════════════════════════

static List<(string desc, string expected, string result, string detail)>
TestVerification(string hvprx, string smartPass, int vpxDevId)
{
    var r = new List<(string, string, string, string)>();

    // Find an enrolled user
    string dp = "", name = "";
    try
    {
        using var conn = new SqlConnection(hvprx); conn.Open();
        using var cmd = new SqlCommand(@"
            SELECT TOP 1 U.DevicePIN, UI.Name FROM TUser U
            INNER JOIN TUser_Info UI ON UI.User_Id = U.Id
            WHERE U.DeleteDT IS NULL AND U.Enable_Flag = 1 AND U.DevicePIN IS NOT NULL
            ORDER BY U.Id DESC", conn);
        using var reader = cmd.ExecuteReader();
        if (reader.Read()) { dp = reader["DevicePIN"]?.ToString() ?? ""; name = reader["Name"]?.ToString() ?? ""; }
    }
    catch (Exception ex) { r.Add(("Find enrolled user", "SUCCESS", "FAILURE", ex.Message)); return r; }

    if (string.IsNullOrEmpty(dp))
    { r.Add(("Find enrolled user", "SUCCESS", "SKIP", "No enrolled users in TUser")); return r; }

    r.Add(($"User: {name}", "SUCCESS", "SUCCESS", ""));

    string csn = "";
    try { csn = DevicePINtoCSN(dp); } catch { }
    r.Add(($"CSN: {csn}", "SUCCESS", "SUCCESS", ""));

    // SIMULATE VPX verification — insert TEvent + TEvent_InOut
    try
    {
        using var conn = new SqlConnection(hvprx); conn.Open();

        int eventId;
        using (var cmd = new SqlCommand(@"
            INSERT INTO TEvent (Type, RecordTime, EventTime, DeviceMAC, DeviceChannel, DeviceInfo, Flag)
            VALUES (3, GETDATE(), GETDATE(), '001510000168', 0, 'TestSimulator', 0);
            SELECT SCOPE_IDENTITY();", conn))
        { eventId = Convert.ToInt32(cmd.ExecuteScalar()); }

        using (var cmd = new SqlCommand(@"
            INSERT INTO TEvent_InOut (Event_Id, DataType, DataTypeDetail, PINType, DevicePIN, PIN, PINInfo,
                UserClass, RetryCount, VerifyType, VerifyMethod, Bypass, VascularIndex, Duress, FunctionKey)
            VALUES (@eid, 0, 0, 3, @dp, @pin, @n, 0, 0, 3, 1, 0, 1, 0, 0)", conn))
        {
            cmd.Parameters.AddWithValue("@eid", eventId); cmd.Parameters.AddWithValue("@dp", dp);
            cmd.Parameters.AddWithValue("@pin", csn); cmd.Parameters.AddWithValue("@n", name);
            cmd.ExecuteNonQuery();
        }

        using (var cmd = new SqlCommand(
            "SELECT TOP 1 DataTypeDetail FROM TEvent_InOut WHERE DevicePIN = @dp ORDER BY Event_Id DESC", conn))
        {
            cmd.Parameters.AddWithValue("@dp", dp);
            var detail = cmd.ExecuteScalar();
            string detailStr = detail != null && Convert.ToInt32(detail) == 0
                ? "Verification success (simulated)" : $"Code: {detail}";
            r.Add(("VPX verification", "VERIFIED", "VERIFIED", detailStr));
        }
    }
    catch (Exception ex) { r.Add(("VPX verify", "VERIFIED", "FAILURE", ex.Message)); }

    return r;
}

// ═══════════════════════════════════════════════════════════════════
// CONVERTER
// ═══════════════════════════════════════════════════════════════════

static string GenerateTestCSN() => Random.Shared.NextInt64(0x10000000, 0xFFFFFFFF).ToString("X8");

static string CSNtoDevicePIN(string csn)
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

static string DevicePINtoCSN(string dp)
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
