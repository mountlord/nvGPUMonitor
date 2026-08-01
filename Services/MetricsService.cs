using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Management;
using System.Runtime.InteropServices;
using nvGPUMonitor.Models;
using nvGPUMonitor.Utils;

namespace nvGPUMonitor.Services
{
    public class MetricsService : IDisposable
    {
        // After this many consecutive failures a WMI sensor source is disabled
        // for the rest of the run, so a missing sensor does not cost a WMI
        // round-trip on every sample tick (this was a major source of the
        // 75-350 ms sampling jitter seen in field CSVs).
        private const int SensorFailureLimit = 5;

        private readonly PerformanceCounter _cpuTotal;

        // Live CPU clock = base clock * "% Processor Performance".
        // Win32_Processor.CurrentClockSpeed is static on most systems (it
        // reports the rated clock, e.g. a constant 3301 MHz) and must not be
        // used as a live value.
        private PerformanceCounter? _cpuPerfPct;
        private int _cpuBaseClockMHz;

        private DateTime _lastPythonSample;
        private TimeSpan _lastPythonCpu;
        private bool _pythonBaselineValid;

        private bool _nvmlOk;
        private IntPtr _gpu0;

        // v0.10.0: vendor-agnostic fallback backend (Windows WDDM GPU
        // counters -- Intel Arc, AMD, or NVIDIA-without-NVML). Constructed
        // only when NVML is absent; null when neither backend works.
        private WddmGpu? _wddm;
        private string _gpuName = "";
        private int _gpuSensorFailures; // LHM bridge gate, same pattern as CPU

        // PCIe link CAPABILITY (max gen/width), read once at startup.
        // This is the denominator for bandwidth utilization. The CURRENT
        // (negotiated) gen/width is re-read on every Sample() because ASPM
        // power management retrains the link at runtime (idle: gen1/gen3,
        // load: max gen). Freezing the current link at startup was the cause
        // of throughput readings "exceeding" the reported bus maximum.
        private uint? _pcieMaxGen;
        private uint? _pcieMaxWidth;
        private double? _pcieMaxBandwidthKBps;

        private int _tempFailures;
        private int _fanFailures;

        private bool _disposed;

        public MetricsService()
        {
            _cpuTotal = new PerformanceCounter("Processor", "% Processor Time", "_Total");
            _cpuTotal.NextValue();

            try
            {
                _cpuPerfPct = new PerformanceCounter("Processor Information", "% Processor Performance", "_Total");
                _cpuPerfPct.NextValue(); // prime; first real read on next Sample()
            }
            catch
            {
                _cpuPerfPct = null;
            }
            _cpuBaseClockMHz = ReadCpuBaseClockMHz();

            _lastPythonSample = DateTime.UtcNow;
            _lastPythonCpu = TimeSpan.Zero;
            _pythonBaselineValid = false;

            try
            {
                if (Nvml.nvmlInit_v2() == Nvml.Return.NVML_SUCCESS &&
                    Nvml.nvmlDeviceGetCount_v2(out var n) == Nvml.Return.NVML_SUCCESS && n > 0 &&
                    Nvml.nvmlDeviceGetHandleByIndex_v2(0, out _gpu0) == Nvml.Return.NVML_SUCCESS)
                {
                    _nvmlOk = true;

                    if (Nvml.nvmlDeviceGetMaxPcieLinkGeneration(_gpu0, out var maxGen) == Nvml.Return.NVML_SUCCESS &&
                        Nvml.nvmlDeviceGetMaxPcieLinkWidth(_gpu0, out var maxWidth) == Nvml.Return.NVML_SUCCESS &&
                        maxGen > 0 && maxWidth > 0)
                    {
                        _pcieMaxGen = maxGen;
                        _pcieMaxWidth = maxWidth;
                        _pcieMaxBandwidthKBps = CalcPcieBandwidthKBps(maxGen, maxWidth);
                    }
                    // If the max-link queries are unsupported, leave the max
                    // fields null ("unknown") rather than guessing a value.
                }
            }
            catch { _nvmlOk = false; }

            if (_nvmlOk)
            {
                try
                {
                    var sb = new System.Text.StringBuilder(96);
                    if (Nvml.nvmlDeviceGetName(_gpu0, sb, 96) == Nvml.Return.NVML_SUCCESS)
                        _gpuName = sb.ToString();
                }
                catch { }
            }
            else
            {
                // No NVML: fall back to the OS-level WDDM GPU counters
                // (vendor-agnostic; same source as Task Manager's GPU view).
                try
                {
                    var w = new WddmGpu();
                    if (w.Ok) { _wddm = w; _gpuName = w.AdapterName; }
                }
                catch { _wddm = null; }
            }
        }

