using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;

namespace Opera2Oris.Licensing;

public static class LicenseValidator
{
    [SupportedOSPlatform("windows")]
    public static bool Validate(string licenseKey, string publicKeyXml)
    {
        var fingerprint = MachineFingerprint.Compute();
        return Validate(licenseKey, fingerprint, publicKeyXml);
    }

    public static bool Validate(string licenseKey, string fingerprint, string publicKeyXml)
    {
        try
        {
            using var rsa = RSA.Create();
            rsa.FromXmlString(publicKeyXml);

            var data = Encoding.UTF8.GetBytes(fingerprint);
            var signature = Convert.FromBase64String(licenseKey);

            return rsa.VerifyData(data, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        }
        catch
        {
            return false;
        }
    }
}
