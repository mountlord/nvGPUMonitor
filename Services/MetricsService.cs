using System;
using System.Diagnostics;
using System.Management;
using System.Runtime.InteropServices;
using nvGPUMonitor.Models;
using nvGPUMonitor.Utils;

namespace nvGPUMonitor.Services
{
    public class MetricsService : IDisposable
    {
        private readonly PerformanceCounter _cpuTotal;
        private DateTime _lastPythonSample;
        private TimeSpan _lastPythonCpu;
        private int _procCount;
        private bool _nvmlOk;
        private IntPtr _gpu0;
        private uint _pcieCurrentGen;
        private uint _pcieCurrentWidth;
        private double _pcieMaxBandwidthKBps;
        private bool _disposed;

        public MetricsService()
        {
            _cpuTotal = new PerformanceCounter("Processor", "% Processor Time", "_Total");
            _cpuTotal.NextValue();
            _lastPythonSample = DateTime.UtcNow;
            _lastPythonCpu = GetPythonCpuTime();

            try
            {
                if (Nvml.nvmlInit_v2() == Nvml.Return.NVML_SUCCESS &&
                    Nvml.nvmlDeviceGetCount_v2(out var n) == Nvml.Return.NVML_SUCCESS && n > 0 &&
                    Nvml.nvmlDeviceGetHandleByIndex_v2(0, out _gpu0) == Nvml.Return.NVML_SUCCESS)
                {
                    _nvmlOk = true;
                    
                    // Get current (negotiated) PCIe link parameters — reflects actual slot capability
                    if (Nvml.nvmlDeviceGetCurrPcieLinkGeneration(_gpu0, out _pcieCurrentGen) == Nvml.Return.NVML_SUCCESS &&
                        Nvml.nvmlDeviceGetCurrPcieLinkWidth(_gpu0, out _pcieCurrentWidth) == Nvml.Return.NVML_SUCCESS)
                    {
                        _pcieMaxBandwidthKBps = CalcPcieBandwidthKBps(_pcieCurrentGen, _pcieCurrentWidth);
                    }
                    else
                    {
                        // Fallback to PCIe 3.0 x16
                        _pcieCurrentGen = 3;
                        _pcieCurrentWidth = 16;
                        _pcieMaxBandwidthKBps = CalcPcieBandwidthKBps(3, 16);
                    }
                }
            }
            catch { _nvmlOk = false; }
        }

