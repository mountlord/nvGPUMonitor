# nvGPUMonitor - NVIDIA GPU Monitor

A real-time system monitoring application for Windows that tracks GPU, CPU, RAM, PCIe bandwidth, and encoder/decoder utilization with beautiful donut-style gauges.

![nvGPUMonitor in action](nvGPUMonitorScreenShot.png)

## Features

### 📊 Real-Time Monitoring
- **GPU**: Load percentage, temperature, clock speed, fan speed
- **VRAM**: Occupancy (used / total); memory-controller utilization is logged separately
- **Decoder**: NVIDIA video decoder (NVDEC) engine utilization
- **Encoder**: NVIDIA video encoder (NVENC) engine utilization
- **PCIe Bandwidth**: TX/RX throughput as % of the link's maximum capability, with live link state (e.g. `PCIe 1.0 x8 (max 4.0 x8)` when power management downtrains the link)
- **CPU**: Load percentage, live clock speed; temperature and fan when a sensor source is available (see note below)
- **RAM**: Memory usage and percentage
- **Python processes**: Aggregate CPU and memory of all running `python*` processes

### 🎨 Visual Gauges
- Custom donut-style gauges with smooth animations
- Color-coded indicators:
  - **PCIe**: Orange (TX) and Green (RX) dual rings
- Configurable polling intervals (0.1s to 5.0s)

### 📈 Data Logging
- Export metrics to CSV format
- Timestamped logs saved to `Documents/nvGPUMonitor/`
- Start/stop logging on demand
- Every column documented in [docs/METRICS_COLUMNS.md](docs/METRICS_COLUMNS.md) (sources, units, semantics)
- An empty CSV field always means "could not read"; a `0` is always a real measured zero

## Requirements

- **OS**: Windows 10/11
- **.NET**: .NET 9.0 SDK or Runtime (the MSI installer is self-contained and needs neither)
- **GPU**: NVIDIA GPU with NVML support (optional - app works without GPU)
- **CPU temperature/fan** (optional): most consumer desktops do not expose these via stock Windows WMI. Run [LibreHardwareMonitor](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor) alongside nvGPUMonitor to populate them.

## Installation

### Build from Source

**Quick start:**
```bash
# Clone the repository
git clone https://github.com/mountlord/nvGPUMonitor.git
cd nvGPUMonitor

# Build
dotnet build nvGPUMonitor.Wpf.csproj -c Release

# Run
dotnet run --project nvGPUMonitor.Wpf.csproj -c Release
```

For full build instructions, MSI packaging with WiX, the release checklist, and troubleshooting, see **[docs/BUILDING.md](docs/BUILDING.md)**.

## Usage

### Basic Monitoring
1. Launch nvGPUMonitor
2. Gauges update automatically at the configured polling interval
3. View real-time metrics for all monitored components

### Data Logging
1. Click **"Start Log"** to begin recording metrics
2. Data saves to `Documents/nvGPUMonitor/metrics-[timestamp].csv`
3. Click **"Stop Log"** when done

Note: the CSV `ts` column is ISO-8601 **UTC**; the filename timestamp is **local time**.

## Project Structure
```
nvGPUMonitor/
├── Controls/              # Custom WPF controls
│   ├── DonutGauge.*       # Single-metric gauge
│   └── DualDonutGauge.*   # Dual-metric gauge (PCIe TX/RX)
├── Models/                # Data models
├── Services/              # Business logic
├── Utils/                 # Helper utilities (NVML bindings)
├── docs/                  # Build guide and CSV column reference
├── MainWindow.*           # Main UI
└── Installer/             # WiX installer project
```

## Technologies

- **Framework**: .NET 9.0, WPF (Windows Presentation Foundation)
- **GPU Interface**: NVIDIA NVML (Management Library)
- **System Metrics**: Windows performance counters and WMI (with optional LibreHardwareMonitor bridge)

## License

nvGPUMonitor is released under the [GNU General Public License v3](LICENSE).

## Disclaimer

THIS SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED.
USE AT YOUR OWN RISK. The authors and contributors shall not be liable for any damages
arising from the use of this software, including but not limited to system instability,
data loss, or hardware issues. See the LICENSE file for full terms.

## Support the Project

If you find this software useful, consider donating to my favorite charity: [Save The Children](https://support.savethechildren.org/site/Donation2)

## Version

**v0.9.0** - Metrics audit release: corrected PCIe link-state handling, live CPU clock, python process matching, renamed misleading columns (**breaking CSV schema change**). Full history in [CHANGELOG.md](CHANGELOG.md).
