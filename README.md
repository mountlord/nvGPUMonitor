# nvGPUMonitor - NVIDIA GPU Monitor

A real-time system monitoring application for Windows that tracks GPU, CPU, RAM, PCIe bandwidth, and disk space usage with beautiful donut-style gauges.

## Features

### 📊 Real-Time Monitoring
- **GPU**: Load percentage, temperature, clock speed, fan RPM
- **VRAM**: Memory usage and percentage
- **PCIe Bandwidth**: Separate TX/RX throughput visualization
- **Disk Space**: Free space percentage with color-coded warnings
- **CPU**: Load percentage, temperature, clock speed, fan RPM
- **RAM**: Memory usage and percentage
- **Python Processes**: Aggregate CPU and memory usage

### 🎨 Visual Gauges
- Custom donut-style gauges with smooth animations
- Color-coded indicators:
  - **PCIe**: Orange (TX) and Green (RX) dual rings
  - **Disk Space**: Green (>50% free), Orange (20-50% free), Red (<20% free)
- Real-time updating (1-second intervals)

### 📈 Data Logging
- Export metrics to CSV format
- Timestamped logs saved to `Documents/nvGPUMonitor/`
- Start/stop logging on demand

### 📁 Custom Directory Monitoring
- Monitor any directory for size tracking
- Displays size as percentage of drive capacity
- Visual warning when disk space is low

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
dotnet restore nvGPUMonitor/nvGPUMonitor.Wpf.csproj

# Build
dotnet build nvGPUMonitor/nvGPUMonitor.Wpf.csproj -c Release

# Run
dotnet run --project nvMonTwo/nvGPUMonitor.Wpf.csproj
```

## Usage

### Basic Monitoring
1. Launch nvGPUMonitor
2. Gauges update automatically every second
3. View real-time metrics for all monitored components

### Custom Directory Monitoring
1. Click **"Select Temp Dir"** button
2. Navigate to the directory you want to monitor
3. Select any file in that folder
4. Gauge shows directory size as % of drive capacity

### Data Logging
1. Click **"Start Log"** to begin recording metrics
2. Data saves to `Documents/nvGPUMonitor/metrics-[timestamp].csv`
3. Click **"Stop Log"** when done

## Project Structure
```
nvGPUMonitor/
├── nvGPUMonitor/                   # Main application
│   ├── Controls/              # Custom WPF controls
│   │   ├── DonutGauge.*       # Single-metric gauge
│   │   └── DualDonutGauge.*   # Dual-metric gauge (PCIe)
│   ├── Models/                # Data models
│   ├── Services/              # Business logic
│   ├── Utils/                 # Helper utilities
│   └── MainWindow.*          # Main UI
└── Installer/                # WiX installer project
```

## Technologies

- **Framework**: .NET 9.0, WPF (Windows Presentation Foundation)
- **GPU Interface**: NVIDIA NVML (Management Library)
- **System Metrics**: Windows Management Instrumentation (WMI)

## License

Vrisber is released under the Apache License 2.0.

## Version

v0.5.0 - Dynamic disk space monitoring with color-coded warnings