        /// <summary>
        /// Calculate theoretical one-direction PCIe bandwidth in KB/s.
        /// Per-lane rates (MB/s): Gen1=250, Gen2=500, Gen3=985, Gen4=1969, Gen5=3938.
        /// </summary>
        private static double CalcPcieBandwidthKBps(uint gen, uint width)
        {
            double mbpsPerLane = gen switch
            {
                1 => 250.0,
                2 => 500.0,
                3 => 985.0,
                4 => 1969.0,
                5 => 3938.0,
                _ => 985.0 // Default to Gen 3 if unknown
            };
            return mbpsPerLane * width * 1024.0;
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
            double vramUtil = 0;
            double decoderUtil = 0;
            double encoderUtil = 0;
            int gpuTemp = 0;
            int gpuClock = 0;
            int gpuFan = 0;
            ulong vmemTotal = 0, vmemUsed = 0;
            uint pcieTxKBps = 0, pcieRxKBps = 0;

            if (hasNv)
            {
                try
                {
                    if (Nvml.nvmlDeviceGetUtilizationRates(_gpu0, out var util) == Nvml.Return.NVML_SUCCESS)
                    {
                        gpuLoad = util.gpu;
                        vramUtil = util.memory;
                    }
                    if (Nvml.nvmlDeviceGetTemperature(_gpu0, Nvml.TemperatureSensors.NVML_TEMPERATURE_GPU, out var t) == Nvml.Return.NVML_SUCCESS) gpuTemp = (int)t;
                    if (Nvml.nvmlDeviceGetClockInfo(_gpu0, Nvml.ClockType.Graphics, out var c) == Nvml.Return.NVML_SUCCESS) gpuClock = (int)c;
                    if (Nvml.nvmlDeviceGetFanSpeed(_gpu0, out var f) == Nvml.Return.NVML_SUCCESS) gpuFan = (int)f;
                    if (Nvml.nvmlDeviceGetMemoryInfo(_gpu0, out var m) == Nvml.Return.NVML_SUCCESS) { vmemTotal = m.total; vmemUsed = m.used; }
                    
                    // PCIe bandwidth: TX = GPU→CPU (uploads), RX = CPU→GPU (downloads)
                    if (Nvml.nvmlDeviceGetPcieThroughput(_gpu0, Nvml.PcieUtilCounter.NVML_PCIE_UTIL_TX_BYTES, out var tx) == Nvml.Return.NVML_SUCCESS) pcieTxKBps = tx;
                    if (Nvml.nvmlDeviceGetPcieThroughput(_gpu0, Nvml.PcieUtilCounter.NVML_PCIE_UTIL_RX_BYTES, out var rx) == Nvml.Return.NVML_SUCCESS) pcieRxKBps = rx;
                    
                    // Decoder and Encoder utilization
                    if (Nvml.nvmlDeviceGetDecoderUtilization(_gpu0, out var decUtil, out var _) == Nvml.Return.NVML_SUCCESS) decoderUtil = decUtil;
                    if (Nvml.nvmlDeviceGetEncoderUtilization(_gpu0, out var encUtil, out var _) == Nvml.Return.NVML_SUCCESS) encoderUtil = encUtil;
                }
                catch { hasNv = false; }
            }

            double? cpuTempC = TryGetCpuTemp();
            int? cpuClock = TryGetCpuClockMHz();
            int? cpuFan = TryGetCpuFanRpm();

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
                VramUtilPct: vramUtil,
                DecoderUtilPct: decoderUtil,
                EncoderUtilPct: encoderUtil,
                GpuPcieTxKBps: pcieTxKBps,
                GpuPcieRxKBps: pcieRxKBps,
                PcieMaxBandwidthKBps: _pcieMaxBandwidthKBps,
                PcieGeneration: _pcieCurrentGen,
                PcieWidth: _pcieCurrentWidth,
                RamTotal: total,
                RamUsed: ramUsed,
                RamLoadPct: ramPct,
                PythonCpuPct: pyCpuPct,
                PythonWorkingSet: pyRss
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
            return Math.Clamp(100.0 * deltaCpu / (deltaWall * _procCount), 0, 100);
        }

        private static TimeSpan GetPythonCpuTime()
        {
            TimeSpan sum = TimeSpan.Zero;
            foreach (var p in Process.GetProcessesByName("python"))
            {
                try { sum += p.TotalProcessorTime; }
                catch { }
                finally { p.Dispose(); }
            }
            return sum;
        }

        private static ulong AggregatePythonRss()
        {
            ulong rss = 0;
            foreach (var p in Process.GetProcessesByName("python"))
            {
                try { rss += (ulong)p.WorkingSet64; }
                catch { }
                finally { p.Dispose(); }
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
                    using (mo)
                    {
                        var raw = Convert.ToDouble(mo["CurrentTemperature"]);
                        var c = (raw / 10.0) - 273.15;
                        if (c > 0 && c < 110) return c;
                    }
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
                    using (mo)
                    {
                        return Convert.ToInt32(mo["CurrentClockSpeed"]);
                    }
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
                    using (mo)
                    {
                        var rpm = mo["CurrentRPM"];
                        if (rpm != null) return Convert.ToInt32(rpm);
                    }
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
            ms.dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
            GlobalMemoryStatusEx(ref ms);
            total = ms.ullTotalPhys;
            avail = ms.ullAvailPhys;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _cpuTotal.Dispose();

            if (_nvmlOk)
            {
                try { Nvml.nvmlShutdown(); } catch { }
            }
        }
    }
}
