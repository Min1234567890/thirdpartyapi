#nullable disable warnings
using System;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.SqlClient;
using System.Threading;
using Microsoft.Extensions.Configuration;
using ThirdpartyAPI.Models;

namespace ThirdpartyAPI.Services;

public class EnrolmentService
{
    private readonly string _hvprx, _smartPass;
    private readonly int _vpxDeviceId;
    private readonly JCMSLookup _jcms;
    private const int CMD_USER_ENROLL = 159;
    private const int CMD_COPY_USER = 137;
    private const int STATUS_COMPLETE = 3;

    public EnrolmentService(IConfiguration config)
    {
        _hvprx = config.GetConnectionString("HVPRX");
        _smartPass = config.GetConnectionString("SmartPass");
        _vpxDeviceId = int.Parse(config["VPX_DeviceId"] ?? "1");
        _jcms = new JCMSLookup(config);
    }

    public EnrolmentResponse Enrol(EnrolmentRequest request)
    {
        // Validate input
        if (string.IsNullOrEmpty(request.IdType) || string.IsNullOrEmpty(request.IdNumber))
            return Fail("idType and idNumber are required.");

        if (request.IdType.ToUpperInvariant() is not ("NRIC" or "FIN" or "PASSPORT"))
            return Fail("idType must be NRIC, FIN, or PASSPORT.");

        // Look up ALL cards for this person (any status)
        List<JCMSRecord> allCards;
        try { allCards = _jcms.LookupAllCards(request.IdType, request.IdNumber); }
        catch (Exception ex) { return Fail($"JCMS lookup error: {ex.Message}"); }

        if (allCards.Count == 0)
            return new EnrolmentResponse { Result = "USER_NOT_FOUND",
                Message = "No record found in JCMS for the provided ID." };

        // Categorize cards
        var activeCards = allCards.FindAll(c =>
            c.StatusCode == "USE" || c.StatusCode == "SUS" || c.StatusCode == "SUSV");
        var enrolledCards = allCards.FindAll(c => !string.IsNullOrEmpty(c.CsnNo));

        // Find: active card NOT yet enrolled (target for enrolment)
        var targetCard = activeCards.Find(c => string.IsNullOrEmpty(c.CsnNo));

        if (targetCard == null)
        {
            // All active cards are already enrolled
            if (activeCards.Count > 0 && enrolledCards.Count > 0)
                return new EnrolmentResponse { Result = "ALREADY_ENROLLED",
                    Csn = enrolledCards[0].CsnNo, CardSerialNo = activeCards[0].CardSerialNo,
                    Name = activeCards[0].CardHolderName,
                    Message = "All active cards for this person are already enrolled." };
            return new EnrolmentResponse { Result = "USER_NOT_FOUND",
                Message = "No active unenrolled card found for this person." };
        }

        // Find: old enrolled card (source for CopyUser template transfer)
        var oldCard = enrolledCards.Find(c => c.CardSerialNo != targetCard.CardSerialNo);

        // Generate CSN + DevicePIN for the new card
        string newCsn = GenerateCSN(targetCard.CardSerialNo);
        string newDp = DevicePINConverter.CSNtoDevicePIN(newCsn);

        // Check DevicePIN not already in use
        if (IsDevicePinInUse(newDp))
            return new EnrolmentResponse { Result = "FAILURE", Csn = newCsn, DevicePin = newDp,
                CardSerialNo = targetCard.CardSerialNo, Name = targetCard.CardHolderName,
                Message = "DevicePIN collision — please retry." };

        int userId; string pin;
        string personName = targetCard.CardHolderName;
        string personId = targetCard.NricNo ?? targetCard.FinNo;

        if (oldCard != null)
        {
            // ── COPY USER FLOW: transfer vascular template from old to new card ──
            string oldCsn = oldCard.CsnNo;
            string oldDp = DevicePINConverter.CSNtoDevicePIN(oldCsn);
            string copyInput = $"{oldDp};{newDp};";

            int taskId;
            try { taskId = InsertTask(CMD_COPY_USER, copyInput, newCsn); }
            catch (Exception ex) { return Ctx(newCsn, newDp, targetCard,
                $"Failed to create CopyUser task: {ex.Message}"); }

            if (!PollTask(taskId, out string result, out string output))
                return Ctx(newCsn, newDp, targetCard,
                    $"CopyUser failed. Result: {result}. {output}");

            // Read TUser for the NEW DevicePIN (XAgent created it during copy)
            try { (userId, pin) = GetUserIdAndPin(newDp); }
            catch (Exception ex) { return Ctx(newCsn, newDp, targetCard,
                $"CopyUser completed but failed to read TUser: {ex.Message}"); }
        }
        else
        {
            // ── NEW USER ENROLMENT ──
            string validFrom = DateTime.Now.ToString("yyyyMMddHHmmss");
            string validTo = targetCard.ExpiryDate == DateTime.MaxValue ? ""
                : targetCard.ExpiryDate.ToString("yyyyMMddHHmmss");
            string inputData = $"{newDp};1;0;0;1;1;0;1;0;{validFrom};{validTo};;";

            int taskId;
            try { taskId = InsertTask(CMD_USER_ENROLL, inputData, newCsn); }
            catch (Exception ex) { return Ctx(newCsn, newDp, targetCard,
                $"Failed to create enrolment task: {ex.Message}"); }

            if (!PollTask(taskId, out string result, out string output))
                return Ctx(newCsn, newDp, targetCard,
                    $"VPX enrolment failed. Result: {result}. {output}");

            try { (userId, pin) = GetUserIdAndPin(newDp); }
            catch (Exception ex) { return Ctx(newCsn, newDp, targetCard,
                $"Enrolled but failed to read TUser: {ex.Message}"); }
        }

        // Post-enrolment: TUser_Info + JCMS update + log
        try { InsertUserInfo(userId, targetCard); } catch { }
        try { UpdateJCMS(targetCard.CardSerialNo, newCsn); } catch { }
        try { LogEnrolment(userId, newCsn, pin, newDp, targetCard); } catch { }

        return new EnrolmentResponse
        {
            Result = "SUCCESS", Csn = newCsn, Pin = pin, DevicePin = newDp,
            CardSerialNo = targetCard.CardSerialNo, Name = personName,
            IdNumber = personId, UserId = userId,
            Message = oldCard != null
                ? $"Vascular template copied from old card {oldCard.CardSerialNo}"
                : ""
        };
    }

