using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using Microsoft.Win32;

namespace nvGPUMonitor.Services
{
    /// <summary>
    /// Vendor-agnostic GPU sampling from the Windows WDDM performance
    /// counters -- the "GPU Engine" and "GPU Adapter Memory" categories,
    /// which are the same data Task Manager's GPU view reads. Works for
    /// Intel Arc, AMD, and NVIDIA alike; used as the FALLBACK backend when
    /// NVML is not available (v0.10.0).
    ///
    /// What this backend provides:
    ///   - VRAM used ("Dedicated Usage", bytes, per adapter)
    ///   - VRAM total + adapter name (display-class driver registry:
    ///     HardwareInformation.qwMemorySize / DriverDesc)
    ///   - Per-engine-type utilization summed across processes:
    ///     GPU load (max engine group, Task Manager semantics),
    ///     Video Decode, Video Encode
    ///
    /// What it cannot provide (fields stay null -> empty CSV / N/A UI):
    ///   - Temperature, clock, fan: no OS counters exist. The
    ///     LibreHardwareMonitor WMI bridge fills these when running
    ///     (MetricsService queries it, same as for CPU sensors).
    ///   - PCIe link state and throughput: NVML-only.
    ///
    /// Mechanics: one ReadCategory() per category per tick (a single
    /// registry blob read; no per-instance PerformanceCounter objects,
    /// which are costly and leak as pids churn). "Utilization Percentage"
    /// is a PERF_100NSEC_TIMER rate counter, so each value needs two
    /// samples; CounterSample.Calculate(prev, cur) does the type-correct
    /// math and the previous samples are kept per instance. The first tick
    /// after startup (or after a process appears) therefore reports
    /// engines from the second tick onward.
    ///
    /// Multi-adapter: each tick the adapter (LUID) with the largest
    /// dedicated VRAM usage wins -- on an iGPU + dGPU box the dGPU takes
    /// over the moment a real workload allocates on it. Engine sums are
    /// filtered to that adapter. (Per-adapter selection UI is a future
    /// knob; this heuristic is right for single-dGPU rigs.)
    /// </summary>
    public sealed class WddmGpu
    {
        public bool Ok { get; }
        public string AdapterName { get; } = "GPU (WDDM)";
        public ulong? VramTotal { get; }

        private readonly PerformanceCounterCategory? _engineCat;
        private readonly PerformanceCounterCategory? _memCat;

        // Previous engine samples per instance name, for rate computation.
        // Instances come and go with processes; entries for vanished
        // instances are dropped each tick to keep the map bounded.
        private Dictionary<string, CounterSample> _prevEngine = new();

        // v0.10.1: whether this adapter has EVER exposed an *Encode* engine
        // type. Intel exposes the fixed-function media block as a
        // "VideoDecode" engine and accounts QSV ENCODE work there too
        // (field-verified: Arc A580, av1_qsv encode with CPU decode -->
        // VideoDecode engine 0-45% busy, no VideoEncode instance at all).
        // When no Encode engine exists, encoder utilization is UNKNOWN
        // (null -> empty CSV / N/A), not 0, and the decode value must be
        // read as the combined media engine.
        private bool _encodeEngineSeen;

        public WddmGpu()
        {
            try
            {
                if (!PerformanceCounterCategory.Exists("GPU Adapter Memory") ||
                    !PerformanceCounterCategory.Exists("GPU Engine"))
                {
                    Ok = false;
                    return;
                }
                _memCat = new PerformanceCounterCategory("GPU Adapter Memory");
                _engineCat = new PerformanceCounterCategory("GPU Engine");

                // Probe once so a broken counter store fails here, not on
                // every tick.
                _memCat.ReadCategory();

                (AdapterName, VramTotal) = ReadAdapterInfoFromRegistry();
                Ok = true;
            }
            catch
            {
                Ok = false;
            }
        }

