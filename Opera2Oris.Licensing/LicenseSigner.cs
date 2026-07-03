using System.Security.Cryptography;
using System.Text;

namespace Opera2Oris.Licensing;

public static class LicenseSigner
{
    public static (string PublicKeyXml, string PrivateKeyXml) GenerateKeyPair(int keySize = 2048)
    {
        using var rsa = RSA.Create(keySize);
        return (rsa.ToXmlString(false), rsa.ToXmlString(true));
    }

    public static string Sign(string fingerprint, string privateKeyXml)
    {
        using var rsa = RSA.Create();
        rsa.FromXmlString(privateKeyXml);

        var data = Encoding.UTF8.GetBytes(fingerprint);
        var signature = rsa.SignData(data, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        return Convert.ToBase64String(signature);
    }
}
