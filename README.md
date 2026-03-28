# nvGPUMonitor - NVIDIA GPU Monitor

A real-time system monitoring application for Windows that tracks GPU, CPU, RAM, PCIe bandwidth, and encoder/decoder utilization with beautiful donut-style gauges.

## Features

### 📊 Real-Time Monitoring
- **GPU**: Load percentage, temperature, clock speed, fan RPM
- **VRAM**: Memory controller utilization percentage
- **Decoder**: NVIDIA video decoder engine utilization
- **Encoder**: NVIDIA video encoder engine utilization
- **PCIe Bandwidth**: TX/RX throughput as % of detected link capacity
- **CPU**: Load percentage, temperature, clock speed, fan RPM
- **RAM**: Memory usage and percentage

### 🎨 Visual Gauges
- Custom donut-style gauges with smooth animations
- Color-coded indicators:
  - **PCIe**: Orange (TX) and Green (RX) dual rings
- Configurable polling intervals (0.1s to 5.0s)

### 📈 Data Logging
- Export metrics to CSV format
- Timestamped logs saved to `Documents/nvGPUMonitor/`
- Start/stop logging on demand

### 📋 Historical Data Table
- Last 500 data points displayed
- Newest entries appear at top
- Scrollable history view

## Requirements

- **OS**: Windows 10/11
- **.NET**: .NET 9.0 SDK or Runtime
- **GPU**: NVIDIA GPU with NVML support (optional - app works without GPU)

## Installation

### Build from Source

**Prerequisites:**
- [.NET 9.0 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)
- Visual Studio 2022 (or any C# IDE)

**Build Steps:**
```bash
# Clone the repository
git clone https://github.com/seatv/nvGPUMonitor.git
cd nvGPUMonitor

# Restore dependencies
dotnet restore nvGPUMonitor.Wpf.csproj

# Build
dotnet build nvGPUMonitor.Wpf.csproj -c Release

# Run
dotnet run --project nvGPUMonitor.Wpf.csproj
```

## Usage

### Basic Monitoring
1. Launch nvGPUMonitor
2. Gauges update automatically at the configured polling interval
3. View real-time metrics for all monitored components

### Data Logging
1. Click **"Start Log"** to begin recording metrics
2. Data saves to `Documents/nvGPUMonitor/metrics-[timestamp].csv`
3. Click **"Stop Log"** when done

## Project Structure
```
nvGPUMonitor/
├── Controls/              # Custom WPF controls
│   ├── DonutGauge.*       # Single-metric gauge
│   └── DualDonutGauge.*   # Dual-metric gauge (PCIe TX/RX)
├── Models/                # Data models
├── Services/              # Business logic
├── Utils/                 # Helper utilities (NVML bindings)
├── MainWindow.*           # Main UI
└── Installer/             # WiX installer project
```

## Technologies

- **Framework**: .NET 9.0, WPF (Windows Presentation Foundation)
- **GPU Interface**: NVIDIA NVML (Management Library)
- **System Metrics**: Windows Management Instrumentation (WMI)

## License

nvGPUMonitor is released under the [GNU General Public License v3](LICENSE).

## Disclaimer

THIS SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED.
USE AT YOUR OWN RISK. The authors and contributors shall not be liable for any damages
arising from the use of this software, including but not limited to system instability,
data loss, or hardware issues. See the LICENSE file for full terms.

## Support the Project

If you find this software useful, consider donating to my favorite charity: [Save The Children](https://support.savethechildren.org/site/Donation2)
Details coming soon.

## Version

v0.6.0 - Added Decoder/Encoder gauges, PCIe % utilization with auto-detection
