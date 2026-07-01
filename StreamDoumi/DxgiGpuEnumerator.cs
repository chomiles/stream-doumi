using System.Runtime.InteropServices;

namespace StreamDoumi;

public static class DxgiGpuEnumerator
{
    private const int DxgiErrorNotFound = unchecked((int)0x887A0002);
    private const uint DxgiAdapterFlagSoftware = 2;

    public static IReadOnlyList<DxgiGpuInfo> GetGpus()
    {
        var gpus = new List<DxgiGpuInfo>();
        var factoryGuid = typeof(IDXGIFactory1).GUID;
        var hr = CreateDXGIFactory1(ref factoryGuid, out var factory);
        if (hr < 0 || factory is null)
        {
            return gpus;
        }

        try
        {
            for (uint i = 0; ; i++)
            {
                hr = factory.EnumAdapters1(i, out var adapter);
                if (hr == DxgiErrorNotFound)
                {
                    break;
                }

                if (hr < 0 || adapter is null)
                {
                    continue;
                }

                try
                {
                    if (adapter.GetDesc1(out var desc) < 0)
                    {
                        continue;
                    }

                    if ((desc.Flags & DxgiAdapterFlagSoftware) == DxgiAdapterFlagSoftware ||
                        desc.Description.Contains("Microsoft Basic", StringComparison.OrdinalIgnoreCase) ||
                        desc.Description.Contains("Virtual", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    gpus.Add(new DxgiGpuInfo(
                        desc.Description.TrimEnd('\0', ' '),
                        desc.AdapterLuid.LowPart,
                        desc.AdapterLuid.HighPart,
                        desc.DedicatedVideoMemory));
                }
                finally
                {
                    Marshal.ReleaseComObject(adapter);
                }
            }
        }
        finally
        {
            Marshal.ReleaseComObject(factory);
        }

        return gpus;
    }

    [DllImport("dxgi.dll")]
    private static extern int CreateDXGIFactory1(ref Guid riid, out IDXGIFactory1? factory);

    [ComImport]
    [Guid("ae02eedb-c735-4690-8d52-5a8dc20213aa")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDXGIObject
    {
        [PreserveSig] int SetPrivateData(in Guid name, uint dataSize, IntPtr data);
        [PreserveSig] int SetPrivateDataInterface(in Guid name, IntPtr unknown);
        [PreserveSig] int GetPrivateData(in Guid name, ref uint dataSize, IntPtr data);
        [PreserveSig] int GetParent(in Guid riid, out IntPtr parent);
    }

    [ComImport]
    [Guid("7b7166ec-21c7-44ae-b21a-c9ae321ae369")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDXGIFactory : IDXGIObject
    {
        [PreserveSig] new int SetPrivateData(in Guid name, uint dataSize, IntPtr data);
        [PreserveSig] new int SetPrivateDataInterface(in Guid name, IntPtr unknown);
        [PreserveSig] new int GetPrivateData(in Guid name, ref uint dataSize, IntPtr data);
        [PreserveSig] new int GetParent(in Guid riid, out IntPtr parent);
        [PreserveSig] int EnumAdapters(uint adapter, out IntPtr adapterPointer);
        [PreserveSig] int MakeWindowAssociation(IntPtr windowHandle, uint flags);
        [PreserveSig] int GetWindowAssociation(out IntPtr windowHandle);
        [PreserveSig] int CreateSwapChain(IntPtr device, IntPtr desc, out IntPtr swapChain);
        [PreserveSig] int CreateSoftwareAdapter(IntPtr module, out IntPtr adapter);
    }

    [ComImport]
    [Guid("29038f61-3839-4626-91fd-086879011a05")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDXGIAdapter1 : IDXGIObject
    {
        [PreserveSig] new int SetPrivateData(in Guid name, uint dataSize, IntPtr data);
        [PreserveSig] new int SetPrivateDataInterface(in Guid name, IntPtr unknown);
        [PreserveSig] new int GetPrivateData(in Guid name, ref uint dataSize, IntPtr data);
        [PreserveSig] new int GetParent(in Guid riid, out IntPtr parent);
        [PreserveSig] int EnumOutputs(uint output, out IntPtr outputPointer);
        [PreserveSig] int GetDesc(out IntPtr desc);
        [PreserveSig] int CheckInterfaceSupport(in Guid interfaceName, out long umdVersion);
        [PreserveSig] int GetDesc1(out DxgiAdapterDesc1 desc);
    }

    [ComImport]
    [Guid("770aae78-f26f-4dba-a829-253c83d1b387")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDXGIFactory1 : IDXGIFactory
    {
        [PreserveSig] new int SetPrivateData(in Guid name, uint dataSize, IntPtr data);
        [PreserveSig] new int SetPrivateDataInterface(in Guid name, IntPtr unknown);
        [PreserveSig] new int GetPrivateData(in Guid name, ref uint dataSize, IntPtr data);
        [PreserveSig] new int GetParent(in Guid riid, out IntPtr parent);
        [PreserveSig] new int EnumAdapters(uint adapter, out IntPtr adapterPointer);
        [PreserveSig] new int MakeWindowAssociation(IntPtr windowHandle, uint flags);
        [PreserveSig] new int GetWindowAssociation(out IntPtr windowHandle);
        [PreserveSig] new int CreateSwapChain(IntPtr device, IntPtr desc, out IntPtr swapChain);
        [PreserveSig] new int CreateSoftwareAdapter(IntPtr module, out IntPtr adapter);
        [PreserveSig] int EnumAdapters1(uint adapter, out IDXGIAdapter1? adapterPointer);
        [PreserveSig] bool IsCurrent();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Luid
    {
        public uint LowPart;
        public int HighPart;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DxgiAdapterDesc1
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string Description;
        public uint VendorId;
        public uint DeviceId;
        public uint SubSysId;
        public uint Revision;
        public nuint DedicatedVideoMemory;
        public nuint DedicatedSystemMemory;
        public nuint SharedSystemMemory;
        public Luid AdapterLuid;
        public uint Flags;
    }
}

public sealed record DxgiGpuInfo(string Name, uint LuidLow, int LuidHigh, nuint DedicatedVideoMemory);
