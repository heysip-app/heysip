using System.Security.Cryptography;

namespace HeySip.Core.Features.Control;

/// <summary>
/// Checks that a control file was signed with one of the keys built into the app. It uses ECDSA over P-256 with SHA-256; the
/// signature is the 64-byte r||s form. Where the file is hosted needs no trust, because only the key counts.
/// </summary>
public static class ControlVerifier
{
    public const int SignatureBytes = 64;

    public static bool Verify(ReadOnlySpan<byte> data, ReadOnlySpan<byte> signature, IEnumerable<string> publicKeys)
    {
        if (signature.Length != SignatureBytes)
        {
            return false;
        }

        foreach (var key in publicKeys)
        {
            try
            {
                using var ecdsa = ECDsa.Create();
                ecdsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(key), out _);
                if (ecdsa.VerifyData(data, signature, HashAlgorithmName.SHA256))
                {
                    return true;
                }
            }
            catch (Exception exception) when (exception is CryptographicException or FormatException or ArgumentException)
            {
                // A key in the app that can't be read just doesn't match; another key may.
            }
        }

        return false;
    }

    /// <summary>The signature as it is written in <c>control.sig</c>: base64 text.</summary>
    public static string Sign(ReadOnlySpan<byte> data, ECDsa privateKey) =>
        Convert.ToBase64String(privateKey.SignData(data, HashAlgorithmName.SHA256));

    /// <summary>The signature bytes from the text of <c>control.sig</c>, or null when it isn't valid base64.</summary>
    public static byte[]? ReadSignature(ReadOnlySpan<byte> file)
    {
        try
        {
            return Convert.FromBase64String(System.Text.Encoding.ASCII.GetString(file).Trim());
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
