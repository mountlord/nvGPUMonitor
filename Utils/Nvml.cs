using System;
using System.Runtime.InteropServices;
namespace nvGPUMonitor.Utils
{
    internal static class Nvml
    {
        private const string DllName = "nvml.dll";
        public enum Return { NVML_SUCCESS=0, NVML_ERROR_UNINITIALIZED=1, NVML_ERROR_NOT_SUPPORTED=3, NVML_ERROR_NO_PERMISSION=8, NVML_ERROR_NOT_FOUND=5, NVML_ERROR_UNKNOWN=999 }
        [StructLayout(LayoutKind.Sequential)] public struct Utilization { public uint gpu; public uint memory; }
        [StructLayout(LayoutKind.Sequential)] public struct Memory { public ulong total; public ulong free; public ulong used; }
        public enum TemperatureSensors:uint { NVML_TEMPERATURE_GPU=0 }
        public enum ClockType:uint { Graphics=0, SM=1, Memory=2, Video=3 }
        public enum PcieUtilCounter:uint { NVML_PCIE_UTIL_TX_BYTES=0, NVML_PCIE_UTIL_RX_BYTES=1 }
        [DllImport(DllName, CallingConvention=CallingConvention.Cdecl)] public static extern Return nvmlInit_v2();
        [DllImport(DllName, CallingConvention=CallingConvention.Cdecl)] public static extern Return nvmlShutdown();
        [DllImport(DllName, CallingConvention=CallingConvention.Cdecl)] public static extern Return nvmlDeviceGetCount_v2(out uint deviceCount);
        [DllImport(DllName, CallingConvention=CallingConvention.Cdecl)] public static extern Return nvmlDeviceGetHandleByIndex_v2(uint index, out IntPtr device);
        [DllImport(DllName, CallingConvention=CallingConvention.Cdecl)] public static extern Return nvmlDeviceGetUtilizationRates(IntPtr device, out Utilization utilization);
        [DllImport(DllName, CallingConvention=CallingConvention.Cdecl)] public static extern Return nvmlDeviceGetTemperature(IntPtr device, TemperatureSensors sensorType, out uint temp);
        [DllImport(DllName, CallingConvention=CallingConvention.Cdecl)] public static extern Return nvmlDeviceGetClockInfo(IntPtr device, ClockType type, out uint clockMHz);
        [DllImport(DllName, CallingConvention=CallingConvention.Cdecl)] public static extern Return nvmlDeviceGetFanSpeed(IntPtr device, out uint speedPct);
        [DllImport(DllName, CallingConvention=CallingConvention.Cdecl)] public static extern Return nvmlDeviceGetMemoryInfo(IntPtr device, out Memory memory);
        [DllImport(DllName, CallingConvention=CallingConvention.Cdecl)] public static extern Return nvmlDeviceGetPcieThroughput(IntPtr device, PcieUtilCounter counter, out uint value);
    }
}