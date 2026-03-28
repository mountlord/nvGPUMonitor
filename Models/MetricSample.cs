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
        double VramUtilPct,
        double DecoderUtilPct,
        double EncoderUtilPct,
        uint GpuPcieTxKBps,
        uint GpuPcieRxKBps,
        double PcieMaxBandwidthKBps,
        uint PcieGeneration,
        uint PcieWidth,
        ulong RamTotal,
        ulong RamUsed,
        double RamLoadPct,
        double PythonCpuPct,
        ulong PythonWorkingSet
    )
    {
        public static string CsvHeader =>
            "ts,cpu_load,cpu_temp,cpu_clock,cpu_fan,has_nvidia,gpu_load,gpu_temp,gpu_clock,gpu_fan,gpu_mem_total,gpu_mem_used,vram_util,decoder_util,encoder_util,gpu_pcie_tx_kbps,gpu_pcie_rx_kbps,pcie_max_bw_kbps,pcie_gen,pcie_width,ram_total,ram_used,ram_load,python_cpu,python_rss";

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
                VramUtilPct.ToString("0.###"),
                DecoderUtilPct.ToString("0.###"),
                EncoderUtilPct.ToString("0.###"),
                GpuPcieTxKBps.ToString(),
                GpuPcieRxKBps.ToString(),
                PcieMaxBandwidthKBps.ToString("0.###"),
                PcieGeneration.ToString(),
                PcieWidth.ToString(),
                RamTotal.ToString(),
                RamUsed.ToString(),
                RamLoadPct.ToString("0.###"),
                PythonCpuPct.ToString("0.###"),
                PythonWorkingSet.ToString()
            );
    }
}
