using System.Text.Json.Serialization;

namespace ThirdpartyAPI.Models;

public class EnrolmentRequest
{
    [JsonPropertyName("idType")]   public string IdType { get; set; } = "";
    [JsonPropertyName("idNumber")] public string IdNumber { get; set; } = "";
}

public class EnrolmentResponse
{
    [JsonPropertyName("result")]       public string Result { get; set; } = "";
    [JsonPropertyName("csn")]          public string Csn { get; set; } = "";
    [JsonPropertyName("pin")]          public string Pin { get; set; } = "";
    [JsonPropertyName("devicePin")]    public string DevicePin { get; set; } = "";
    [JsonPropertyName("name")]         public string Name { get; set; } = "";
    [JsonPropertyName("cardSerialNo")] public string CardSerialNo { get; set; } = "";
    [JsonPropertyName("idNumber")]     public string IdNumber { get; set; } = "";
    [JsonPropertyName("company")]      public string Company { get; set; } = "";
    [JsonPropertyName("userId")]       public int UserId { get; set; }
    [JsonPropertyName("message")]      public string Message { get; set; } = "";
}

public class VerifyRequest
{
    [JsonPropertyName("idType")]   public string IdType { get; set; } = "";
    [JsonPropertyName("idNumber")] public string IdNumber { get; set; } = "";
}

public class VerifyResponse
{
    [JsonPropertyName("result")]       public string Result { get; set; } = "";
    [JsonPropertyName("name")]         public string Name { get; set; } = "";
    [JsonPropertyName("idNumber")]     public string IdNumber { get; set; } = "";
    [JsonPropertyName("cardSerialNo")] public string CardSerialNo { get; set; } = "";
    [JsonPropertyName("csn")]          public string Csn { get; set; } = "";
    [JsonPropertyName("devicePin")]    public string DevicePin { get; set; } = "";
    [JsonPropertyName("verifyDetail")] public string VerifyDetail { get; set; } = "";
    [JsonPropertyName("message")]      public string Message { get; set; } = "";
}

public class JCMSRecord
{
    public string CardSerialNo { get; set; } = "";
    public string NricNo { get; set; } = "";
    public string FinNo { get; set; } = "";
    public string PassportNo { get; set; } = "";
    public string CardType { get; set; } = "";
    public string CardHolderName { get; set; } = "";
    public string StatusCode { get; set; } = "";
    public DateTime ExpiryDate { get; set; }
}

public class TUserRecord
{
    public int UserId { get; set; }
    public string DevicePIN { get; set; } = "";
    public string Pin { get; set; } = "";
    public bool IsActive { get; set; }
    public bool IsDeleted { get; set; }
    public int EnableFlag { get; set; }
}
