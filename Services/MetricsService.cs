using System;
using System.Diagnostics;
using System.Management;
using System.Runtime.InteropServices;
using nvGPUMonitor.Models;
using nvGPUMonitor.Utils;

namespace nvGPUMonitor.Services
{
    public class MetricsService
    {
        private readonly PerformanceCounter _cpuTotal;
        private DateTime _lastPythonSample;
        private TimeSpan _lastPythonCpu;
        private int _procCount;
        private bool _nvmlOk;
        private IntPtr _gpu0;
        private string _tempDirPath;

        public MetricsService()
        {
            _cpuTotal = new PerformanceCounter("Processor", "% Processor Time", "_Total");
            _cpuTotal.NextValue();
            _lastPythonSample = DateTime.UtcNow;
            _lastPythonCpu = GetPythonCpuTime();
            _tempDirPath = System.IO.Path.GetTempPath(); // Default to Windows TEMP

            try
            {
                if (Nvml.nvmlInit_v2() == Nvml.Return.NVML_SUCCESS &&
                    Nvml.nvmlDeviceGetCount_v2(out var n) == Nvml.Return.NVML_SUCCESS && n > 0 &&
                    Nvml.nvmlDeviceGetHandleByIndex_v2(0, out _gpu0) == Nvml.Return.NVML_SUCCESS)
                {
                    _nvmlOk = true;
                }
            }
            catch { _nvmlOk = false; }
        }

        public void SetTempDirectory(string path)
        {
            if (System.IO.Directory.Exists(path))
            {
                _tempDirPath = path;
            }
        }

        public string GetCurrentTempPath()
        {
            return _tempDirPath;
        }

        public MetricSample Sample()
        {
            var now = DateTime.UtcNow;

            double cpuLoad = Math.Clamp(_cpuTotal.NextValue(), 0, 100);

            GetMemoryStatus(out var total, out var avail);
            ulong ramUsed = total - avail;
            double ramPct = total > 0 ? (ramUsed * 100.0) / total : 0;

            var pyCpuPct = SamplePythonCpuPct(now);
            var pyRss = AggregatePythonRss();

            bool hasNv = _nvmlOk;
            double gpuLoad = 0;
            int gpuTemp = 0;
            int gpuClock = 0;
            int gpuFan = 0;
            ulong vmemTotal = 0, vmemUsed = 0;
            uint pcieTxKBps = 0, pcieRxKBps = 0;

            if (hasNv)
            {
                try
                {
                    if (Nvml.nvmlDeviceGetUtilizationRates(_gpu0, out var util) == Nvml.Return.NVML_SUCCESS) gpuLoad = util.gpu;
                    if (Nvml.nvmlDeviceGetTemperature(_gpu0, Nvml.TemperatureSensors.NVML_TEMPERATURE_GPU, out var t) == Nvml.Return.NVML_SUCCESS) gpuTemp = (int)t;
                    if (Nvml.nvmlDeviceGetClockInfo(_gpu0, Nvml.ClockType.Graphics, out var c) == Nvml.Return.NVML_SUCCESS) gpuClock = (int)c;
                    if (Nvml.nvmlDeviceGetFanSpeed(_gpu0, out var f) == Nvml.Return.NVML_SUCCESS) gpuFan = (int)f;
                    if (Nvml.nvmlDeviceGetMemoryInfo(_gpu0, out var m) == Nvml.Return.NVML_SUCCESS) { vmemTotal = m.total; vmemUsed = m.used; }
                    
                    // PCIe bandwidth: TX = GPU→CPU (uploads), RX = CPU→GPU (downloads)
                    if (Nvml.nvmlDeviceGetPcieThroughput(_gpu0, Nvml.PcieUtilCounter.NVML_PCIE_UTIL_TX_BYTES, out var tx) == Nvml.Return.NVML_SUCCESS) pcieTxKBps = tx;
                    if (Nvml.nvmlDeviceGetPcieThroughput(_gpu0, Nvml.PcieUtilCounter.NVML_PCIE_UTIL_RX_BYTES, out var rx) == Nvml.Return.NVML_SUCCESS) pcieRxKBps = rx;
                }
                catch { hasNv = false; }
            }

            double? cpuTempC = TryGetCpuTemp();
            int? cpuClock = TryGetCpuClockMHz();
            int? cpuFan = TryGetCpuFanRpm();

            ulong tempDirSize = GetTempDirectorySize();

            return new MetricSample(
                Timestamp: now,
                CpuLoadPct: cpuLoad,
                CpuTempC: cpuTempC,
                CpuClockMHz: cpuClock,
                CpuFanRpm: cpuFan,
                HasNvGpu: hasNv,
                GpuLoadPct: gpuLoad,
                GpuTempC: gpuTemp,
                GpuClockMHz: gpuClock,
                GpuFanRpm: gpuFan,
                GpuMemTotal: vmemTotal,
                GpuMemUsed: vmemUsed,
                GpuPcieTxKBps: pcieTxKBps,
                GpuPcieRxKBps: pcieRxKBps,
                RamTotal: total,
                RamUsed: ramUsed,
                RamLoadPct: ramPct,
                PythonCpuPct: pyCpuPct,
                PythonWorkingSet: pyRss,
                TempDirBytes: tempDirSize
            );
        }

