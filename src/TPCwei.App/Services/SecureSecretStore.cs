using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace TPC.App.Services;

public sealed class SecureSecretStore
{
    private readonly string _directory;

    public SecureSecretStore(string? directory = null)
    {
        _directory = directory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "TPCwei",
            "secrets");
        Directory.CreateDirectory(_directory);
    }

    public async Task SaveSecretAsync(string name, string value, CancellationToken cancellationToken = default)
    {
        var safeName = MakeSafeFileName(name);
        var path = Path.Combine(_directory, safeName + ".dpapi");
        var plain = Encoding.UTF8.GetBytes(value ?? "");
        var protectedBytes = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? DpapiProtect(plain)
            : Encoding.UTF8.GetBytes("TPCWEI-UNPROTECTED:" + Convert.ToBase64String(plain));
        await File.WriteAllBytesAsync(path, protectedBytes, cancellationToken).ConfigureAwait(false);
    }

    public async Task<string?> LoadSecretAsync(string name, CancellationToken cancellationToken = default)
    {
        var safeName = MakeSafeFileName(name);
        var path = Path.Combine(_directory, safeName + ".dpapi");
        if (!File.Exists(path))
        {
            return null;
        }

        var protectedBytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        var plain = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? DpapiUnprotect(protectedBytes)
            : Convert.FromBase64String(Encoding.UTF8.GetString(protectedBytes).Replace("TPCWEI-UNPROTECTED:", "", StringComparison.Ordinal));
        return Encoding.UTF8.GetString(plain);
    }

    private static string MakeSafeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            builder.Append(invalid.Contains(ch) ? '_' : ch);
        }
        return builder.Length == 0 ? "secret" : builder.ToString();
    }

    private static byte[] DpapiProtect(byte[] data)
    {
        return Dpapi(data, protect: true);
    }

    private static byte[] DpapiUnprotect(byte[] data)
    {
        return Dpapi(data, protect: false);
    }

    private static byte[] Dpapi(byte[] data, bool protect)
    {
        var input = new DataBlob();
        var output = new DataBlob();
        input.cbData = data.Length;
        input.pbData = Marshal.AllocHGlobal(data.Length);
        Marshal.Copy(data, 0, input.pbData, data.Length);
        try
        {
            var ok = protect
                ? CryptProtectData(ref input, "TPCwei", IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0, out output)
                : CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0, out output);
            if (!ok)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            var result = new byte[output.cbData];
            Marshal.Copy(output.pbData, result, 0, output.cbData);
            return result;
        }
        finally
        {
            if (input.pbData != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(input.pbData);
            }
            if (output.pbData != IntPtr.Zero)
            {
                LocalFree(output.pbData);
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob
    {
        public int cbData;
        public IntPtr pbData;
    }

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptProtectData(
        ref DataBlob pDataIn,
        string? szDataDescr,
        IntPtr pOptionalEntropy,
        IntPtr pvReserved,
        IntPtr pPromptStruct,
        int dwFlags,
        out DataBlob pDataOut);

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptUnprotectData(
        ref DataBlob pDataIn,
        IntPtr ppszDataDescr,
        IntPtr pOptionalEntropy,
        IntPtr pvReserved,
        IntPtr pPromptStruct,
        int dwFlags,
        out DataBlob pDataOut);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr hMem);
}
