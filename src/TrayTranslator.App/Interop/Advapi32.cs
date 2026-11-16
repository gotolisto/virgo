using System.Runtime.InteropServices;

namespace TrayTranslator.App.Interop;

internal static partial class Advapi32
{
    public const uint TOKEN_QUERY = 0x0008;
    public const int TokenElevation = 20;

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool OpenProcessToken(nint processHandle, uint desiredAccess, out nint tokenHandle);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetTokenInformation(nint tokenHandle, int tokenInformationClass, out uint tokenInformation, uint tokenInformationLength, out uint returnLength);
}

internal static partial class ProcessInterop
{
    public const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

    [LibraryImport("kernel32.dll", SetLastError = true)]
    public static partial nint OpenProcess(uint desiredAccess, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, uint processId);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool CloseHandle(nint handle);

    [LibraryImport("kernel32.dll")]
    public static partial nint GetCurrentProcess();

    /// <summary>True when the process runs elevated (as administrator); null when that cannot be read.</summary>
    public static bool? IsElevated(nint processHandle)
    {
        if (!Advapi32.OpenProcessToken(processHandle, Advapi32.TOKEN_QUERY, out var token))
        {
            return null;
        }

        try
        {
            return Advapi32.GetTokenInformation(token, Advapi32.TokenElevation, out var elevated, sizeof(uint), out _)
                ? elevated != 0
                : null;
        }
        finally
        {
            CloseHandle(token);
        }
    }

    /// <summary>True when the process <paramref name="processId"/> runs elevated; null when unknown.</summary>
    public static bool? IsElevated(uint processId)
    {
        var handle = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, processId);
        if (handle == 0)
        {
            return null;
        }

        try
        {
            return IsElevated(handle);
        }
        finally
        {
            CloseHandle(handle);
        }
    }
}