        private double SamplePythonCpuPct(DateTime now)
        {
            var cpuTime = GetPythonCpuTime();
            var deltaCpu = (cpuTime - _lastPythonCpu).TotalMilliseconds;
            var deltaWall = (now - _lastPythonSample).TotalMilliseconds;
            _lastPythonCpu = cpuTime;
            _lastPythonSample = now;

            if (deltaWall <= 0) return 0;
            _procCount = _procCount == 0 ? Environment.ProcessorCount : _procCount;
            return Math.Clamp(100.0 * deltaCpu / (deltaWall * 10.0 * _procCount), 0, 100);
        }

        private static TimeSpan GetPythonCpuTime()
        {
            TimeSpan sum = TimeSpan.Zero;
            foreach (var p in Process.GetProcessesByName("python"))
            {
                try { sum += p.TotalProcessorTime; } catch { }
            }
            return sum;
        }

        private static ulong AggregatePythonRss()
        {
            ulong rss = 0;
            foreach (var p in Process.GetProcessesByName("python"))
            {
                try { rss += (ulong)p.WorkingSet64; } catch { }
            }
            return rss;
        }

        private static double? TryGetCpuTemp()
        {
            try
            {
                using var s = new ManagementObjectSearcher(@"root\WMI", "SELECT CurrentTemperature FROM MSAcpi_ThermalZoneTemperature");
                foreach (ManagementObject mo in s.Get())
                {
                    var raw = Convert.ToDouble(mo["CurrentTemperature"]);
                    var c = (raw / 10.0) - 273.15;
                    if (c > 0 && c < 110) return c;
                }
            }
            catch { }
            return null;
        }

        private static int? TryGetCpuClockMHz()
        {
            try
            {
                using var s = new ManagementObjectSearcher("SELECT CurrentClockSpeed FROM Win32_Processor");
                foreach (ManagementObject mo in s.Get())
                {
                    return Convert.ToInt32(mo["CurrentClockSpeed"]);
                }
            }
            catch { }
            return null;
        }

        private static int? TryGetCpuFanRpm()
        {
            try
            {
                using var s = new ManagementObjectSearcher(@"root\WMI", "SELECT CurrentRPM FROM Win32_Fan");
                foreach (ManagementObject mo in s.Get())
                {
                    var rpm = mo["CurrentRPM"];
                    if (rpm != null) return Convert.ToInt32(rpm);
                }
            }
            catch { }
            return null;
        }

        [DllImport("kernel32.dll")]
        private static extern void GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct MEMORYSTATUSEX
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;
        }

        private static void GetMemoryStatus(out ulong total, out ulong avail)
        {
            MEMORYSTATUSEX ms = new MEMORYSTATUSEX();
            ms.dwLength = (uint)System.Runtime.InteropServices.Marshal.SizeOf(typeof(MEMORYSTATUSEX));
            GlobalMemoryStatusEx(ref ms);
            total = ms.ullTotalPhys;
            avail = ms.ullAvailPhys;
        }

        private ulong GetTempDirectorySize()
        {
            try
            {
                string tempPath = _tempDirPath; // Use configured path
                var dirInfo = new System.IO.DirectoryInfo(tempPath);
                
                ulong totalSize = 0;
                foreach (var file in dirInfo.GetFiles("*", System.IO.SearchOption.AllDirectories))
                {
                    try
                    {
                        totalSize += (ulong)file.Length;
                    }
                    catch
                    {
                        // Skip files we can't access
                    }
                }
                return totalSize;
            }
            catch
            {
                return 0;
            }
        }
    }
}