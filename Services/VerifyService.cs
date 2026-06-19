#nullable disable warnings
using System;
using Microsoft.Data.SqlClient;
using System.Threading;
using Microsoft.Extensions.Configuration;
using ThirdpartyAPI.Models;

namespace ThirdpartyAPI.Services;

public class VerifyService
{
    private readonly string _hvprx;
    private readonly int _vpxDeviceId;
    private readonly JCMSLookup _jcms;
    private const int CMD_USER_VERIFY = 161;
    private const int STATUS_COMPLETE = 3;

    public VerifyService(IConfiguration config)
    {
        _hvprx = config.GetConnectionString("HVPRX");
        _vpxDeviceId = int.Parse(config["VPX_DeviceId"] ?? "1");
        _jcms = new JCMSLookup(config);
    }

    public VerifyResponse Verify(VerifyRequest request)
    {
        if (string.IsNullOrEmpty(request.IdType) || string.IsNullOrEmpty(request.IdNumber))
            return VFail("idType and idNumber are required.");

        if (request.IdType.ToUpperInvariant() is not ("NRIC" or "FIN" or "PASSPORT"))
            return VFail("idType must be NRIC, FIN, or PASSPORT.");

        // JCMS lookup
        JCMSRecord rec;
        try { rec = _jcms.LookupById(request.IdType, request.IdNumber); }
        catch (Exception ex) { return VFail($"JCMS lookup error: {ex.Message}"); }

        if (rec == null)
            return new VerifyResponse { Result = "USER_NOT_FOUND", Message = "No record found in JCMS." };

        // Find TUser
        TUserRecord user = FindUserByCardSN(rec.CardSerialNo) ?? FindUserByIdNumber(request.IdNumber);
        if (user == null)
            return new VerifyResponse { Result = "USER_NOT_ENROLLED", CardSerialNo = rec.CardSerialNo,
                Name = rec.CardHolderName, Message = "User found in JCMS but not enrolled." };

        if (!user.IsActive || user.IsDeleted || user.EnableFlag == 0)
            return new VerifyResponse { Result = "USER_NOT_ENROLLED", CardSerialNo = rec.CardSerialNo,
                Name = rec.CardHolderName, Message = "User is inactive or disabled." };

        // Derive CSN
        string csn = "";
        try { csn = DevicePINConverter.DevicePINtoCSN(user.DevicePIN); } catch { }

        // TTask Type 161
        string inputData = $"0;{user.DevicePIN};";
        int taskId;
        try { taskId = InsertVerifyTask(inputData); }
        catch (Exception ex) { return new VerifyResponse { Result = "FAILURE", CardSerialNo = rec.CardSerialNo,
            Name = rec.CardHolderName, Csn = csn, DevicePin = user.DevicePIN, Message = ex.Message }; }

        if (!PollTask(taskId, out string result, out string output))
            return new VerifyResponse { Result = "NOT_VERIFIED", CardSerialNo = rec.CardSerialNo,
                Name = rec.CardHolderName, Csn = csn, DevicePin = user.DevicePIN,
                VerifyDetail = $"VPX returned: {result}", Message = "Verification failed." };

        string detail = GetVerificationDetail(user.DevicePIN);

        return new VerifyResponse { Result = "VERIFIED", Name = rec.CardHolderName, IdNumber = request.IdNumber,
            CardSerialNo = rec.CardSerialNo, Csn = csn, DevicePin = user.DevicePIN, VerifyDetail = detail };
    }

