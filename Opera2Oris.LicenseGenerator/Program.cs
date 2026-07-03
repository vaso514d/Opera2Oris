using Opera2Oris.Licensing;

if (args.Length == 0)
{
    PrintHelp();
    return 0;
}

var command = args[0].ToLowerInvariant();

switch (command)
{
    case "generate-keys":
        return GenerateKeys(args);
    case "fingerprint":
        return ShowFingerprint();
    case "sign":
        return SignLicense(args);
    case "verify":
        return VerifyLicense(args);
    default:
        Console.Error.WriteLine($"Unknown command: {command}");
        PrintHelp();
        return 1;
}

static int GenerateKeys(string[] args)
{
    var outputDir = args.Length > 1 ? args[1] : Directory.GetCurrentDirectory();
    Directory.CreateDirectory(outputDir);

    var (publicKey, privateKey) = LicenseSigner.GenerateKeyPair();

    var publicKeyPath = Path.Combine(outputDir, "public.key");
    var privateKeyPath = Path.Combine(outputDir, "private.key");

    File.WriteAllText(publicKeyPath, publicKey);
    File.WriteAllText(privateKeyPath, privateKey);

    // Auto-generate LicenseKeys.cs in the Middlewear project if found
    var middlewearDir = FindMiddlewearProject(outputDir);
    if (middlewearDir is not null)
    {
        var licenseKeysPath = Path.Combine(middlewearDir, "LicenseKeys.cs");
        var escaped = publicKey.Replace("\"", "\\\"");
        File.WriteAllText(licenseKeysPath,
            $$"""
            namespace Opera2Oris.Middlewear;

            internal static class LicenseKeys
            {
                public const string PublicKey = "{{escaped}}";
            }
            """);
        Console.WriteLine($"  LicenseKeys.cs updated: {licenseKeysPath}");
    }

    Console.WriteLine($"Key pair generated:");
    Console.WriteLine($"  Public key:  {publicKeyPath}");
    Console.WriteLine($"  Private key: {privateKeyPath}");
    Console.WriteLine();
    Console.WriteLine("IMPORTANT: Keep private.key secret! Never distribute it.");
    if (middlewearDir is null)
    {
        Console.WriteLine("Middlewear project not found nearby. Copy public.key content into LicenseKeys.cs manually.");
    }
    else
    {
        Console.WriteLine("LicenseKeys.cs has been updated automatically. Rebuild the solution in Visual Studio.");
    }

    return 0;
}

static string? FindMiddlewearProject(string startDir)
{
    var dir = Path.GetFullPath(startDir);
    for (var i = 0; i < 5; i++)
    {
        var candidate = Path.Combine(dir, "Opera2Oris.Middlewear");
        if (Directory.Exists(candidate) && File.Exists(Path.Combine(candidate, "Opera2Oris.Middlewear.csproj")))
        {
            return candidate;
        }

        var parent = Directory.GetParent(dir)?.FullName;
        if (parent is null || parent == dir)
        {
            break;
        }

        dir = parent;
    }

    return null;
}

static int ShowFingerprint()
{
    Console.WriteLine("Machine hardware details:");
    Console.WriteLine(MachineFingerprint.GetDetails());
    Console.WriteLine();

    var fingerprint = MachineFingerprint.Compute();
    Console.WriteLine($"Fingerprint: {fingerprint}");

    return 0;
}

static int SignLicense(string[] args)
{
    if (args.Length < 2)
    {
        Console.Error.WriteLine("Usage: sign <private-key-path> [output-path]");
        return 1;
    }

    var privateKeyPath = args[1];
    if (!File.Exists(privateKeyPath))
    {
        Console.Error.WriteLine($"Private key file not found: {privateKeyPath}");
        return 1;
    }

    var outputPath = args.Length > 2 ? args[2] : "license.key";
    var privateKeyXml = File.ReadAllText(privateKeyPath);
    var fingerprint = MachineFingerprint.Compute();
    var licenseKey = LicenseSigner.Sign(fingerprint, privateKeyXml);

    File.WriteAllText(outputPath, licenseKey);

    Console.WriteLine($"License generated: {Path.GetFullPath(outputPath)}");
    Console.WriteLine($"Fingerprint: {fingerprint}");
    Console.WriteLine();
    Console.WriteLine("Place the license.key file next to Opera2Oris.Middlewear executable.");

    return 0;
}

static int VerifyLicense(string[] args)
{
    if (args.Length < 2)
    {
        Console.Error.WriteLine("Usage: verify <public-key-path> [license-path]");
        return 1;
    }

    var publicKeyPath = args[1];
    if (!File.Exists(publicKeyPath))
    {
        Console.Error.WriteLine($"Public key file not found: {publicKeyPath}");
        return 1;
    }

    var licensePath = args.Length > 2 ? args[2] : "license.key";
    if (!File.Exists(licensePath))
    {
        Console.Error.WriteLine($"License file not found: {licensePath}");
        return 1;
    }

    var publicKeyXml = File.ReadAllText(publicKeyPath);
    var licenseKey = File.ReadAllText(licensePath).Trim();
    var valid = LicenseValidator.Validate(licenseKey, publicKeyXml);

    if (valid)
    {
        Console.WriteLine("License is VALID for this machine.");
        return 0;
    }

    Console.Error.WriteLine("License is INVALID. It does not match this machine's hardware.");
    return 1;
}

static void PrintHelp()
{
    Console.WriteLine("Opera2Oris License Generator");
    Console.WriteLine();
    Console.WriteLine("Commands:");
    Console.WriteLine("  generate-keys [output-dir]              Generate RSA key pair (public.key + private.key)");
    Console.WriteLine("  fingerprint                             Show this machine's hardware fingerprint");
    Console.WriteLine("  sign <private-key-path> [output-path]   Generate license.key for this machine");
    Console.WriteLine("  verify <public-key-path> [license-path] Verify license.key against this machine");
    Console.WriteLine();
    Console.WriteLine("Workflow:");
    Console.WriteLine("  1. Run 'generate-keys' once — LicenseKeys.cs is updated automatically");
    Console.WriteLine("  2. Rebuild the solution in Visual Studio");
    Console.WriteLine("  3. On customer's machine, run 'sign <private-key-path>' to generate license.key");
    Console.WriteLine("  4. Place license.key next to Opera2Oris.Middlewear executable");
}
