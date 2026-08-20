using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;

namespace DXNexus.LocalTransport;

public static class LocalPipeName
{
    private const string Prefix = "DXNexus.SDRSharp";

    public static string FromWindowsSid(string sid)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sid);
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(sid));
        return $"{Prefix}.{Convert.ToHexString(digest.AsSpan(0, 12)).ToLowerInvariant()}";
    }

    public static string ForCurrentWindowsUser()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("The SDRNexus local pipe is available only on Windows.");
        }

        using var identity = WindowsIdentity.GetCurrent();
        var sid = identity.User?.Value
            ?? throw new InvalidOperationException("The current Windows user has no security identifier.");
        return FromWindowsSid(sid);
    }
}