        /// <summary>
        /// One sample tick. Outputs are null when the value could not be
        /// read this tick (never a fake 0). copyUtil (v0.10.2) is the
        /// Copy/DMA engine group -- host&lt;-&gt;VRAM transfer activity, the
        /// vendor-agnostic stand-in for the NVML-only PCIe throughput.
        /// </summary>
        public void Sample(out double? gpuLoad, out double? decodeUtil,
                           out double? encodeUtil, out double? copyUtil,
                           out ulong? vramUsed)
        {
            gpuLoad = null;
            decodeUtil = null;
            encodeUtil = null;
            copyUtil = null;
            vramUsed = null;
            if (!Ok || _memCat == null || _engineCat == null) return;

            string luid = "";
            try
            {
                // ---- adapter memory: pick the busiest adapter's LUID ----
                var memData = _memCat.ReadCategory();
                var dedicated = FindCounter(memData, "Dedicated Usage");
                ulong best = 0;
                bool any = false;
                if (dedicated != null)
                {
                    foreach (DictionaryEntry e in dedicated)
                    {
                        var inst = (string)e.Key;
                        var d = (InstanceData)e.Value!;
                        ulong bytes = (ulong)Math.Max(0, d.RawValue);
                        any = true;
                        if (bytes >= best)
                        {
                            best = bytes;
                            luid = ExtractLuid(inst);
                        }
                    }
                }
                if (any) vramUsed = best;
            }
            catch { /* leave vramUsed null this tick */ }

            try
            {
                // ---- engine utilization, filtered to the chosen LUID ----
                var engData = _engineCat.ReadCategory();
                var util = FindCounter(engData, "Utilization Percentage");
                if (util == null) return;

                bool hadPrev = _prevEngine.Count > 0;
                var next = new Dictionary<string, CounterSample>();
                var byType = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

                foreach (DictionaryEntry e in util)
                {
                    var inst = (string)e.Key;
                    var d = (InstanceData)e.Value!;
                    var cur = d.Sample;
                    next[inst] = cur;

                    if (luid.Length > 0 && !inst.Contains(luid, StringComparison.OrdinalIgnoreCase))
                        continue;

                    // Existence check BEFORE the rate/idle filters: an idle
                    // Encode engine still proves the adapter reports encode
                    // separately (v0.10.1).
                    if (ExtractEngType(inst).Contains("Encode", StringComparison.OrdinalIgnoreCase))
                        _encodeEngineSeen = true;

                    if (!_prevEngine.TryGetValue(inst, out var prev))
                        continue; // first sighting: no rate yet

                    double v;
                    try { v = CounterSample.Calculate(prev, cur); }
                    catch { continue; }
                    if (v <= 0) continue;

                    string engType = ExtractEngType(inst);
                    byType.TryGetValue(engType, out var sum);
                    byType[engType] = sum + v;
                }
                _prevEngine = next;

                if (byType.Count == 0)
                {
                    // Valid read, all engines idle -- report a real 0, but
                    // only when there were previous samples to diff against
                    // (the first tick has no rate and must stay null).
                    // encoder: 0 only if this adapter reports encode as a
                    // separate engine; otherwise unknown (null).
                    if (hadPrev)
                    {
                        gpuLoad = 0; decodeUtil = 0; copyUtil = 0;
                        encodeUtil = _encodeEngineSeen ? 0 : (double?)null;
                    }
                    return;
                }

                double load = 0, dec = 0, enc = 0, cpy = 0;
                foreach (var kv in byType)
                {
                    double v = Math.Clamp(kv.Value, 0, 100);
                    // v0.10.1: substring match (was StartsWith) to cover
                    // engtype naming variants across vendors/drivers.
                    if (kv.Key.Contains("Decode", StringComparison.OrdinalIgnoreCase))
                        dec = Math.Max(dec, v);
                    else if (kv.Key.Contains("Encode", StringComparison.OrdinalIgnoreCase))
                        enc = Math.Max(enc, v);
                    else if (kv.Key.Contains("Copy", StringComparison.OrdinalIgnoreCase))
                        cpy = Math.Max(cpy, v); // v0.10.2: DMA engine group
                    // Overall GPU load = busiest engine group, Task Manager
                    // semantics (video engines included).
                    load = Math.Max(load, v);
                }
                gpuLoad = load;
                decodeUtil = dec;
                copyUtil = cpy;
                // No separate Encode engine on this adapter (Intel: encode
                // is accounted on the VideoDecode/media engine) -> encoder
                // utilization is not separately measurable: null, never 0.
                encodeUtil = _encodeEngineSeen ? enc : (double?)null;
            }
            catch { /* leave engine outputs null this tick */ }
        }