    /// <summary>Generate deterministic CSN from JCMS card serial number.</summary>
    private static string GenerateCSN(string cardSerialNo)
    {
        using var sha = System.Security.Cryptography.SHA256.Create();
        var hash = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(cardSerialNo));
        uint val = BitConverter.ToUInt32(hash, 0);
        return val.ToString("X8");
    }

    private int InsertTask(int cmdType, string inputData, string csn)
    {
        using var conn = new SqlConnection(_hvprx);
        conn.Open();
        using var cmd = new SqlCommand(
            "INSERT INTO TTask (Type, Status, InputTime, InputData, Device_Id, CmdAccount) " +
            "VALUES (@Type, 0, GETDATE(), @Data, @Dev, @Cmd); SELECT SCOPE_IDENTITY();", conn);
        cmd.Parameters.AddWithValue("@Type", cmdType);
        cmd.Parameters.AddWithValue("@Data", inputData);
        cmd.Parameters.AddWithValue("@Dev", _vpxDeviceId);
        cmd.Parameters.AddWithValue("@Cmd", $"ThirdpartyAPI_{csn}");
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

    private bool IsDevicePinInUse(string dp)
    {
        using var conn = new SqlConnection(_hvprx);
        conn.Open();
        using var cmd = new SqlCommand("SELECT COUNT(1) FROM TUser WHERE DevicePIN = @dp AND DeleteDT IS NULL", conn);
        cmd.Parameters.AddWithValue("@dp", dp);
        return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
    }

    private (int userId, string pin) GetUserIdAndPin(string dp)
    {
        using var conn = new SqlConnection(_hvprx);
        conn.Open();
        using var cmd = new SqlCommand(
            "SELECT TOP 1 Id, PIN FROM TUser WHERE DevicePIN = @dp AND DeleteDT IS NULL ORDER BY Id DESC", conn);
        cmd.Parameters.AddWithValue("@dp", dp);
        using var r = cmd.ExecuteReader();
        if (r.Read())
            return (Convert.ToInt32(r["Id"]), r["PIN"]?.ToString() ?? "");
        throw new Exception("User not found in TUser after enrolment");
    }

    private void InsertUserInfo(int userId, JCMSRecord rec)
    {
        using var conn = new SqlConnection(_hvprx);
        conn.Open();
        using var cmd = new SqlCommand(
            "INSERT INTO TUser_Info (User_Id, Name, FirstName, Number, Modify_DateTime) VALUES (@uid, @n, @fn, @num, GETDATE())", conn);
        cmd.Parameters.AddWithValue("@uid", userId);
        cmd.Parameters.AddWithValue("@n", rec.CardHolderName);
        cmd.Parameters.AddWithValue("@fn", rec.CardHolderName);
        string num = !string.IsNullOrEmpty(rec.NricNo) && rec.NricNo != "-" ? rec.NricNo :
                     !string.IsNullOrEmpty(rec.FinNo) && rec.FinNo != "-" ? rec.FinNo : rec.PassportNo ?? "";
        cmd.Parameters.AddWithValue("@num", num);
        cmd.ExecuteNonQuery();
    }

    private void UpdateJCMS(string cardSN, string csn)
    {
        using var conn = new SqlConnection(_smartPass);
        conn.Open();
        using var cmd = new SqlCommand("UPDATE JC_CARDDTL SET CSN_No = @csn WHERE CARD_SERIALNO = @sn", conn);
        cmd.Parameters.AddWithValue("@csn", csn);
        cmd.Parameters.AddWithValue("@sn", cardSN);
        cmd.ExecuteNonQuery();
    }

    private void LogEnrolment(int userId, string csn, string pin, string dp, JCMSRecord rec)
    {
        using var conn = new SqlConnection(_smartPass);
        conn.Open();
        using var cmd = new SqlCommand(
            "INSERT INTO Enrolment_Log (Name, Number, Card_SN, EnrolDT, Executer, DevicePIN, PIN, Type) " +
            "VALUES (@n, @num, @sn, GETDATE(), 'ThirdpartyAPI', @dp, @pin, 'QR_CODE')", conn);
        cmd.Parameters.AddWithValue("@n", rec.CardHolderName);
        cmd.Parameters.AddWithValue("@num", rec.NricNo ?? rec.FinNo ?? "");
        cmd.Parameters.AddWithValue("@sn", rec.CardSerialNo);
        cmd.Parameters.AddWithValue("@dp", dp);
        cmd.Parameters.AddWithValue("@pin", pin);
        cmd.ExecuteNonQuery();
    }

    private EnrolmentResponse Fail(string msg) => new() { Result = "FAILURE", Message = msg };

    private EnrolmentResponse Ctx(string csn, string dp, JCMSRecord r, string msg) => new()
    { Result = "FAILURE", Csn = csn, DevicePin = dp,
      CardSerialNo = r?.CardSerialNo, Name = r?.CardHolderName, IdNumber = r?.NricNo, Message = msg };
}