        /// <summary>
        /// Theoretical one-direction PCIe payload bandwidth in KB/s (SI: KB = 1000 bytes,
        /// matching the NVML nvmlDeviceGetPcieThroughput unit convention).
        /// Per-lane payload rates in MB/s: Gen1=250, Gen2=500, Gen3=985, Gen4=1969, Gen5=3938.
        /// </summary>
        private static double CalcPcieBandwidthKBps(uint gen, uint width)
        {
            double mbPerLane = gen switch
            {
                1 => 250.0,
                2 => 500.0,
                3 => 985.0,
                4 => 1969.0,
                5 => 3938.0,
                _ => 985.0 // default to Gen 3 if unknown
            };
            return mbPerLane * width * 1000.0;
        }

        public MetricSample Sample()
        {
            var now = DateTime.UtcNow;

            double cpuLoad = Math.Clamp(_cpuTotal.NextValue(), 0, 100);

            GetMemoryStatus(out var total, out var avail);
            ulong ramUsed = total > avail ? total - avail : 0;
            double ramPct = total > 0 ? (ramUsed * 100.0) / total : 0;

            SamplePython(now, out var pyCpuPct, out var pyRss);

            bool hasNv = _nvmlOk;
            double? gpuLoad = null;
            double? memCtrlUtil = null;
            double? decoderUtil = null;
            double? encoderUtil = null;
            int? gpuTemp = null;
            int? gpuClock = null;
            int? gpuFan = null;
            ulong? vmemTotal = null, vmemUsed = null;
            uint? pcieTxKBps = null, pcieRxKBps = null;
            uint? pcieCurGen = null, pcieCurWidth = null;

            if (hasNv)
            {
                try
                {
                    if (Nvml.nvmlDeviceGetUtilizationRates(_gpu0, out var util) == Nvml.Return.NVML_SUCCESS)
                    {
                        gpuLoad = util.gpu;
                        // NVML "memory utilization" is the percentage of time
                        // the MEMORY CONTROLLER was busy, NOT how full VRAM is.
                        // VRAM occupancy is gpu_mem_used / gpu_mem_total.
                        memCtrlUtil = util.memory;
                    }
                    if (Nvml.nvmlDeviceGetTemperature(_gpu0, Nvml.TemperatureSensors.NVML_TEMPERATURE_GPU, out var t) == Nvml.Return.NVML_SUCCESS) gpuTemp = (int)t;
                    if (Nvml.nvmlDeviceGetClockInfo(_gpu0, Nvml.ClockType.Graphics, out var c) == Nvml.Return.NVML_SUCCESS) gpuClock = (int)c;
                    if (Nvml.nvmlDeviceGetFanSpeed(_gpu0, out var f) == Nvml.Return.NVML_SUCCESS) gpuFan = (int)f;
                    if (Nvml.nvmlDeviceGetMemoryInfo(_gpu0, out var m) == Nvml.Return.NVML_SUCCESS) { vmemTotal = m.total; vmemUsed = m.used; }

                    // Refresh the CURRENT (negotiated) link state every tick;
                    // ASPM retrains the link between idle and load.
                    if (Nvml.nvmlDeviceGetCurrPcieLinkGeneration(_gpu0, out var cg) == Nvml.Return.NVML_SUCCESS && cg > 0) pcieCurGen = cg;
                    if (Nvml.nvmlDeviceGetCurrPcieLinkWidth(_gpu0, out var cw) == Nvml.Return.NVML_SUCCESS && cw > 0) pcieCurWidth = cw;

                    // NVML PCIe throughput: units are KB/s, measured by the
                    // driver over its own ~20 ms window, so short bursts can
                    // read above the sustainable line rate. TX = GPU to host,
                    // RX = host to GPU.
                    if (Nvml.nvmlDeviceGetPcieThroughput(_gpu0, Nvml.PcieUtilCounter.NVML_PCIE_UTIL_TX_BYTES, out var tx) == Nvml.Return.NVML_SUCCESS) pcieTxKBps = tx;
                    if (Nvml.nvmlDeviceGetPcieThroughput(_gpu0, Nvml.PcieUtilCounter.NVML_PCIE_UTIL_RX_BYTES, out var rx) == Nvml.Return.NVML_SUCCESS) pcieRxKBps = rx;

                    if (Nvml.nvmlDeviceGetDecoderUtilization(_gpu0, out var decUtil, out var _) == Nvml.Return.NVML_SUCCESS) decoderUtil = decUtil;
                    if (Nvml.nvmlDeviceGetEncoderUtilization(_gpu0, out var encUtil, out var _) == Nvml.Return.NVML_SUCCESS) encoderUtil = encUtil;
                }
                catch { hasNv = false; }
            }

            // v0.10.0 fallback: WDDM GPU counters (any vendor). Provides
            // load / decode / encode / VRAM; PCIe and mem-controller fields
            // stay null (NVML-only). Temp/clock/fan come from the
            // LibreHardwareMonitor WMI bridge when that app is running,
            // with the same auto-disable-after-failures gate as the CPU
            // sensors so an absent bridge costs nothing per tick.
            double? copyUtil = null; // v0.10.2: wddm Copy/DMA engine
            bool hasWddm = false;
            if (!hasNv && _wddm != null)
            {
                _wddm.Sample(out gpuLoad, out decoderUtil, out encoderUtil, out copyUtil, out var usedBytes);
                vmemUsed = usedBytes;
                vmemTotal = _wddm.VramTotal;
                hasWddm = true;

                if (_gpuSensorFailures >= 0)
                {
                    var gt = QueryHwMonitorSensor("Temperature", new[] { "GPU Core", "GPU" });
                    var gc = QueryHwMonitorSensor("Clock", new[] { "GPU Core" });
                    var gf = QueryHwMonitorSensor("Fan", new[] { "GPU" });
                    if (gt.HasValue && gt.Value > 0 && gt.Value < 120) gpuTemp = (int)Math.Round(gt.Value);
                    if (gc.HasValue && gc.Value > 0) gpuClock = (int)Math.Round(gc.Value);
                    if (gf.HasValue && gf.Value > 0) gpuFan = (int)Math.Round(gf.Value); // RPM here (NVML reports %)
                    if (!gt.HasValue && !gc.HasValue && !gf.HasValue)
                    {
                        if (++_gpuSensorFailures >= SensorFailureLimit) _gpuSensorFailures = -1;
                    }
                    else _gpuSensorFailures = 0;
                }
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
                HasGpu: hasNv || hasWddm,
                GpuBackend: hasNv ? "nvml" : (hasWddm ? "wddm" : ""),
                GpuName: (hasNv || hasWddm) ? _gpuName : "",
                GpuLoadPct: gpuLoad,
                GpuTempC: gpuTemp,
                GpuClockMHz: gpuClock,
                GpuFanRpm: gpuFan,
                GpuMemTotal: vmemTotal,
                GpuMemUsed: vmemUsed,
                MemCtrlUtilPct: memCtrlUtil,
                DecoderUtilPct: decoderUtil,
                EncoderUtilPct: encoderUtil,
                CopyUtilPct: copyUtil,
                GpuPcieTxKBps: pcieTxKBps,
                GpuPcieRxKBps: pcieRxKBps,
                PcieMaxBandwidthKBps: hasNv ? _pcieMaxBandwidthKBps : null,
                PcieCurGeneration: pcieCurGen,
                PcieCurWidth: pcieCurWidth,
                PcieMaxGeneration: hasNv ? _pcieMaxGen : null,
                PcieMaxWidth: hasNv ? _pcieMaxWidth : null,
                RamTotal: total,
                RamUsed: ramUsed,
                RamLoadPct: ramPct,
                PythonCpuPct: pyCpuPct,
                PythonWorkingSet: pyRss
            );
        }

