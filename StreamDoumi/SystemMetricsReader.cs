using System.Diagnostics;
using System.Net.NetworkInformation;

namespace StreamDoumi;

public sealed class SystemMetricsReader : IDisposable
{
    private readonly PerformanceCounter? _cpuCounter;
    private readonly Dictionary<string, PerformanceCounter> _gpuCounters = new();
    private readonly Dictionary<string, string> _gpuNameByLuid = new();
    private readonly Dictionary<string, long> _previousReceived = new();
    private readonly Dictionary<string, long> _previousSent = new();
    private DateTime _previousNetworkSample = DateTime.UtcNow;

    public SystemMetricsReader()
    {
        try
        {
            _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
            _cpuCounter.NextValue();
        }
        catch
        {
            _cpuCounter = null;
        }

        InitializeGpuCounters();
        InitializeGpuNameMap();
        PrimeNetworkCounters();
    }

    public SystemMetricsSnapshot Read()
    {
        var cpu = ReadCpu();
        var memory = ReadMemory();
        var network = ReadNetwork();
        var gpuLoads = ReadGpuLoads();
        return new SystemMetricsSnapshot(cpu, memory, network.DownloadMbps, network.UploadMbps, gpuLoads);
    }

    public void Dispose()
    {
        _cpuCounter?.Dispose();
        foreach (var counter in _gpuCounters.Values)
        {
            counter.Dispose();
        }
    }

    private double ReadCpu()
    {
        try
        {
            return Math.Clamp(_cpuCounter?.NextValue() ?? 0, 0, 100);
        }
        catch
        {
            return 0;
        }
    }

    private static double ReadMemory()
    {
        try
        {
            var status = new MemoryStatusEx();
            if (!GlobalMemoryStatusEx(status) || status.TotalPhys == 0)
            {
                return 0;
            }

            var used = status.TotalPhys - status.AvailPhys;
            return Math.Clamp(used * 100.0 / status.TotalPhys, 0, 100);
        }
        catch
        {
            return 0;
        }
    }

    private (double DownloadMbps, double UploadMbps) ReadNetwork()
    {
        var now = DateTime.UtcNow;
        var seconds = Math.Max(0.001, (now - _previousNetworkSample).TotalSeconds);
        long received = 0;
        long sent = 0;
        long previousReceived = 0;
        long previousSent = 0;

        foreach (var network in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (network.OperationalStatus != OperationalStatus.Up ||
                network.NetworkInterfaceType == NetworkInterfaceType.Loopback)
            {
                continue;
            }

            var stats = network.GetIPv4Statistics();
            received += stats.BytesReceived;
            sent += stats.BytesSent;
            _previousReceived.TryGetValue(network.Id, out var oldReceived);
            _previousSent.TryGetValue(network.Id, out var oldSent);
            previousReceived += oldReceived;
            previousSent += oldSent;
            _previousReceived[network.Id] = stats.BytesReceived;
            _previousSent[network.Id] = stats.BytesSent;
        }

        _previousNetworkSample = now;
        return (
            Math.Max(0, (received - previousReceived) / seconds / 1024 / 1024),
            Math.Max(0, (sent - previousSent) / seconds / 1024 / 1024));
    }

    private void PrimeNetworkCounters()
    {
        foreach (var network in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (network.OperationalStatus != OperationalStatus.Up ||
                network.NetworkInterfaceType == NetworkInterfaceType.Loopback)
            {
                continue;
            }

            var stats = network.GetIPv4Statistics();
            _previousReceived[network.Id] = stats.BytesReceived;
            _previousSent[network.Id] = stats.BytesSent;
        }

        _previousNetworkSample = DateTime.UtcNow;
    }

    private void InitializeGpuCounters()
    {
        try
        {
            var category = new PerformanceCounterCategory("GPU Engine");
            foreach (var instance in category.GetInstanceNames())
            {
                if (!instance.Contains("engtype_3D", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                _gpuCounters[instance] = new PerformanceCounter("GPU Engine", "Utilization Percentage", instance);
            }
        }
        catch
        {
            _gpuCounters.Clear();
        }
    }

    private List<GpuMetric> ReadGpuLoads()
    {
        var values = new Dictionary<string, double>();
        foreach (var pair in _gpuCounters)
        {
            try
            {
                var name = GpuNameFromInstance(pair.Key);
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                values[name] = values.GetValueOrDefault(name) + pair.Value.NextValue();
            }
            catch
            {
                // GPU counters can disappear when drivers reset or sleep.
            }
        }

        return values
            .Select(pair => new GpuMetric(pair.Key, (int)Math.Clamp(Math.Round(pair.Value), 0, 100)))
            .OrderBy(metric => metric.Name)
            .ToList();
    }

    private string GpuNameFromInstance(string instance)
    {
        var luid = GpuLuidFromInstance(instance);
        if (luid is not null && _gpuNameByLuid.TryGetValue(luid, out var mappedName))
        {
            return mappedName;
        }

        return "";
    }

    private void InitializeGpuNameMap()
    {
        foreach (var gpu in DxgiGpuEnumerator.GetGpus())
        {
            _gpuNameByLuid[LuidKey(gpu.LuidLow, gpu.LuidHigh)] = gpu.Name;
        }
    }

    private static string? GpuLuidFromInstance(string instance)
    {
        var parts = instance.Split('_');
        for (var i = 0; i < parts.Length; i++)
        {
            if (!parts[i].Equals("luid", StringComparison.OrdinalIgnoreCase) || i + 2 >= parts.Length)
            {
                continue;
            }

            try
            {
                var high = Convert.ToInt32(parts[i + 1], 16);
                var low = Convert.ToUInt32(parts[i + 2], 16);
                return LuidKey(low, high);
            }
            catch
            {
                return null;
            }
        }

        return null;
    }

    private static string LuidKey(uint low, int high) => $"{high:X8}_{low:X8}";
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private sealed class MemoryStatusEx
    {
        public uint Length = (uint)System.Runtime.InteropServices.Marshal.SizeOf<MemoryStatusEx>();
        public uint MemoryLoad;
        public ulong TotalPhys;
        public ulong AvailPhys;
        public ulong TotalPageFile;
        public ulong AvailPageFile;
        public ulong TotalVirtual;
        public ulong AvailVirtual;
        public ulong AvailExtendedVirtual;
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx([System.Runtime.InteropServices.In, System.Runtime.InteropServices.Out] MemoryStatusEx status);
}

public sealed record SystemMetricsSnapshot(double CpuLoad, double MemoryLoad, double DownloadMbps, double UploadMbps, List<GpuMetric> Gpus);

public sealed record GpuMetric(string Name, int Load);
