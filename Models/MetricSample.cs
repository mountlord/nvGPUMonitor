using System;
using System.Globalization;

namespace nvGPUMonitor.Models
{
    /// <summary>
    /// One metrics sample.
    ///
    /// CSV conventions (schema v0.10.0 - ADDITIVE change: two columns
    /// appended at the END, existing column positions unchanged; see
    /// CHANGELOG.md. Base conventions from v0.9.0):
    /// - An EMPTY field means "could not read / not applicable". A 0 is
    ///   always a real measured zero.
    /// - Bandwidth columns are kilobytes per second (KB/s, SI), named
    ///   *_kbytes_s so they can never be misread as kilobits.
    /// - mem_ctrl_util is NVML memory-CONTROLLER busy time percent, not VRAM
    ///   occupancy. Occupancy = gpu_mem_used / gpu_mem_total.
    /// - pcie_cur_* is the live negotiated link (changes with ASPM power
    ///   management); pcie_max_* is the link capability, and
    ///   pcie_max_bw_kbytes_s is derived from pcie_max_*.
    /// - ts is ISO-8601 UTC; the log FILENAME uses local time.
    /// - v0.10.0 GPU backends: gpu_backend is "nvml" (NVIDIA, full metric
    ///   set) or "wddm" (any vendor via Windows GPU counters; PCIe and
    ///   mem_ctrl_util stay empty, temp/clock/fan only via the
    ///   LibreHardwareMonitor bridge). has_nvidia stays 1 only for nvml,
    ///   preserving its v0.9.0 meaning.
    /// </summary>
    public record MetricSample(
        DateTime Timestamp,
        double CpuLoadPct,
        double? CpuTempC,
        int? CpuClockMHz,
        int? CpuFanRpm,
        bool HasNvGpu,
        bool HasGpu,
        string GpuBackend,
        string GpuName,
        double? GpuLoadPct,
        int? GpuTempC,
        int? GpuClockMHz,
        int? GpuFanRpm,
        ulong? GpuMemTotal,
        ulong? GpuMemUsed,
        double? MemCtrlUtilPct,
        double? DecoderUtilPct,
        double? EncoderUtilPct,
        double? CopyUtilPct,
        uint? GpuPcieTxKBps,
        uint? GpuPcieRxKBps,
        double? PcieMaxBandwidthKBps,
        uint? PcieCurGeneration,
        uint? PcieCurWidth,
        uint? PcieMaxGeneration,
        uint? PcieMaxWidth,
        ulong RamTotal,
        ulong RamUsed,
        double RamLoadPct,
        double? PythonCpuPct,
        ulong? PythonWorkingSet
    )
    {
        public static string CsvHeader =>
            "ts,cpu_load,cpu_temp,cpu_clock,cpu_fan,has_nvidia," +
            "gpu_load,gpu_temp,gpu_clock,gpu_fan,gpu_mem_total,gpu_mem_used," +
            "mem_ctrl_util,decoder_util,encoder_util," +
            "gpu_pcie_tx_kbytes_s,gpu_pcie_rx_kbytes_s,pcie_max_bw_kbytes_s," +
            "pcie_cur_gen,pcie_cur_width,pcie_max_gen,pcie_max_width," +
            "ram_total,ram_used,ram_load,python_cpu,python_rss," +
            "gpu_backend,gpu_name," + // v0.10.0: appended, positions unchanged
            "copy_util"; // v0.10.2: appended (wddm Copy/DMA engine; empty on nvml)

        // CSV must be culture-invariant: on locales with a comma decimal
        // separator, culture-sensitive ToString would corrupt the file.
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        private static string F(double v) => v.ToString("0.###", Inv);
        private static string F(double? v) => v.HasValue ? v.Value.ToString("0.###", Inv) : "";
        private static string F(int? v) => v.HasValue ? v.Value.ToString(Inv) : "";
        private static string F(uint? v) => v.HasValue ? v.Value.ToString(Inv) : "";
        private static string F(ulong v) => v.ToString(Inv);
        private static string F(ulong? v) => v.HasValue ? v.Value.ToString(Inv) : "";

        // Minimal CSV quoting for free-text fields (adapter names).
        private static string Q(string s) =>
            (s.Contains(',') || s.Contains('"'))
                ? "\"" + s.Replace("\"", "\"\"") + "\""
                : s;

        public string ToCsv() =>
            string.Join(",",
                Timestamp.ToString("o", Inv),
                F(CpuLoadPct),
                F(CpuTempC),
                F(CpuClockMHz),
                F(CpuFanRpm),
                HasNvGpu ? "1" : "0",
                F(GpuLoadPct),
                F(GpuTempC),
                F(GpuClockMHz),
                F(GpuFanRpm),
                F(GpuMemTotal),
                F(GpuMemUsed),
                F(MemCtrlUtilPct),
                F(DecoderUtilPct),
                F(EncoderUtilPct),
                F(GpuPcieTxKBps),
                F(GpuPcieRxKBps),
                F(PcieMaxBandwidthKBps),
                F(PcieCurGeneration),
                F(PcieCurWidth),
                F(PcieMaxGeneration),
                F(PcieMaxWidth),
                F(RamTotal),
                F(RamUsed),
                F(RamLoadPct),
                F(PythonCpuPct),
                F(PythonWorkingSet),
                GpuBackend,
                Q(GpuName),
                F(CopyUtilPct)
            );
    }
}