        // ------------------------------------------------------------------
        // Python process aggregation
        // ------------------------------------------------------------------

        /// <summary>
        /// Aggregate CPU (percent of all cores) and working set across all
        /// processes whose name starts with "python" (python, python3,
        /// python3.11, pythonw, ...). Process.GetProcessesByName("python")
        /// only matched the exact name "python", which is why python_cpu and
        /// python_rss were always 0 in field data.
        /// Outputs are null (CSV empty) when no python process exists or when
        /// a rate cannot be computed yet - never a fake 0.
        /// </summary>
        private void SamplePython(DateTime now, out double? cpuPct, out ulong? rss)
        {
            cpuPct = null;
            rss = null;

            TimeSpan cpuSum = TimeSpan.Zero;
            ulong rssSum = 0;
            int found = 0;

            Process[] all;
            try { all = Process.GetProcesses(); }
            catch { _pythonBaselineValid = false; return; }

            foreach (var p in all)
            {
                try
                {
                    if (p.ProcessName.StartsWith("python", StringComparison.OrdinalIgnoreCase))
                    {
                        found++;
                        rssSum += (ulong)p.WorkingSet64;
                        cpuSum += p.TotalProcessorTime; // may throw (access denied)
                    }
                }
                catch { }
                finally { p.Dispose(); }
            }

            if (found == 0)
            {
                _pythonBaselineValid = false;
                return;
            }

            rss = rssSum;

            double deltaCpu = (cpuSum - _lastPythonCpu).TotalMilliseconds;
            double deltaWall = (now - _lastPythonSample).TotalMilliseconds;
            bool baselineWasValid = _pythonBaselineValid;

            _lastPythonCpu = cpuSum;
            _lastPythonSample = now;
            _pythonBaselineValid = true;

            // First sample after python appeared, or a python process exited
            // (aggregate CPU time went backwards): no valid rate this tick.
            if (!baselineWasValid || deltaCpu < 0 || deltaWall <= 0) return;

            cpuPct = Math.Clamp(100.0 * deltaCpu / (deltaWall * Environment.ProcessorCount), 0, 100);
        }

