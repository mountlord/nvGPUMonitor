# nvGPUMonitor CSV Column Reference (schema v0.9.0)

Global conventions:

- **Empty field = "could not read / not applicable".** A `0` is always a real
  measured zero. Parsers must treat empty and `0` differently.
- `ts` is **ISO-8601 UTC**. The log **filename** uses **local time**. When
  correlating with wall-clock events, convert.
- Bandwidth columns are **kilobytes per second (KB/s, SI: 1 KB = 1000 B)**,
  the unit convention of NVML `nvmlDeviceGetPcieThroughput`. Column names use
  `_kbytes_s` so they cannot be misread as kilobits.
- Numbers are written with `InvariantCulture` (`.` decimal separator)
  regardless of the system locale.
- Sampling interval is the UI-selected tick (default 1000 ms; 200 ms in
  ChitraMaya runs). Sampling runs off the UI thread; a tick is **skipped**
  (not queued) if the previous sample is still in progress, so expect
  occasional longer gaps rather than bursts.

| Column | Source | Units / range | Semantics |
|---|---|---|---|
| `ts` | `DateTime.UtcNow` | ISO-8601 UTC | Time the sample was taken. |
| `cpu_load` | PerfCounter `Processor \ % Processor Time \ _Total` | % 0-100 | Whole-machine CPU load since previous read. |
| `cpu_temp` | LibreHardwareMonitor/OpenHardwareMonitor WMI bridge (preferred: `Tctl`/`Package`), else `MSAcpi_ThermalZoneTemperature` | deg C | CPU temperature. Empty on most desktops unless LibreHardwareMonitor is running (modern AMD/Intel desktops do not expose CPU temp via stock WMI). Source is auto-disabled after 5 consecutive failures. |
| `cpu_clock` | PerfCounter `Processor Information \ % Processor Performance \ _Total` x rated base clock (`Win32_Processor.MaxClockSpeed`) | MHz | **Live** effective clock; can exceed base under boost. (Pre-0.9.0 this was `Win32_Processor.CurrentClockSpeed`, which is static on most systems - hence the constant 3301 MHz.) Empty if the counter is unavailable. |
| `cpu_fan` | LHM/OHM WMI bridge (Fan, "CPU"), else `Win32_Fan.CurrentRPM` | RPM | CPU fan speed. `Win32_Fan` is unpopulated on nearly all consumer boards; expect empty unless LibreHardwareMonitor is running. |
| `has_nvidia` | NVML init + device 0 handle | 0/1 | 1 when NVML is loaded and GPU 0 responded this tick. When 0, all GPU columns are empty. |
| `gpu_load` | `nvmlDeviceGetUtilizationRates().gpu` | % 0-100 | Percent of time over the driver's sample window (~1 s or less) in which at least one kernel was executing. |
| `gpu_temp` | `nvmlDeviceGetTemperature(GPU)` | deg C | GPU core temperature. |
| `gpu_clock` | `nvmlDeviceGetClockInfo(Graphics)` | MHz | Current graphics clock. |
| `gpu_fan` | `nvmlDeviceGetFanSpeed` | % 0-100 | Intended fan speed as percent of max, **not RPM**. |
| `gpu_mem_total` | `nvmlDeviceGetMemoryInfo.total` | bytes | Total VRAM (WDDM-reserved memory reduces this slightly vs. the sticker size). |
| `gpu_mem_used` | `nvmlDeviceGetMemoryInfo.used` | bytes | Allocated VRAM. **Occupancy % = gpu_mem_used / gpu_mem_total.** |
| `mem_ctrl_util` | `nvmlDeviceGetUtilizationRates().memory` | % 0-100 | Percent of time the **memory controller** was busy (bandwidth pressure). **NOT** "how full VRAM is". Renamed from the misleading `vram_util`. |
| `decoder_util` | `nvmlDeviceGetDecoderUtilization` | % 0-100 | NVDEC utilization over the driver's own sampling period (~100-200 ms typ.). 0 across a run means the workload decoded on CPU or another engine. |
| `encoder_util` | `nvmlDeviceGetEncoderUtilization` | % 0-100 | NVENC utilization over the driver's own sampling period. Bursty synchronous encodes sampled at ~5 Hz legitimately produce a 0-or-100 bimodal pattern (aliasing between our tick and the driver window), not an error. |
| `gpu_pcie_tx_kbytes_s` | `nvmlDeviceGetPcieThroughput(TX)` | KB/s | GPU -> host traffic. Measured by the driver over its own **~20 ms window**, then reported as a rate: short bursts can read up to roughly 10-15% above sustainable line rate. Treat spikes as indicative, medians as trustworthy. |
| `gpu_pcie_rx_kbytes_s` | `nvmlDeviceGetPcieThroughput(RX)` | KB/s | Host -> GPU traffic. Same caveats as TX. |
| `pcie_max_bw_kbytes_s` | computed from `pcie_max_gen` x `pcie_max_width` | KB/s | Theoretical one-direction payload bandwidth of the link **capability** (per-lane MB/s: Gen1 250, Gen2 500, Gen3 985, Gen4 1969, Gen5 3938). Correct denominator for utilization. (Pre-0.9.0 this was wrongly derived from the *current* link at startup; an ASPM-downtrained idle link made throughput appear to exceed the bus max.) |
| `pcie_cur_gen` | `nvmlDeviceGetCurrPcieLinkGeneration` (re-read every tick) | 1-5 | **Live negotiated** link generation; drops at idle under ASPM, rises under load. |
| `pcie_cur_width` | `nvmlDeviceGetCurrPcieLinkWidth` (re-read every tick) | lanes | Live negotiated link width. |
| `pcie_max_gen` | `nvmlDeviceGetMaxPcieLinkGeneration` (read once) | 1-5 | Link capability = min(GPU, slot). |
| `pcie_max_width` | `nvmlDeviceGetMaxPcieLinkWidth` (read once) | lanes | Link width capability. |
| `ram_total` | `GlobalMemoryStatusEx.ullTotalPhys` | bytes | Physical RAM. |
| `ram_used` | total - `ullAvailPhys` | bytes | Physical RAM in use. |
| `ram_load` | derived | % 0-100 | `ram_used / ram_total`. |
| `python_cpu` | `Process.TotalProcessorTime` delta across all processes named `python*` | % of ALL cores, 0-100 | Aggregate CPU of every process whose name starts with `python` (python, python3, python3.11, pythonw, ...). 100% = all cores saturated by python. Empty on the first tick after a python process appears, when one exits mid-interval, or when none is running. (Pre-0.9.0 only the exact name `python` matched, so this was always 0.) |
| `python_rss` | `Process.WorkingSet64` sum across `python*` | bytes | Aggregate working set of the same process set. Empty when no python process is running. |

## Known limitations (documented, not bugs)

- **PCIe throughput spikes**: NVML's 20 ms measurement window means
  instantaneous readings above `pcie_max_bw_kbytes_s` are possible; clamp to
  100% for utilization math and rely on medians/percentiles for tuning
  decisions.
- **cpu_temp / cpu_fan on desktops**: run LibreHardwareMonitor (with its WMI
  bridge enabled) to populate these; stock Windows WMI does not expose them
  on most consumer hardware.
- **decoder_util verification**: to confirm NVDEC reads work, play a video
  with hardware decode forced (e.g. mpv `--hwdec=nvdec` or check Task
  Manager's "Video Decode" engine) and confirm nonzero values.
- **Cross-validation**: `nvidia-smi dmon -s ut` shows the same NVML counters
  (pcie in MB/s); Task Manager GPU "Copy" engine correlates with transfer
  bursts but is not the same metric.
