using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace EveFocus;

internal static class ReleaseSignature
{
    public const string PublicKey =
        "<RSAKeyValue><Modulus>uy7y3681kfDwVzx5lro0AqB6tt6XyyR30XWKFwUSdoGTmnFRD5/yX5pYUVobUSLNSyLrZ4OwxmcJeFykJZIxj5CvdimnbSNZ6irYON913GdIVWHv4VfLQHwpplPEC3X2E53GxjQkTwexawJvSvjjrIjHdnCZ9xX/6GiMq+NsWSFXwk/vtND4/1Y0R0og4r7mrm6LGd1MVx+ksrZYtOEf+M2OvJ6bv7OMuVTQpEA9yY98Vg5Q28/vDMDIMFQB2dE7eHv4GG1pT/kQ652ifoR1DRSHmwXC2S/jGR/SN5+fElmkFF7i+tQc0db6D2R6gh1gvN4J4XHmuxUqnEnjF/6KVdLmTXAlDxU/EbBYlKkDlv0xPOMa+325O2T9wKhB4wUAJigMx+wJDeZ7nioKZzDs8wFR5Nk4vTPwuuQsA4zfnqgGoj5MKjwomYcYbUbytSMmHzdCa08CK0iuqu8yGH5OKbRuPuuYwurV9yRnoXQ+HmcMyUAnkvjNgUCMPCuF2pDp</Modulus><Exponent>AQAB</Exponent></RSAKeyValue>";

    public static string Sha256Hex(string path)
    {
        using var sha = SHA256.Create();
        using var stream = File.OpenRead(path);
        var hex = new StringBuilder(64);
        foreach (byte b in sha.ComputeHash(stream)) hex.Append(b.ToString("x2"));
        return hex.ToString();
    }

    public static byte[] Message(string version, string fileName, string sha256Hex) =>
        Encoding.UTF8.GetBytes("EveFocus release\nversion=" + version + "\nfile=" + fileName + "\nsha256=" + sha256Hex + "\n");

    public static bool Verify(string version, string fileName, string sha256Hex, string signatureBase64)
    {
        try
        {
            using var rsa = new RSACng();
            rsa.FromXmlString(PublicKey);
            return rsa.VerifyData(Message(version, fileName, sha256Hex), Convert.FromBase64String(signatureBase64.Trim()),
                HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException)
        {
            return false;
        }
    }
}
