namespace nvGPUMonitor.Models
{
    public class TableRow
    {
        public string ColT { get; set; } = "—"; // Time
        public string Col0 { get; set; } = "—"; // GPU Load
        public string Col1 { get; set; } = "—"; // GPU Temp
        public string Col2 { get; set; } = "—"; // GPU Clock
        public string Col3 { get; set; } = "—"; // VRAM
        public string Col4 { get; set; } = "—"; // CPU Load
        public string Col5 { get; set; } = "—"; // CPU Temp
        public string Col6 { get; set; } = "—"; // CPU Clock
        public string Col7 { get; set; } = "—"; // RAM
        public string Col8 { get; set; } = "—"; // Py Aggregate
        public string Col9 { get; set; } = "—"; // PCIe TX
        public string Col10 { get; set; } = "—"; // PCIe RX
    }
}
