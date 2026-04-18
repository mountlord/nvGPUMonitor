using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Windows;
using nvGPUMonitor.Models;
using nvGPUMonitor.Services;
using nvGPUMonitor.Controls;
using Timer = System.Timers.Timer;

namespace nvGPUMonitor
{
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        private readonly MetricsService _svc;
        private readonly Timer _tick;
        private StreamWriter? _logWriter;

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnChange(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));

        public static string AppVersion { get; } =
            Assembly.GetExecutingAssembly().GetName().Version is { } v
                ? $"v{v.Major}.{v.Minor}.{v.Build}"
                : "v0.0.0";

        public string CpuSummary { get; set; } = "—";
        public string GpuSummary { get; set; } = "—";
        public string RamSummary { get; set; } = "—";
        public string PythonSummary { get; set; } = "—";

        private double _cpuLoad; public double CpuLoad { get => _cpuLoad; set { _cpuLoad = value; OnChange(nameof(CpuLoad)); } }
        private double _gpuLoad; public double GpuLoad { get => _gpuLoad; set { _gpuLoad = value; OnChange(nameof(GpuLoad)); } }
        private double _ramLoad; public double RamLoad { get => _ramLoad; set { _ramLoad = value; OnChange(nameof(RamLoad)); } }
        private double _vramLoad; public double VramLoad { get => _vramLoad; set { _vramLoad = value; OnChange(nameof(VramLoad)); } }
        private double _decoderLoad; public double DecoderLoad { get => _decoderLoad; set { _decoderLoad = value; OnChange(nameof(DecoderLoad)); } }
        private double _encoderLoad; public double EncoderLoad { get => _encoderLoad; set { _encoderLoad = value; OnChange(nameof(EncoderLoad)); } }
        private double _pcieTxLoad; public double PcieTxLoad { get => _pcieTxLoad; set { _pcieTxLoad = value; OnChange(nameof(PcieTxLoad)); } }
        private double _pcieRxLoad; public double PcieRxLoad { get => _pcieRxLoad; set { _pcieRxLoad = value; OnChange(nameof(PcieRxLoad)); } }

        private string _gpuDetail = ""; public string GpuDetail { get => _gpuDetail; set { _gpuDetail = value; OnChange(nameof(GpuDetail)); } }
        private string _ramDetail = ""; public string RamDetail { get => _ramDetail; set { _ramDetail = value; OnChange(nameof(RamDetail)); } }
        private string _vramDetail = ""; public string VramDetail { get => _vramDetail; set { _vramDetail = value; OnChange(nameof(VramDetail)); } }
        private string _decoderDetail = ""; public string DecoderDetail { get => _decoderDetail; set { _decoderDetail = value; OnChange(nameof(DecoderDetail)); } }
        private string _encoderDetail = ""; public string EncoderDetail { get => _encoderDetail; set { _encoderDetail = value; OnChange(nameof(EncoderDetail)); } }
        private string _pcieTxRate = "0 KB/s"; public string PcieTxRate { get => _pcieTxRate; set { _pcieTxRate = value; OnChange(nameof(PcieTxRate)); } }
        private string _pcieRxRate = "0 KB/s"; public string PcieRxRate { get => _pcieRxRate; set { _pcieRxRate = value; OnChange(nameof(PcieRxRate)); } }
        private string _pcieTxDetail = "0 KB/s"; public string PcieTxDetail { get => _pcieTxDetail; set { _pcieTxDetail = value; OnChange(nameof(PcieTxDetail)); } }
        private string _pcieRxDetail = "0 KB/s"; public string PcieRxDetail { get => _pcieRxDetail; set { _pcieRxDetail = value; OnChange(nameof(PcieRxDetail)); } }
        private string _pcieDetail = ""; public string PcieDetail { get => _pcieDetail; set { _pcieDetail = value; OnChange(nameof(PcieDetail)); } }

        public ObservableCollection<TableRow> TableRows { get; } = new();

        public MainWindow()
        {
            InitializeComponent();
            DataContext = this;
            _svc = new MetricsService();
            _tick = new Timer(1000);
            _tick.Elapsed += (_, __) => Dispatcher.Invoke(UpdateUi);
            _tick.Start();
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            _tick.Stop();
            _tick.Dispose();
            _logWriter?.Dispose();
            _logWriter = null;
            _svc.Dispose();
            base.OnClosing(e);
        }

        private void UpdateUi()
        {
            var m = _svc.Sample();

            CpuSummary = $"Load {m.CpuLoadPct:0}% • Temp {(m.CpuTempC?.ToString("0") ?? "N/A")}°C • Clock {m.CpuClockMHz?.ToString("0") ?? "N/A"} MHz • Fan {(m.CpuFanRpm?.ToString() ?? "N/A")} RPM";
            GpuSummary = m.HasNvGpu
                ? $"Load {m.GpuLoadPct:0}% • Temp {m.GpuTempC:0}°C • Clock {m.GpuClockMHz} MHz • Fan {m.GpuFanRpm} RPM • VRAM {Bytes(m.GpuMemUsed)} / {Bytes(m.GpuMemTotal)}"
                : "No NVIDIA GPU (NVML not found)";
            RamSummary = $"Used {Bytes(m.RamUsed)} / {Bytes(m.RamTotal)} ({m.RamLoadPct:0}%)";
            PythonSummary = $"CPU {m.PythonCpuPct:0.0}% • RSS {Bytes(m.PythonWorkingSet)}";

            CpuLoad = m.CpuLoadPct;
            GpuLoad = m.GpuLoadPct;
            RamLoad = m.RamLoadPct;

            // VRAM gauge: show allocation percentage (used / total), not memory controller utilization
            VramLoad = m.GpuMemTotal > 0
                ? (m.GpuMemUsed * 100.0) / m.GpuMemTotal
                : 0;

            DecoderLoad = m.DecoderUtilPct;
            EncoderLoad = m.EncoderUtilPct;

            GpuDetail = m.HasNvGpu ? $"{m.GpuTempC}°C, {m.GpuClockMHz} MHz" : "—";
            RamDetail = $"{Bytes(m.RamUsed)} / {Bytes(m.RamTotal)}";
            VramDetail = $"{Bytes(m.GpuMemUsed)} / {Bytes(m.GpuMemTotal)}";
            DecoderDetail = m.HasNvGpu ? "Video Decode" : "—";
            EncoderDetail = m.HasNvGpu ? "Video Encode" : "—";
            
            // Update PCIe bandwidth using detected maximum bandwidth
            double maxBw = m.PcieMaxBandwidthKBps > 0 ? m.PcieMaxBandwidthKBps : 15750000.0; // Fallback to PCIe 3.0 x16
            PcieTxLoad = Math.Min(100, (m.GpuPcieTxKBps / maxBw) * 100);
            PcieRxLoad = Math.Min(100, (m.GpuPcieRxKBps / maxBw) * 100);
            PcieTxDetail = FormatBandwidth(m.GpuPcieTxKBps);
            PcieRxDetail = FormatBandwidth(m.GpuPcieRxKBps);
            PcieTxRate = FormatBandwidth(m.GpuPcieTxKBps);
            PcieRxRate = FormatBandwidth(m.GpuPcieRxKBps);
            PcieDetail = $"PCIe {m.PcieGeneration}.0 x{m.PcieWidth}";

            var row = new TableRow
            {
                ColT = DateTime.Now.ToString("HH:mm:ss"),
                Col0 = $"{m.GpuLoadPct:0}%",
                Col1 = $"{m.GpuTempC} °C",
                Col2 = $"{m.GpuClockMHz} MHz",
                Col3 = $"{Bytes(m.GpuMemUsed)} / {Bytes(m.GpuMemTotal)}",
                Col4 = $"{m.CpuLoadPct:0}%",
                Col5 = m.CpuTempC.HasValue ? $"{m.CpuTempC:0} °C" : "N/A",
                Col6 = m.CpuClockMHz.HasValue ? $"{m.CpuClockMHz} MHz" : "N/A",
                Col7 = $"{Bytes(m.RamUsed)} / {Bytes(m.RamTotal)} ({m.RamLoadPct:0}%)",
                Col8 = $"CPU {m.PythonCpuPct:0.0}% RSS {Bytes(m.PythonWorkingSet)}",
                Col9 = FormatBandwidth(m.GpuPcieTxKBps),
                Col10 = FormatBandwidth(m.GpuPcieRxKBps),
                Col11 = $"{m.DecoderUtilPct:0}%",
                Col12 = $"{m.EncoderUtilPct:0}%"
            };
            TableRows.Insert(0, row);  // Insert at top instead of bottom
            if (TableRows.Count > 500) TableRows.RemoveAt(TableRows.Count - 1);  // Remove from bottom

            // Write to log file if recording is active
            if (_logWriter != null)
            {
                try
                {
                    _logWriter.WriteLine(m.ToCsv());
                    _logWriter.Flush(); // Ensure data is written immediately
                }
                catch (Exception ex)
                {
                    // Stop logging on error
                    _logWriter?.Dispose();
                    _logWriter = null;
                    MessageBox.Show($"Logging error: {ex.Message}\nLogging has been stopped.", "nvGPUMonitor Error");
                }
            }
        }

        private static string Bytes(ulong b)
        {
            double v = b;
            string[] units = { "B", "KB", "MB", "GB", "TB" };
            int u = 0;
            while (v >= 1024 && u < units.Length - 1)
            {
                v /= 1024;
                u++;
            }
            return $"{v:0.##} {units[u]}";
        }

        private static string FormatBandwidth(uint kbps)
        {
            if (kbps < 1024)
                return $"{kbps} KB/s";
            else if (kbps < 1024 * 1024)
                return $"{kbps / 1024.0:0.##} MB/s";
            else
                return $"{kbps / (1024.0 * 1024.0):0.##} GB/s";
        }

        private void StartLog_Click(object sender, RoutedEventArgs e)
        {
            if (_logWriter != null) return;

            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "nvGPUMonitor");
            Directory.CreateDirectory(dir);

            var path = Path.Combine(dir, $"metrics-{DateTime.Now:yyyyMMdd-HHmmss}.csv");
            _logWriter = new StreamWriter(path);
            _logWriter.WriteLine(Models.MetricSample.CsvHeader);

            string msg = "Logging to:" + Environment.NewLine + path;
            MessageBox.Show(msg, "nvGPUMonitor");
        }

        private void StopLog_Click(object sender, RoutedEventArgs e)
        {
            _logWriter?.Dispose();
            _logWriter = null;
            MessageBox.Show("Logging stopped.", "nvGPUMonitor");
        }
		private void DonutGauge_Loaded(object sender, RoutedEventArgs e)
		{
			if (sender is DonutGauge gauge)
			{
				gauge.InvalidateVisual();
			}
		}

        private void IntervalComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            // Don't execute during initialization (before _tick is created)
            if (_tick == null) return;
            
            if (sender is System.Windows.Controls.ComboBox comboBox && comboBox.SelectedItem is System.Windows.Controls.ComboBoxItem item)
            {
                if (item.Tag is string intervalStr && int.TryParse(intervalStr, out int intervalMs))
                {
                    _tick.Stop();
                    _tick.Interval = intervalMs;
                    _tick.Start();
                }
            }
        }

        private void About_Click(object sender, RoutedEventArgs e)
        {
            var about = new AboutWindow { Owner = this };
            about.ShowDialog();
        }

        private void Donate_Click(object sender, RoutedEventArgs e)
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
                "https://www.savethechildren.org/") { UseShellExecute = true });
        }
    }
}