        // ------------------------------------------------------------------
        // CPU clock
        // ------------------------------------------------------------------

        private static int ReadCpuBaseClockMHz()
        {
            try
            {
                using var s = new ManagementObjectSearcher("SELECT MaxClockSpeed FROM Win32_Processor");
                foreach (ManagementObject mo in s.Get())
                {
                    using (mo)
                    {
                        return Convert.ToInt32(mo["MaxClockSpeed"]);
                    }
                }
            }
            catch { }
            return 0;
        }

        /// <summary>
        /// Live CPU clock: rated base clock scaled by the
        /// "% Processor Performance" counter (can exceed 100 under boost).
        /// Returns null when no live source is available.
        /// </summary>
        private int? TryGetCpuClockMHz()
        {
            if (_cpuPerfPct != null && _cpuBaseClockMHz > 0)
            {
                try
                {
                    double pct = _cpuPerfPct.NextValue();
                    if (pct > 0) return (int)Math.Round(_cpuBaseClockMHz * pct / 100.0);
                }
                catch { }
            }
            return null;
        }

        // ------------------------------------------------------------------
        // CPU temperature / fan (best effort; most consumer desktops expose
        // neither without a helper such as LibreHardwareMonitor)
        // ------------------------------------------------------------------

        private double? TryGetCpuTemp()
        {
            if (_tempFailures < 0) return null; // disabled

            // 1) LibreHardwareMonitor / OpenHardwareMonitor WMI bridge, if
            //    the helper app is running. This is the reliable path for
            //    modern AMD/Intel desktops.
            var lhm = QueryHwMonitorSensor("Temperature",
                new[] { "Core (Tctl/Tdie)", "Tctl", "CPU Package", "Package", "CPU" });
            if (lhm.HasValue && lhm.Value > 0 && lhm.Value < 120)
            {
                _tempFailures = 0;
                return lhm;
            }

            // 2) ACPI thermal zone (tenths of Kelvin). Often absent or a
            //    static motherboard zone on desktops.
            try
            {
                using var s = new ManagementObjectSearcher(@"root\WMI", "SELECT CurrentTemperature FROM MSAcpi_ThermalZoneTemperature");
                foreach (ManagementObject mo in s.Get())
                {
                    using (mo)
                    {
                        var raw = Convert.ToDouble(mo["CurrentTemperature"]);
                        var c = (raw / 10.0) - 273.15;
                        if (c > 0 && c < 110)
                        {
                            _tempFailures = 0;
                            return c;
                        }
                    }
                }
            }
            catch { }

            if (++_tempFailures >= SensorFailureLimit) _tempFailures = -1;
            return null;
        }

