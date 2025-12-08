using System;
namespace nvGPUMonitor.Models
{
    public record MetricSample(
        DateTime Timestamp,
        double CpuLoadPct,
        double? CpuTempC,
        int? CpuClockMHz,
        int? CpuFanRpm,
        bool HasNvGpu,
        double GpuLoadPct,
        int GpuTempC,
        int GpuClockMHz,
        int GpuFanRpm,
        ulong GpuMemTotal,
        ulong GpuMemUsed,
        uint GpuPcieTxKBps,
        uint GpuPcieRxKBps,
        ulong RamTotal,
        ulong RamUsed,
        double RamLoadPct,
        double PythonCpuPct,
        ulong PythonWorkingSet,
        ulong TempDirBytes
    )
    {
        public static string CsvHeader =>
            "ts,cpu_load,cpu_temp,cpu_clock,cpu_fan,has_nvidia,gpu_load,gpu_temp,gpu_clock,gpu_fan,gpu_mem_total,gpu_mem_used,gpu_pcie_tx_kbps,gpu_pcie_rx_kbps,ram_total,ram_used,ram_load,python_cpu,python_rss,temp_dir_bytes";

        public string ToCsv() =>
            string.Join(",",
                Timestamp.ToString("o"),
                CpuLoadPct.ToString("0.###"),
                CpuTempC?.ToString("0.###") ?? "",
                CpuClockMHz?.ToString() ?? "",
                CpuFanRpm?.ToString() ?? "",
                HasNvGpu ? "1" : "0",
                GpuLoadPct.ToString("0.###"),
                GpuTempC.ToString(),
                GpuClockMHz.ToString(),
                GpuFanRpm.ToString(),
                GpuMemTotal.ToString(),
                GpuMemUsed.ToString(),
                GpuPcieTxKBps.ToString(),
                GpuPcieRxKBps.ToString(),
                RamTotal.ToString(),
                RamUsed.ToString(),
                RamLoadPct.ToString("0.###"),
                PythonCpuPct.ToString("0.###"),
                PythonWorkingSet.ToString(),
                TempDirBytes.ToString()
            );
    }
}
