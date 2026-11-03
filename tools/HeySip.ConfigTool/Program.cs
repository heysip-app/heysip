using System.Security.Cryptography;
using HeySip.Core.Features.Companion.Store;
using HeySip.Core.Features.Control;

namespace HeySip.ConfigTool;

/// <summary>
/// Makes and uses the keys that sign what the app trusts: the control file and the online list (catalog). Run it from your own
/// computer; private keys never go in a repository.
///   keygen  --out DIR [--names main,backup]    makes a key pair for each name (NAME.private.pem) and prints the public keys
///   sign    --key NAME.private.pem --file FILE [--kind control|catalog]
///             control (the default): writes control.sig next to the file;  catalog: writes the same name ending in .sig (catalog.json -> catalog.sig)
///   verify  --file FILE --keys PUBLICKEY[,PUBLICKEY] [--kind control|catalog]   checks the file against its signature and the app's reader
/// </summary>
internal static class Program
{
    public static int Main(string[] args)
    {
        try
        {
            var options = Options(args.Skip(1));
            var kind = options.GetValueOrDefault("kind", "control").ToLowerInvariant();
            if (kind is not ("control" or "catalog"))
            {
                throw new ArgumentException("--kind must be control or catalog");
            }

            return args.FirstOrDefault() switch
            {
                "keygen" => KeyGen(Need(options, "out"), options.GetValueOrDefault("names", "main,backup").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)),
                "sign" => Sign(Need(options, "key"), Need(options, "file"), kind),
                "verify" => Verify(Need(options, "file"), Need(options, "keys").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries), kind),
                _ => Usage(),
            };
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or CryptographicException or ArgumentException)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
    }

    private static int KeyGen(string directory, string[] names)
    {
        Directory.CreateDirectory(directory);
        foreach (var name in names)
        {
            var path = Path.Combine(directory, $"{name}.private.pem");
            if (File.Exists(path))
            {
                throw new IOException($"{path} already exists; it won't be overwritten.");
            }

            using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            File.WriteAllText(path, key.ExportPkcs8PrivateKeyPem());
            Console.WriteLine($"{name}.public = {Convert.ToBase64String(key.ExportSubjectPublicKeyInfo())}");
        }

        Console.WriteLine();
        Console.WriteLine("Private keys were written to " + directory + ". Move them to a password manager and an offline copy, and never commit them.");
        return 0;
    }

    private static int Sign(string keyFile, string file, string kind)
    {
        using var key = ECDsa.Create();
        key.ImportFromPem(File.ReadAllText(keyFile));
        var data = File.ReadAllBytes(file);
        if (!IsValid(data, kind, out var problem))
        {
            throw new ArgumentException($"The file isn't a valid {kind} file ({problem}); not signed.");
        }

        var signaturePath = SignaturePath(file, kind);
        File.WriteAllText(signaturePath, ControlVerifier.Sign(data, key) + "\n");
        Console.WriteLine("Wrote " + signaturePath);
        return 0;
    }

    private static int Verify(string file, string[] keys, string kind)
    {
        var data = File.ReadAllBytes(file);
        var signature = ControlVerifier.ReadSignature(File.ReadAllBytes(SignaturePath(file, kind)));
        var signed = signature is not null && ControlVerifier.Verify(data, signature, keys);
        var valid = IsValid(data, kind, out var problem);
        Console.WriteLine(signed ? "Signature: OK" : "Signature: DOES NOT MATCH any of the keys");
        Console.WriteLine(valid ? $"File: OK ({Describe(data, kind)})" : $"File: NOT a valid {kind} file ({problem})");
        return signed && valid ? 0 : 2;
    }

    private static bool IsValid(byte[] data, string kind, out string problem)
    {
        problem = "";
        if (kind == "control")
        {
            if (ControlParser.Parse(data, _ => true) is not null)
            {
                return true;
            }

            problem = "check formatVersion, issuedAt and the JSON";
            return false;
        }

        var result = StoreCatalog.Parse(data);
        problem = result.Error ?? "";
        return result.Ok;
    }

    private static string Describe(byte[] data, string kind) =>
        kind == "control"
            ? ControlParser.Parse(data, _ => true) is { } file ? $"{file.Notices.Count} notice(s), {file.Catalog.Count} list rule(s), issued {file.IssuedAt:u}" : ""
            : $"{StoreCatalog.Parse(data).Entries.Count} item(s)";

    /// <summary>control.json is signed in control.sig; any other file (the catalog) in the same name ending in .sig.</summary>
    private static string SignaturePath(string file, string kind) =>
        kind == "control"
            ? Path.Combine(Path.GetDirectoryName(Path.GetFullPath(file))!, "control.sig")
            : Path.ChangeExtension(Path.GetFullPath(file), ".sig");

    private static int Usage()
    {
        Console.Error.WriteLine("Usage: heysip-config keygen --out DIR [--names main,backup] | sign --key FILE --file FILE [--kind control|catalog] | verify --file FILE --keys PUBLIC[,PUBLIC] [--kind control|catalog]");
        return 1;
    }

    private static Dictionary<string, string> Options(IEnumerable<string> args)
    {
        var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var list = args.ToList();
        for (var i = 0; i + 1 < list.Count; i += 2)
        {
            options[list[i].TrimStart('-')] = list[i + 1];
        }

        return options;
    }

    private static string Need(Dictionary<string, string> options, string name) =>
        options.TryGetValue(name, out var value) ? value : throw new ArgumentException($"Missing --{name}");
}