        // ------------------------------------------------------------------

        private static InstanceDataCollection? FindCounter(
            InstanceDataCollectionCollection data, string counterName)
        {
            foreach (DictionaryEntry e in data)
            {
                if (string.Equals((string)e.Key, counterName,
                                  StringComparison.OrdinalIgnoreCase))
                    return (InstanceDataCollection)e.Value!;
            }
            return null;
        }

        /// <summary>"pid_1234_luid_0x00000000_0x0000C382_phys_0_engtype_3D"
        /// -> "luid_0x00000000_0x0000c382" (lowered, no phys suffix).</summary>
        private static string ExtractLuid(string instance)
        {
            int i = instance.IndexOf("luid_", StringComparison.OrdinalIgnoreCase);
            if (i < 0) return "";
            int end = instance.IndexOf("_phys", i, StringComparison.OrdinalIgnoreCase);
            return (end > i ? instance.Substring(i, end - i) : instance.Substring(i))
                .ToLowerInvariant();
        }

        private static string ExtractEngType(string instance)
        {
            int i = instance.IndexOf("engtype_", StringComparison.OrdinalIgnoreCase);
            return i >= 0 ? instance.Substring(i + "engtype_".Length) : "Other";
        }

        /// <summary>
        /// Adapter name + VRAM capacity from the display-class driver
        /// registry (the same values Task Manager and dxdiag report).
        /// HardwareInformation.qwMemorySize is a QWORD and correct above
        /// 4 GB (unlike Win32_VideoController.AdapterRAM, which is a
        /// uint32 and caps at 4 GB). With several adapters, the largest
        /// VRAM wins -- consistent with the busiest-adapter heuristic for
        /// a single-dGPU box. Returns ("GPU (WDDM)", null) when the key
        /// cannot be read.
        /// </summary>
        private static (string name, ulong? total) ReadAdapterInfoFromRegistry()
        {
            const string classKey =
                @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";
            string bestName = "GPU (WDDM)";
            ulong bestMem = 0;
            try
            {
                using var k = Registry.LocalMachine.OpenSubKey(classKey);
                if (k == null) return (bestName, null);
                foreach (var sub in k.GetSubKeyNames())
                {
                    if (sub.Length != 4 || !int.TryParse(sub, out _)) continue; // 0000..000N
                    try
                    {
                        using var a = k.OpenSubKey(sub);
                        if (a == null) continue;
                        var desc = a.GetValue("DriverDesc") as string ?? "";
                        if (desc.Contains("Microsoft Basic", StringComparison.OrdinalIgnoreCase))
                            continue;
                        ulong mem = 0;
                        var qw = a.GetValue("HardwareInformation.qwMemorySize");
                        if (qw != null)
                        {
                            try { mem = Convert.ToUInt64(qw); } catch { }
                        }
                        if (mem > bestMem)
                        {
                            bestMem = mem;
                            if (desc.Length > 0) bestName = desc;
                        }
                        else if (bestMem == 0 && bestName == "GPU (WDDM)" && desc.Length > 0)
                        {
                            bestName = desc; // name even when size is unreadable
                        }
                    }
                    catch { }
                }
            }
            catch { return (bestName, null); }
            return (bestName, bestMem > 0 ? bestMem : null);
        }
    }
}
