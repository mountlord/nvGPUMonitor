using System;
using System.Collections.ObjectModel;
using Microsoft.Win32;
using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Threading;
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
        private int _sampling; // reentrancy guard for the timer callback
        private volatile bool _closing;

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnChange(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));

        public static string AppVersion { get; } =
            Assembly.GetExecutingAssembly().GetName().Version is { } v
                ? $"v{v.Major}.{v.Minor}.{v.Build}"
                : "v0.0.0";

        public string CpuSummary { get; set; } = "\u2014";
        public string GpuSummary { get; set; } = "\u2014";
        public string RamSummary { get; set; } = "\u2014";
        public string PythonSummary { get; set; } = "\u2014";

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

        // v0.10.2: the PCIe gauge is repurposed as the Copy/DMA engine dial
        // on the wddm backend (no OS-level PCIe counters exist off-NVML),
        // so its caption and ring labels are bindable.
        private string _pcieCaption = "PCIe"; public string PcieCaption { get => _pcieCaption; set { _pcieCaption = value; OnChange(nameof(PcieCaption)); } }
        private string _pcieLabel1 = "TX"; public string PcieLabel1 { get => _pcieLabel1; set { _pcieLabel1 = value; OnChange(nameof(PcieLabel1)); } }
        private string _pcieLabel2 = "RX"; public string PcieLabel2 { get => _pcieLabel2; set { _pcieLabel2 = value; OnChange(nameof(PcieLabel2)); } }

        public ObservableCollection<TableRow> TableRows { get; } = new();

        public MainWindow()
        {
            InitializeComponent();
            DataContext = this;
            SetLoggingState(false);
            _svc = new MetricsService();
            _tick = new Timer(1000);
            _tick.Elapsed += (_, __) => OnTick();
            _tick.Start();
        }

        /// <summary>
        /// Runs on a thread-pool thread: take the sample OFF the UI thread
        /// (WMI queries can take tens of milliseconds), then marshal only the
        /// UI update. The guard skips a tick if the previous one is still
        /// running, so slow sensors can never queue up.
        /// </summary>
        private void OnTick()
        {
            if (Interlocked.Exchange(ref _sampling, 1) == 1) return;
            try
            {
                var m = _svc.Sample();
                if (_closing) return;
                Dispatcher.Invoke(() => UpdateUi(m));
            }
            catch { /* window closing or transient sensor failure */ }
            finally
            {
                Interlocked.Exchange(ref _sampling, 0);
            }
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            _closing = true;
            _tick.Stop();
            _tick.Dispose();
            _logWriter?.Dispose();
            _logWriter = null;
            _svc.Dispose();
            base.OnClosing(e);
        }

        private void UpdateUi(MetricSample m)
        {
            CpuSummary = $"Load {m.CpuLoadPct:0}% \u2022 Temp {(m.CpuTempC?.ToString("0") ?? "N/A")}\u00B0C \u2022 Clock {m.CpuClockMHz?.ToString("0") ?? "N/A"} MHz \u2022 Fan {(m.CpuFanRpm?.ToString() ?? "N/A")} RPM";
            // v0.10.0: HasGpu covers both backends (nvml + wddm); name and
            // backend are shown so a WDDM (Arc/AMD) run is recognizable.
            GpuSummary = m.HasGpu
                ? $"{m.GpuName} [{m.GpuBackend}] \u2022 Load {m.GpuLoadPct ?? 0:0}% \u2022 Temp {m.GpuTempC?.ToString() ?? "N/A"}\u00B0C \u2022 Clock {m.GpuClockMHz?.ToString() ?? "N/A"} MHz \u2022 Fan {m.GpuFanRpm?.ToString() ?? "N/A"} \u2022 VRAM {Bytes(m.GpuMemUsed)} / {Bytes(m.GpuMemTotal)}"
                : "No GPU metrics source (no NVML, no WDDM GPU counters)";
            RamSummary = $"Used {Bytes(m.RamUsed)} / {Bytes(m.RamTotal)} ({m.RamLoadPct:0}%)";
            PythonSummary = m.PythonWorkingSet.HasValue
                ? $"CPU {(m.PythonCpuPct.HasValue ? m.PythonCpuPct.Value.ToString("0.0") : "\u2014")}% \u2022 RSS {Bytes(m.PythonWorkingSet)}"
                : "No python process";

            CpuLoad = m.CpuLoadPct;
            GpuLoad = m.GpuLoadPct ?? 0;
            RamLoad = m.RamLoadPct;

            // VRAM gauge: occupancy (used / total), NOT NVML memory-controller
            // utilization (that value is logged separately as mem_ctrl_util).
            VramLoad = (m.GpuMemTotal ?? 0) > 0
                ? (m.GpuMemUsed ?? 0) * 100.0 / m.GpuMemTotal!.Value
                : 0;

            DecoderLoad = m.DecoderUtilPct ?? 0;
            EncoderLoad = m.EncoderUtilPct ?? 0;

            GpuDetail = m.HasGpu ? $"{m.GpuTempC?.ToString() ?? "N/A"}\u00B0C, {m.GpuClockMHz?.ToString() ?? "N/A"} MHz" : "\u2014";
            RamDetail = $"{Bytes(m.RamUsed)} / {Bytes(m.RamTotal)}";
            VramDetail = $"{Bytes(m.GpuMemUsed)} / {Bytes(m.GpuMemTotal)}";
            // v0.10.1: Intel exposes one fixed-function media engine
            // (reported as VideoDecode) and accounts QSV ENCODE work there
            // too; there is no separate encode engine to read. When the
            // wddm backend has no encoder value, the Decoder dial is the
            // combined media engine and the Encoder dial shows N/A.
            bool mediaCombined = m.GpuBackend == "wddm" && !m.EncoderUtilPct.HasValue;
            DecoderDetail = m.HasGpu ? (mediaCombined ? "Media (dec+enc)" : "Video Decode") : "\u2014";
            EncoderDetail = m.HasGpu ? (mediaCombined ? "on Media dial" : "Video Encode") : "\u2014";

            if (m.GpuBackend == "wddm")
            {
                // v0.10.2: no OS-level PCIe counters exist off-NVML, but the
                // Copy/DMA engine (host<->VRAM transfers) is the closest
                // vendor-agnostic signal -- show it on this gauge instead of
                // a dead dial. Single-ring mode: empty Label2 hides ring 2.
                PcieCaption = "Copy";
                PcieLabel1 = "DMA";
                PcieLabel2 = "";
                PcieTxLoad = m.CopyUtilPct ?? 0;
                PcieRxLoad = 0;
                PcieTxDetail = "";
                PcieRxDetail = "";
                PcieTxRate = "";
                PcieRxRate = "";
                PcieDetail = "Host\u2194VRAM engine";
            }
            else
            {
                PcieCaption = "PCIe";
                PcieLabel1 = "TX";
                PcieLabel2 = "RX";
                // PCIe utilization: throughput as a fraction of the link's MAX
                // capability (pcie_max_*), which is fixed. The CURRENT link state
                // varies at runtime under ASPM and must not be the denominator.
                double maxBw = m.PcieMaxBandwidthKBps ?? 15760000.0; // PCIe 3.0 x16 payload, KB/s
                PcieTxLoad = Math.Clamp((m.GpuPcieTxKBps ?? 0) / maxBw * 100.0, 0, 100);
                PcieRxLoad = Math.Clamp((m.GpuPcieRxKBps ?? 0) / maxBw * 100.0, 0, 100);
                PcieTxDetail = FormatBandwidth(m.GpuPcieTxKBps);
                PcieRxDetail = FormatBandwidth(m.GpuPcieRxKBps);
                PcieTxRate = FormatBandwidth(m.GpuPcieTxKBps);
                PcieRxRate = FormatBandwidth(m.GpuPcieRxKBps);
                PcieDetail = FormatPcieLinkState(m);
            }

            var row = new TableRow
            {
                ColT = DateTime.Now.ToString("HH:mm:ss"),
                Col0 = m.GpuLoadPct.HasValue ? $"{m.GpuLoadPct:0}%" : "N/A",
                Col1 = m.GpuTempC.HasValue ? $"{m.GpuTempC} \u00B0C" : "N/A",
                Col2 = m.GpuClockMHz.HasValue ? $"{m.GpuClockMHz} MHz" : "N/A",
                Col3 = $"{Bytes(m.GpuMemUsed)} / {Bytes(m.GpuMemTotal)}",
                Col4 = $"{m.CpuLoadPct:0}%",
                Col5 = m.CpuTempC.HasValue ? $"{m.CpuTempC:0} \u00B0C" : "N/A",
                Col6 = m.CpuClockMHz.HasValue ? $"{m.CpuClockMHz} MHz" : "N/A",
                Col7 = $"{Bytes(m.RamUsed)} / {Bytes(m.RamTotal)} ({m.RamLoadPct:0}%)",
                Col8 = m.PythonWorkingSet.HasValue
                    ? $"CPU {(m.PythonCpuPct.HasValue ? m.PythonCpuPct.Value.ToString("0.0") : "\u2014")}% RSS {Bytes(m.PythonWorkingSet)}"
                    : "no python",
                Col9 = FormatBandwidth(m.GpuPcieTxKBps),
                Col10 = FormatBandwidth(m.GpuPcieRxKBps),
                Col11 = m.DecoderUtilPct.HasValue ? $"{m.DecoderUtilPct:0}%" : "N/A",
                Col12 = m.EncoderUtilPct.HasValue ? $"{m.EncoderUtilPct:0}%" : "N/A"
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
                    SetLoggingState(false);
                    MessageBox.Show($"Logging error: {ex.Message}\nLogging has been stopped.", "nvGPUMonitor Error");
                }
            }
        }

        /// <summary>
        /// Render current vs. max link state, e.g. "PCIe 4.0 x8" when running
        /// at capability, or "PCIe 1.0 x8 (max 4.0 x8)" when ASPM has
        /// downtrained the link.
        /// </summary>
        private static string FormatPcieLinkState(MetricSample m)
        {
            // PCIe link state is NVML-only; the WDDM backend (v0.10.0)
            // leaves these null and the gauge shows an em-dash.
            if (!m.PcieCurGeneration.HasValue || !m.PcieCurWidth.HasValue)
                return "\u2014";

            string cur = $"PCIe {m.PcieCurGeneration}.0 x{m.PcieCurWidth}";
            if (m.PcieMaxGeneration.HasValue && m.PcieMaxWidth.HasValue &&
                (m.PcieMaxGeneration != m.PcieCurGeneration || m.PcieMaxWidth != m.PcieCurWidth))
            {
                return $"{cur} (max {m.PcieMaxGeneration}.0 x{m.PcieMaxWidth})";
            }
            return cur;
        }

        private static string Bytes(ulong? b)
        {
            if (!b.HasValue) return "N/A";
            double v = b.Value;
            string[] units = { "B", "KB", "MB", "GB", "TB" };
            int u = 0;
            while (v >= 1024 && u < units.Length - 1)
            {
                v /= 1024;
                u++;
            }
            return $"{v:0.##} {units[u]}";
        }

        private static string FormatBandwidth(uint? kbps)
        {
            if (!kbps.HasValue)
                return "N/A";
            if (kbps < 1000)
                return $"{kbps} KB/s";
            else if (kbps < 1000 * 1000)
                return $"{kbps / 1000.0:0.##} MB/s";
            else
                return $"{kbps / (1000.0 * 1000.0):0.##} GB/s";
        }

        private static string DefaultLogDir()
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "nvGPUMonitor");
            Directory.CreateDirectory(dir);
            return dir;
        }

        private static string AutoLogFileName() =>
            $"metrics-{DateTime.Now:yyyyMMdd-HHmmss}.csv";

        /// <summary>
        /// Enforces the logging-context UI contract: exactly one of
        /// Start Log / Stop Log is enabled, and the file picker is locked
        /// while a log is being written.
        /// </summary>
        private void SetLoggingState(bool logging)
        {
            StartLogButton.IsEnabled = !logging;
            StopLogButton.IsEnabled = logging;
            LogFileTextBox.IsEnabled = !logging;
            BrowseLogButton.IsEnabled = !logging;
        }

        private void BrowseLog_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new SaveFileDialog
            {
                Title = "Choose log file",
                InitialDirectory = DefaultLogDir(),
                FileName = AutoLogFileName(),
                DefaultExt = ".csv",
                Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
                OverwritePrompt = false // overwrite is confirmed at Start Log time
            };
            if (dlg.ShowDialog(this) == true)
            {
                LogFileTextBox.Text = dlg.FileName;
            }
        }

        private void StartLog_Click(object sender, RoutedEventArgs e)
        {
            if (_logWriter != null) return;

            string text = LogFileTextBox.Text.Trim();
            string path;
            try
            {
                if (text.Length == 0)
                {
                    // No name chosen: automatic timestamped file, as before.
                    path = Path.Combine(DefaultLogDir(), AutoLogFileName());
                }
                else
                {
                    path = text;
                    // A bare filename (no folder) goes to Documents\nvGPUMonitor.
                    if (!Path.IsPathRooted(path))
                        path = Path.Combine(DefaultLogDir(), path);
                    if (string.IsNullOrEmpty(Path.GetExtension(path)))
                        path += ".csv";
                    var dir = Path.GetDirectoryName(path);
                    if (!string.IsNullOrEmpty(dir))
                        Directory.CreateDirectory(dir);
                }

                if (File.Exists(path))
                {
                    var r = MessageBox.Show(
                        "The file already exists and will be overwritten:" +
                        Environment.NewLine + path + Environment.NewLine +
                        Environment.NewLine + "Overwrite?",
                        "nvGPUMonitor", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                    if (r != MessageBoxResult.Yes) return;
                }

                _logWriter = new StreamWriter(path, append: false);
                _logWriter.WriteLine(Models.MetricSample.CsvHeader);
            }
            catch (Exception ex)
            {
                _logWriter?.Dispose();
                _logWriter = null;
                MessageBox.Show(
                    "Could not start logging:" + Environment.NewLine + ex.Message,
                    "nvGPUMonitor Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // Show the resolved full path so it is always visible while recording.
            LogFileTextBox.Text = path;
            SetLoggingState(true);
        }

        private void StopLog_Click(object sender, RoutedEventArgs e)
        {
            _logWriter?.Dispose();
            _logWriter = null;
            SetLoggingState(false);
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