        private int? TryGetCpuFanRpm()
        {
            if (_fanFailures < 0) return null; // disabled

            var lhm = QueryHwMonitorSensor("Fan", new[] { "CPU" });
            if (lhm.HasValue && lhm.Value > 0)
            {
                _fanFailures = 0;
                return (int)Math.Round(lhm.Value);
            }

            try
            {
                using var s = new ManagementObjectSearcher(@"root\WMI", "SELECT CurrentRPM FROM Win32_Fan");
                foreach (ManagementObject mo in s.Get())
                {
                    using (mo)
                    {
                        var rpm = mo["CurrentRPM"];
                        if (rpm != null)
                        {
                            _fanFailures = 0;
                            return Convert.ToInt32(rpm);
                        }
                    }
                }
            }
            catch { }

            if (++_fanFailures >= SensorFailureLimit) _fanFailures = -1;
            return null;
        }

        /// <summary>
        /// Query the LibreHardwareMonitor (or OpenHardwareMonitor) WMI
        /// namespace for a sensor value. Returns the best match by name
        /// priority, or null when the helper app is not running.
        /// </summary>
        private static double? QueryHwMonitorSensor(string sensorType, string[] namePriority)
        {
            foreach (var ns in new[] { @"root\LibreHardwareMonitor", @"root\OpenHardwareMonitor" })
            {
                try
                {
                    using var s = new ManagementObjectSearcher(ns,
                        "SELECT Name, Value FROM Sensor WHERE SensorType='" + sensorType + "'");
                    var values = new List<KeyValuePair<string, double>>();
                    foreach (ManagementObject mo in s.Get())
                    {
                        using (mo)
                        {
                            var name = mo["Name"] as string ?? "";
                            values.Add(new KeyValuePair<string, double>(name, Convert.ToDouble(mo["Value"])));
                        }
                    }
                    if (values.Count == 0) continue;
                    foreach (var wanted in namePriority)
                    {
                        foreach (var kv in values)
                        {
                            if (kv.Key.IndexOf(wanted, StringComparison.OrdinalIgnoreCase) >= 0)
                                return kv.Value;
                        }
                    }
                }
                catch { }
            }
            return null;
        }

        // ------------------------------------------------------------------
        // System RAM
        // ------------------------------------------------------------------

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

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
            if (GlobalMemoryStatusEx(ref ms))
            {
                total = ms.ullTotalPhys;
                avail = ms.ullAvailPhys;
            }
            else
            {
                total = 0;
                avail = 0;
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _cpuTotal.Dispose();
            _cpuPerfPct?.Dispose();

            if (_nvmlOk)
            {
                try { Nvml.nvmlShutdown(); } catch { }
            }
        }
    }
}
