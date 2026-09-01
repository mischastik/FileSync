using System.Security.Cryptography;
using System.Text;

namespace FileSync.Common.Security;

public static class CryptoHelper
{
    // Generate RSA Keys (Public, Private)
    public static (string PublicKey, string PrivateKey) GenerateKeys()
    {
        using var rsa = RSA.Create(2048);
        return (Convert.ToBase64String(rsa.ExportRSAPublicKey()), 
                Convert.ToBase64String(rsa.ExportRSAPrivateKey()));
    }

    public static byte[] Encrypt(byte[] data, string publicKey)
    {
        using var rsa = RSA.Create();
        rsa.ImportRSAPublicKey(Convert.FromBase64String(publicKey), out _);
        return rsa.Encrypt(data, RSAEncryptionPadding.OaepSHA256);
    }

    public static byte[] Decrypt(byte[] data, string privateKey)
    {
        using var rsa = RSA.Create();
        rsa.ImportRSAPrivateKey(Convert.FromBase64String(privateKey), out _);
        return rsa.Decrypt(data, RSAEncryptionPadding.OaepSHA256);
    }

    public static byte[] GenerateRandomBytes(int length)
    {
        var bytes = new byte[length];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(bytes);
        return bytes;
    }

    public static byte[] EncryptAes(byte[] data, byte[] key, byte[] iv)
    {
        using var aes = Aes.Create();
        aes.Key = key;
        aes.IV = iv;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;

        using var encryptor = aes.CreateEncryptor();
        using var ms = new MemoryStream();
        using (var cs = new CryptoStream(ms, encryptor, CryptoStreamMode.Write))
        {
            cs.Write(data, 0, data.Length);
        }
        return ms.ToArray();
    }

    public static byte[] DecryptAes(byte[] data, byte[] key, byte[] iv)
    {
        using var aes = Aes.Create();
        aes.Key = key;
        aes.IV = iv;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;

        using var decryptor = aes.CreateDecryptor();
        using var ms = new MemoryStream(data);
        using var cs = new CryptoStream(ms, decryptor, CryptoStreamMode.Read);
        using var tempMs = new MemoryStream();
        cs.CopyTo(tempMs);
        return tempMs.ToArray();
    }
}