    private int InsertVerifyTask(string inputData)
    {
        using var conn = new SqlConnection(_hvprx);
        conn.Open();
        using var cmd = new SqlCommand(
            "INSERT INTO TTask (Type, Status, InputTime, InputData, Device_Id) VALUES (@Type, 0, GETDATE(), @Data, @Dev); SELECT SCOPE_IDENTITY();", conn);
        cmd.Parameters.AddWithValue("@Type", CMD_USER_VERIFY);
        cmd.Parameters.AddWithValue("@Data", inputData);
        cmd.Parameters.AddWithValue("@Dev", _vpxDeviceId);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private bool PollTask(int taskId, out string result, out string output)
    {
        result = ""; output = "";
        for (int i = 0; i < 60; i++)
        {
            Thread.Sleep(500);
            using var conn = new SqlConnection(_hvprx);
            conn.Open();
            using var cmd = new SqlCommand("SELECT Status, Result, OutputData FROM TTask WHERE Id = @Id", conn);
            cmd.Parameters.AddWithValue("@Id", taskId);
            using var r = cmd.ExecuteReader();
            if (r.Read())
            {
                int status = Convert.ToInt32(r["Status"]);
                if (status == STATUS_COMPLETE)
                {
                    result = r["Result"]?.ToString() ?? "";
                    output = r["OutputData"]?.ToString() ?? "";
                    return result == "0";
                }
                if (status == 2) { result = "CANCELLED"; return false; }
            }
        }
        result = "TIMEOUT";
        return false;
    }

    private TUserRecord FindUserByCardSN(string cardSN)
    {
        using var conn = new SqlConnection(_hvprx);
        conn.Open();
        using var cmd = new SqlCommand(@"
            SELECT TOP 1 U.Id, U.DevicePIN, U.PIN, U.Enable_Flag, U.DeleteDT
            FROM TUser U INNER JOIN TUser_Info UI ON UI.User_Id = U.Id
            WHERE UI.Card_SN = @sn AND U.DeleteDT IS NULL ORDER BY U.Id DESC", conn);
        cmd.Parameters.AddWithValue("@sn", cardSN);
        using var r = cmd.ExecuteReader();
        return r.Read() ? MapUser(r) : null;
    }

    private TUserRecord FindUserByIdNumber(string idNumber)
    {
        using var conn = new SqlConnection(_hvprx);
        conn.Open();
        using var cmd = new SqlCommand(@"
            SELECT TOP 1 U.Id, U.DevicePIN, U.PIN, U.Enable_Flag, U.DeleteDT
            FROM TUser U INNER JOIN TUser_Info UI ON UI.User_Id = U.Id
            WHERE UI.Number = @num AND U.DeleteDT IS NULL ORDER BY U.Id DESC", conn);
        cmd.Parameters.AddWithValue("@num", idNumber);
        using var r = cmd.ExecuteReader();
        return r.Read() ? MapUser(r) : null;
    }

    private TUserRecord MapUser(SqlDataReader r) => new()
    {
        UserId = Convert.ToInt32(r["Id"]),
        DevicePIN = r["DevicePIN"].ToString(),
        Pin = r["PIN"] == DBNull.Value ? "" : r["PIN"].ToString(),
        EnableFlag = Convert.ToInt32(r["Enable_Flag"]),
        IsDeleted = r["DeleteDT"] != DBNull.Value,
        IsActive = Convert.ToInt32(r["Enable_Flag"]) == 1
    };

    private string GetVerificationDetail(string dp)
    {
        try
        {
            using var conn = new SqlConnection(_hvprx);
            conn.Open();
            using var cmd = new SqlCommand(@"
                SELECT TOP 1 DataTypeDetail FROM TEvent_InOut
                WHERE DevicePIN = @dp ORDER BY Event_Id DESC", conn);
            cmd.Parameters.AddWithValue("@dp", dp);
            var r = cmd.ExecuteScalar();
            if (r != null)
                return Convert.ToInt32(r) switch
                {
                    0 => "Verification success", 1 => "Un-enrolled PIN or Card",
                    2 => "Hand input timeout", 4 => "Vascular mismatch",
                    5 => "Restricted by timezone", 7 => "Inactive user",
                    9 => "Expired", _ => $"Failed (code {r})"
                };
        }
        catch { }
        return "Result unavailable";
    }

    private VerifyResponse VFail(string msg) => new() { Result = "FAILURE", Message = msg };
}
