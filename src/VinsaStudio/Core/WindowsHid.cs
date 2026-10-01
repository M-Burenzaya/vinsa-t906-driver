using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32.SafeHandles;

namespace VinsaStudio.Core;

public sealed record HidTablet(string Path, string Product, ushort VendorId, ushort ProductId, ushort Version, ushort FeatureLength)
{
    public ushort UsagePage { get; init; }
    public ushort Usage { get; init; }
    public ushort InputLength { get; init; }
    public ushort OutputLength { get; init; }
}

/// <summary>Enumeration is read-only. Only ApplyMapping explicitly sends the three verified reports.</summary>
public static class WindowsHid
{
    private static readonly object WriteLock = new();
    private static long? lastWriteTimestamp;
    public static bool IsEligible(HidTablet device) => device.FeatureLength == 8
        && ((device.VendorId == 0x0B57 && device.ProductId == 0x902E) || (device.VendorId == 0x08F2 && device.ProductId == 0x6811))
        && Regex.IsMatch(device.Path, $@"(?:^|[#&])vid_{device.VendorId:x4}&pid_{device.ProductId:x4}&mi_0[12]&col01(?:[#&]|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static IReadOnlyList<HidTablet> Enumerate()
    {
        HidD_GetHidGuid(out var guid);
        var set = SetupDiGetClassDevsW(ref guid, null, IntPtr.Zero, 0x12);
        if (set == new IntPtr(-1)) throw Error();
        var tablets = new List<HidTablet>();
        try
        {
            for (uint index = 0; ; index++)
            {
                var data = new InterfaceData { Size = (uint)Marshal.SizeOf<InterfaceData>() };
                if (!SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref guid, index, ref data))
                {
                    if (Marshal.GetLastWin32Error() == 259) break;
                    throw Error();
                }
                bool queried = SetupDiGetDeviceInterfaceDetailW(set, ref data, IntPtr.Zero, 0, out uint size, IntPtr.Zero);
                if (queried || Marshal.GetLastWin32Error() != 122) throw Error();
                var detail = Marshal.AllocHGlobal(checked((int)size));
                try
                {
                    Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6);
                    if (!SetupDiGetDeviceInterfaceDetailW(set, ref data, detail, size, out _, IntPtr.Zero)) throw Error();
                    string path = Marshal.PtrToStringUni(detail + 4) ?? throw new IOException("Empty HID device path.");
                    using var handle = Open(path);
                    if (handle.IsInvalid) continue;
                    try
                    {
                        var tablet = Inspect(handle, path);
                        if (IsEligible(tablet)) tablets.Add(tablet);
                    }
                    catch (Win32Exception) { /* Inaccessible/disconnected collection. */ }
                    catch (IOException) { /* Collection disappeared during enumeration. */ }
                }
                finally { Marshal.FreeHGlobal(detail); }
            }
        }
        finally { SetupDiDestroyDeviceInfoList(set); }
        return tablets;
    }

    public static void ApplyMapping(HidTablet device, TabletSettings settings)
    {
        if (!IsEligible(device)) throw new ArgumentException("Unsupported tablet identity or feature capability.", nameof(device));
        settings.Validate();
        lock (WriteLock)
        {
            using var handle = Open(device.Path);
            if (handle.IsInvalid) throw Error();
            var live = Inspect(handle, device.Path);
            if (!IsEligible(live) || live.VendorId != device.VendorId || live.ProductId != device.ProductId
                || live.Version != device.Version || live.FeatureLength != device.FeatureLength
                || live.UsagePage != device.UsagePage || live.Usage != device.Usage
                || live.InputLength != device.InputLength || live.OutputLength != device.OutputLength)
                throw new IOException("Live tablet identity or capabilities differ from enumeration.");
            var reports = FeatureProtocol.Plan(settings, live.Version);
            for (int i = 0; i < reports.Length; i++)
            {
                ValidateReport(live, reports[i]);
                if (lastWriteTimestamp is long previous)
                {
                    var remaining = TimeSpan.FromMilliseconds(300) - Stopwatch.GetElapsedTime(previous);
                    if (remaining > TimeSpan.Zero) Thread.Sleep((int)Math.Ceiling(remaining.TotalMilliseconds));
                }
                if (!HidD_SetFeature(handle, reports[i], reports[i].Length))
                    throw new IOException($"Mapping stopped after {i} of {reports.Length} reports; settings may be partially applied.", Error());
                lastWriteTimestamp = Stopwatch.GetTimestamp();
            }
        }
    }

    public static void ValidateReport(HidTablet device, byte[] report)
    {
        ArgumentNullException.ThrowIfNull(report);
        if (!IsEligible(device) || report.Length != 8 || report[0] != 8 || report[1] is not (1 or 3 or 6 or 7))
            throw new ArgumentException("Only reconstructed eight-byte feature report 08 commands 01/03/06/07 are allowed.");
    }

    private static SafeFileHandle Open(string path) => CreateFileW(path, 0, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
    private static Win32Exception Error() => new(Marshal.GetLastWin32Error());
    private static HidTablet Inspect(SafeFileHandle handle, string path)
    {
        var attributes = new Attributes { Size = (uint)Marshal.SizeOf<Attributes>() };
        if (!HidD_GetAttributes(handle, ref attributes)) throw Error();
        if (!HidD_GetPreparsedData(handle, out var preparsed)) throw Error();
        Caps caps;
        try
        {
            int status = HidP_GetCaps(preparsed, out caps);
            if (status != 0x00110000) throw new IOException($"HidP_GetCaps returned 0x{status:x8}.");
        }
        finally { HidD_FreePreparsedData(preparsed); }
        var name = new byte[252];
        string product = HidD_GetProductString(handle, name, name.Length)
            ? Encoding.Unicode.GetString(name).Split('\0')[0] : "";
        return new(path, product, attributes.VendorId, attributes.ProductId, attributes.Version, caps.FeatureLength)
        {
            UsagePage = caps.UsagePage, Usage = caps.Usage, InputLength = caps.InputLength, OutputLength = caps.OutputLength
        };
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct InterfaceData { public uint Size; public Guid ClassGuid; public uint Flags; public UIntPtr Reserved; }
    [StructLayout(LayoutKind.Sequential)]
    private struct Attributes { public uint Size; public ushort VendorId, ProductId, Version; }
    [StructLayout(LayoutKind.Sequential)]
    private struct Caps
    {
        public ushort Usage, UsagePage, InputLength, OutputLength, FeatureLength;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)] public ushort[] Reserved;
        public ushort Links, InputButtons, InputValues, InputData, OutputButtons, OutputValues, OutputData, FeatureButtons, FeatureValues, FeatureData;
    }

    [DllImport("hid.dll")] private static extern void HidD_GetHidGuid(out Guid guid);
    [DllImport("hid.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.U1)] private static extern bool HidD_GetAttributes(SafeFileHandle handle, ref Attributes attributes);
    [DllImport("hid.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.U1)] private static extern bool HidD_GetPreparsedData(SafeFileHandle handle, out IntPtr data);
    [DllImport("hid.dll")] [return: MarshalAs(UnmanagedType.U1)] private static extern bool HidD_FreePreparsedData(IntPtr data);
    [DllImport("hid.dll")] private static extern int HidP_GetCaps(IntPtr data, out Caps caps);
    [DllImport("hid.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.U1)] private static extern bool HidD_GetProductString(SafeFileHandle handle, [Out] byte[] buffer, int size);
    [DllImport("hid.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.U1)] private static extern bool HidD_SetFeature(SafeFileHandle handle, byte[] report, int size);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)] private static extern SafeFileHandle CreateFileW(string path, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)] private static extern IntPtr SetupDiGetClassDevsW(ref Guid guid, string? enumerator, IntPtr parent, uint flags);
    [DllImport("setupapi.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetupDiEnumDeviceInterfaces(IntPtr set, IntPtr info, ref Guid guid, uint index, ref InterfaceData data);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetupDiGetDeviceInterfaceDetailW(IntPtr set, ref InterfaceData data, IntPtr detail, uint size, out uint required, IntPtr info);
    [DllImport("setupapi.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);
}
