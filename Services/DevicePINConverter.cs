using System;
using System.Linq;

namespace ThirdpartyAPI.Services;

public static class DevicePINConverter
{
    public static string CSNtoDecimalPIN(string csn8hex)
    {
        return Convert.ToUInt32(csn8hex, 16).ToString();
    }

    public static string CSNtoDevicePIN(string csn8hex)
    {
        var rawBytes = new byte[4];
        for (int i = 0; i < 4; i++)
            rawBytes[i] = Convert.ToByte(csn8hex.Substring(i * 2, 2), 16);

        string binary32 = string.Join("", rawBytes.Select(b => Convert.ToString(b, 2).PadLeft(8, '0')));
        string upper16 = binary32[..16], lower16 = binary32[16..];
        int evenCount = upper16.Count(c => c == '1');
        int oddCount = lower16.Count(c => c == '1');
        char evenParity = evenCount % 2 == 0 ? '0' : '1';
        char oddParity = oddCount % 2 == 0 ? '1' : '0';

        string binary40 = $"{evenParity}{upper16}{lower16}{oddParity}111111";
        string hex10 = Binary40ToHex10(binary40);
        string wiegandCode = hex10.PadRight(48, 'F');

        return $"03{"22"}{wiegandCode}";
    }

    public static string DevicePINtoCSN(string devicePIN)
    {
        string hex10 = devicePIN.Substring(4, 10);
        var bytes = new byte[5];
        for (int i = 0; i < 5; i++)
            bytes[i] = Convert.ToByte(hex10.Substring(i * 2, 2), 16);

        string binary40 = "";
        foreach (byte b in bytes)
            for (int bit = 7; bit >= 0; bit--)
                binary40 += (b & (1 << bit)) != 0 ? "1" : "0";

        string binary32 = binary40[..34][1..];
        var resultBytes = new byte[4];
        for (int i = 0; i < 4; i++)
            resultBytes[i] = Convert.ToByte(binary32.Substring(i * 8, 8), 2);

        return BitConverter.ToString(resultBytes).Replace("-", "");
    }

    private static string Binary40ToHex10(string binary40)
    {
        var ca = binary40.Select(c => c.ToString()).ToArray();
        int nb = (int)Math.Floor((ca.Length - 1) / 8.0) + 1;
        var bytes = new byte[nb];
        byte cb = 0; int bc = 0, bi = 0;

        for (int i = ca.Length - 1; i >= 0; i--)
        {
            cb >>= 1;
            if (ca[i] == "1") cb |= 0x80;
            bc++;
            if (bc % 8 == 0) { bytes[bi++] = cb; cb = 0; }
        }
        if (bc % 8 != 0) cb >>= (8 - (bc % 8));
        if (bi < nb) bytes[bi] = cb;

        Array.Reverse(bytes);
        return string.Join("", bytes.Select(b => b.ToString("X2")));
    }
}
