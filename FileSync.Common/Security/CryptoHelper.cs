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
    
    // For hybrid encryption (AES session key) - implementation TBD based on need
}
