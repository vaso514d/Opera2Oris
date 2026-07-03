using System.Management;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;

namespace Opera2Oris.Licensing;

[SupportedOSPlatform("windows")]
public static class MachineFingerprint
{
    public static string Compute()
    {
        var cpuId = GetWmiValue("Win32_Processor", "ProcessorId");
        var boardSerial = GetWmiValue("Win32_BaseBoard", "SerialNumber");
        var diskSerial = GetWmiValue("Win32_DiskDrive", "SerialNumber");

        var raw = $"{cpuId}|{boardSerial}|{diskSerial}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));

        return Convert.ToBase64String(hash);
    }

    public static string GetDetails()
    {
        var cpuId = GetWmiValue("Win32_Processor", "ProcessorId");
        var boardSerial = GetWmiValue("Win32_BaseBoard", "SerialNumber");
        var diskSerial = GetWmiValue("Win32_DiskDrive", "SerialNumber");

        return $"CPU: {cpuId}\nBoard: {boardSerial}\nDisk: {diskSerial}";
    }

    private static string GetWmiValue(string wmiClass, string propertyName)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher($"SELECT {propertyName} FROM {wmiClass}");
            foreach (var obj in searcher.Get())
            {
                var value = obj[propertyName]?.ToString()?.Trim();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }
        }
        catch
        {
            // WMI not available — return empty
        }

        return string.Empty;
    }
}
