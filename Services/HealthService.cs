#nullable disable warnings
using System;
using System.Collections.Generic;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace ThirdpartyAPI.Services;

public class HealthService
{
    private readonly string _hvprx, _smartPass;

    public HealthService(IConfiguration config)
    {
        _hvprx = config.GetConnectionString("HVPRX");
        _smartPass = config.GetConnectionString("SmartPass");
    }

    public object Check()
    {
        var db = new Dictionary<string, string>
        {
            ["hvprx"] = TestDb(_hvprx) ? "OK" : "ERROR",
            ["smartpass"] = TestDb(_smartPass) ? "OK" : "ERROR"
        };

        var xagent = CheckXAgent();
        var devices = CheckDevices();
        bool allOk = db["hvprx"] == "OK" && db["smartpass"] == "OK"
                     && (xagent["status"] as string) == "OK";

        return new
        {
            timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            status = allOk ? "healthy" : "degraded",
            databases = db,
            xagent,
            devices
        };
    }

    private bool TestDb(string cs)
    {
        try { using var c = new SqlConnection(cs); c.Open(); return true; }
        catch { return false; }
    }

    private Dictionary<string, object> CheckXAgent()
    {
        var r = new Dictionary<string, object>();
        try
        {
            using var conn = new SqlConnection(_hvprx);
            conn.Open();
            using var cmd = new SqlCommand(
                "SELECT Name, Timestamp, CASE WHEN DATEDIFF(MINUTE, Timestamp, GETDATE()) < 5 THEN 'OK' ELSE 'STALE' END AS Status FROM TAgent ORDER BY Id", conn);
            using var reader = cmd.ExecuteReader();
            var agents = new List<object>();
            bool anyOk = false;
            while (reader.Read())
            {
                string s = reader["Status"].ToString();
                if (s == "OK") anyOk = true;
                agents.Add(new { name = reader["Name"].ToString(), lastHeartbeat = reader["Timestamp"].ToString(), status = s });
            }
            r["status"] = agents.Count > 0 ? (anyOk ? "OK" : "STALE") : "ERROR";
            if (agents.Count == 0) r["message"] = "No XAgent registered";
            r["agents"] = agents;
        }
        catch (Exception ex) { r["status"] = "ERROR"; r["message"] = ex.Message; }
        return r;
    }

    private Dictionary<string, object> CheckDevices()
    {
        var r = new Dictionary<string, object>();
        try
        {
            using var conn = new SqlConnection(_hvprx);
            conn.Open();
            using var cmd = new SqlCommand(@"
                SELECT Id, Name, IP, MAC, Master,
                       CASE WHEN HeartBeat_Status = 1 THEN 'ONLINE' ELSE 'OFFLINE' END AS Status,
                       HeartBeat,
                       CASE TestMode WHEN 0 THEN 'TEST_MODE' WHEN 1 THEN 'RELEASED' ELSE 'COMPLETE' END AS TestModeText,
                       Master_Connect, TamperSW_Status, FireAlarm
                FROM TDevice ORDER BY Id", conn);
            using var reader = cmd.ExecuteReader();
            var list = new List<object>();
            int online = 0, offline = 0;
            while (reader.Read())
            {
                string s = reader["Status"].ToString();
                if (s == "ONLINE") online++; else offline++;
                list.Add(new
                {
                    id = reader["Id"], name = reader["Name"].ToString(), ip = reader["IP"].ToString(),
                    mac = reader["MAC"].ToString(), master = Convert.ToInt32(reader["Master"]) == 1,
                    status = s, lastHeartbeat = reader["HeartBeat"]?.ToString() ?? "",
                    testMode = reader["TestModeText"].ToString(),
                    masterConnected = Convert.ToInt32(reader["Master_Connect"]) == 1,
                    tamperSwitch = Convert.ToInt32(reader["TamperSW_Status"]) == 1 ? "CLOSED" : "OPEN",
                    fireAlarm = Convert.ToInt32(reader["FireAlarm"]) == 1
                });
            }
            r["total"] = list.Count;
            r["online"] = online;
            r["offline"] = offline;
            r["status"] = offline == 0 ? "OK" : "WARNING";
            r["list"] = list;
        }
        catch (Exception ex) { r["status"] = "ERROR"; r["message"] = ex.Message; }
        return r;
    }
}
