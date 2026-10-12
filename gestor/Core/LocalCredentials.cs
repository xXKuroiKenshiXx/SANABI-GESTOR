using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace SanabiGestor.Core;

// Windows encrypts credentials for the current OS user. Linux settings are
// private to their owner (0600); no application-wide secret is distributed.
public static class LocalCredentials
{
    const string Prefix = "dpapi:";
    public static string? Protect(string? value) => string.IsNullOrEmpty(value) || !OperatingSystem.IsWindows()
        ? value : Prefix + Convert.ToBase64String(Transform(Encoding.UTF8.GetBytes(value), true));
    public static string? Unprotect(string? value)
    {
        if (value?.StartsWith(Prefix, StringComparison.Ordinal) != true) return value;
        if (!OperatingSystem.IsWindows()) return null;
        try { return Encoding.UTF8.GetString(Transform(Convert.FromBase64String(value[Prefix.Length..]), false)); }
        catch { return null; } // A different OS user must connect their own account.
    }
    [StructLayout(LayoutKind.Sequential)] struct Blob { public int Length; public IntPtr Data; }
    [DllImport("crypt32.dll", SetLastError=true, CharSet=CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool CryptProtectData(ref Blob input, string? description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, out Blob output);
    [DllImport("crypt32.dll", SetLastError=true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool CryptUnprotectData(ref Blob input, IntPtr description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, out Blob output);
    [DllImport("kernel32.dll")] static extern IntPtr LocalFree(IntPtr memory);
    static byte[] Transform(byte[] bytes, bool encrypt)
    {
        var input = new Blob { Length=bytes.Length, Data=Marshal.AllocHGlobal(bytes.Length) };
        Blob output = default;
        try
        {
            Marshal.Copy(bytes,0,input.Data,bytes.Length);
            var ok = encrypt ? CryptProtectData(ref input,"SANABI Gestor",IntPtr.Zero,IntPtr.Zero,IntPtr.Zero,1,out output)
                : CryptUnprotectData(ref input,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero,1,out output);
            if (!ok) throw new Win32Exception(Marshal.GetLastWin32Error());
            var result = new byte[output.Length];
            Marshal.Copy(output.Data,result,0,result.Length);
            return result;
        }
        finally
        {
            Marshal.Copy(new byte[bytes.Length],0,input.Data,bytes.Length);
            Marshal.FreeHGlobal(input.Data);
            if (output.Data != IntPtr.Zero) LocalFree(output.Data);
            Array.Clear(bytes);
        }
    }
}
