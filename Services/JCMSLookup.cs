#nullable disable warnings
using System;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using ThirdpartyAPI.Models;

namespace ThirdpartyAPI.Services;

public class JCMSLookup
{
    private readonly string _connStr;

    public JCMSLookup(IConfiguration config)
    {
        _connStr = config.GetConnectionString("SmartPass");
    }

    public JCMSRecord LookupById(string idType, string idNumber)
    {
        if (string.IsNullOrEmpty(idNumber)) return null;

        string column = idType.ToUpperInvariant() switch
        {
            "NRIC" => "NRIC_NO",
            "FIN" => "FIN_NO",
            "PASSPORT" => "PASSPORT_NO",
            _ => throw new ArgumentException("idType must be NRIC, FIN, or PASSPORT")
        };

        string sql = string.Format(@"
            SELECT TOP 1 CARD_SERIALNO, NRIC_NO, FIN_NO, PASSPORT_NO,
                   NEW_CARD_TYPE, CARDHLDR_NAME, STATUS_CD, EXPIRY_DT, CSN_No
            FROM JC_CARDDTL
            WHERE {0} LIKE '%' + @id + '%'
              AND (STATUS_CD = 'USE' OR STATUS_CD = 'SUS' OR STATUS_CD = 'SUSV')
            ORDER BY CARD_SERIALNO", column);

        using var conn = new SqlConnection(_connStr);
        conn.Open();
        using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@id", idNumber);
        using var reader = cmd.ExecuteReader();
        return reader.Read() ? MapRecord(reader) : null;
    }

    /// <summary>Look up ALL cards for an ID, regardless of status.</summary>
    public List<JCMSRecord> LookupAllCards(string idType, string idNumber)
    {
        var results = new List<JCMSRecord>();
        if (string.IsNullOrEmpty(idNumber)) return results;

        string column = idType.ToUpperInvariant() switch
        {
            "NRIC" => "NRIC_NO", "FIN" => "FIN_NO", "PASSPORT" => "PASSPORT_NO",
            _ => throw new ArgumentException("idType must be NRIC, FIN, or PASSPORT")
        };

        string sql = string.Format(@"
            SELECT CARD_SERIALNO, NRIC_NO, FIN_NO, PASSPORT_NO,
                   NEW_CARD_TYPE, CARDHLDR_NAME, STATUS_CD, EXPIRY_DT, CSN_No
            FROM JC_CARDDTL
            WHERE {0} LIKE '%' + @id + '%'
            ORDER BY CARD_SERIALNO", column);

        using var conn = new SqlConnection(_connStr); conn.Open();
        using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@id", idNumber);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            results.Add(MapRecord(reader));
        return results;
    }

    private JCMSRecord MapRecord(SqlDataReader reader)
    {
        return new JCMSRecord
        {
            CardSerialNo = reader["CARD_SERIALNO"].ToString()?.Trim() ?? "",
            NricNo = reader["NRIC_NO"] == DBNull.Value ? "" : reader["NRIC_NO"].ToString()?.Trim(),
            FinNo = reader["FIN_NO"] == DBNull.Value ? "" : reader["FIN_NO"].ToString()?.Trim(),
            PassportNo = reader["PASSPORT_NO"] == DBNull.Value ? "" : reader["PASSPORT_NO"].ToString()?.Trim(),
            CardType = reader["NEW_CARD_TYPE"] == DBNull.Value ? "" : reader["NEW_CARD_TYPE"].ToString()?.Trim(),
            CardHolderName = reader["CARDHLDR_NAME"] == DBNull.Value ? "" : reader["CARDHLDR_NAME"].ToString()?.Trim(),
            StatusCode = reader["STATUS_CD"] == DBNull.Value ? "" : reader["STATUS_CD"].ToString()?.Trim(),
            ExpiryDate = reader["EXPIRY_DT"] == DBNull.Value ? DateTime.MaxValue : Convert.ToDateTime(reader["EXPIRY_DT"]),
            CsnNo = reader["CSN_No"] == DBNull.Value ? "" : reader["CSN_No"].ToString()?.Trim() ?? ""
        };
    }
}
