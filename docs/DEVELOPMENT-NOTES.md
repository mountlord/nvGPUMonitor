# Development Notes

Context that lives outside the code: design rationale, field findings,
how this project is verified, and what is deliberately not done yet.
Written so a fresh contributor (human or AI) can continue from here with
nothing but this repo. Companion docs: `CHANGELOG.md` (what changed and
why, per release) and `METRICS_COLUMNS.md` (the CSV contract).

## What this program is

A Windows real-time GPU/CPU/RAM monitor (WPF, .NET 9) with donut gauges
and CSV logging. Its primary field role is instrumenting long GPU video
processing runs (hours to days): watching VRAM headroom, engine
utilization, and transfer activity while a separate Python/PyTorch
pipeline works. Design priorities in order: (1) never lie — an empty
CSV field means "could not read", a 0 is a measured zero; (2) cheap
sampling — the tick must not perturb the workload being measured;
(3) work on whatever GPU is present.

## Architecture in one paragraph

`MetricsService` produces one `MetricSample` (an immutable record) per
tick; `MainWindow` renders it and optionally appends `ToCsv()` to a log.
GPU data comes from one of two backends chosen once at startup:
**NVML** (`Utils/Nvml.cs` P/Invoke; full metric set incl. PCIe link and
throughput, temp/clock/fan) when the NVIDIA driver is present, else
**WDDM** (`Services/WddmGpu.cs`; the `GPU Engine` / `GPU Adapter
Memory` performance counters — the same source Task Manager reads —
vendor-agnostic). CPU temp/fan (and GPU temp/clock/fan on the WDDM
backend) come from the LibreHardwareMonitor WMI bridge when that app is
running; every optional sensor auto-disables after 5 consecutive
failures so an absent source costs nothing per tick. Sampling runs on a
thread-pool thread with a reentrancy guard that SKIPS (never queues) a
tick if the previous one is still running.

## WDDM backend: the non-obvious parts

- One `ReadCategory()` per category per tick, with
  `CounterSample.Calculate(prev, cur)` doing the rate math. Do NOT
  create per-instance `PerformanceCounter` objects: instances are
  per-process-per-engine, they churn with pids, and per-instance
  counters are slow to create and leak.
- "Utilization Percentage" is a two-sample rate counter — the first
  tick after startup has no engine values by design.
- Engine instance names look like
  `pid_1234_luid_0x00000000_0x0000C382_phys_0_eng_4_engtype_VideoDecode`.
  LUID identifies the adapter; engtype is matched by SUBSTRING
  (`*Decode*`, `*Encode*`, `*Copy*`) because naming varies by vendor.
- Multi-adapter: each tick the adapter (LUID) with the largest
  Dedicated Usage wins. On an iGPU+dGPU box the dGPU takes over as soon
  as a workload allocates on it. Deliberate simplification; a
  per-adapter selector is a future knob.
- VRAM total + adapter name come from the display-class driver registry
  (`HardwareInformation.qwMemorySize`, `DriverDesc`). qwMemorySize is a
  QWORD and correct above 4 GB; `Win32_VideoController.AdapterRAM` is a
  uint32 and caps at 4 GB — do not "simplify" back to WMI.
- GPU load = max engine-group utilization (Task Manager semantics).
  Consequence: on a transfer-heavy workload the GPU dial can mirror the
  Copy dial — that is correct, not a bug.

## Field findings that shaped the code (do not re-learn these)

1. **Intel media-engine accounting (v0.10.1).** Intel exposes ONE
   fixed-function media engine as a WDDM *VideoDecode* engine type and
   accounts QSV ENCODE work there too; no VideoEncode instance exists.
   Discovered on an Arc A580 running a hardware AV1 encode with decode
   forced to CPU: `decoder_util` showed exactly the expected encode
   load while `encoder_util` sat at a false 0. Hence: `encoder_util`
   on wddm is null (empty/N/A), never 0, unless an Encode engtype has
   been seen; the Decoder gauge relabels "Media (dec+enc)". On Intel
   boxes the Media dial IS the encode activity.
2. **The Copy/DMA engine matters (v0.10.2).** No OS-level PCIe counters
   exist outside NVML, so on wddm the PCIe gauge is repurposed as the
   Copy/DMA engine dial (`copy_util` column; a PERCENT, not bandwidth).
   First field capture on an Arc A580 under a 4K restoration pipeline:
   copy was the busiest engine on the GPU in 96% of samples (~63%
   mean). Ruled out paging (still ~51% at low VRAM) and media (drops
   during QSV bursts) — it was the workload's own host<->VRAM traffic.
   The gauge exists because that number changed engineering decisions
   downstream.
3. **The 0-vs-empty contract catches real bugs.** The Intel encoder
   finding was only diagnosable because a hard 0 next to a nonzero
   decoder on a CPU-decode run was a visible contradiction. Preserve
   the contract when adding columns.
4. **Bimodal encoder readings on NVML are aliasing, not a bug** — the
   driver samples over its own ~100-200 ms window; at fast ticks a
   bursty synchronous encoder legitimately reads 0-or-100.

## How this repo is verified (no-Windows development)

Much of this code was written in a Linux sandbox that cannot build WPF
(and could not reach NuGet). The working method, which caught real bugs
before they reached the field:

- Stub-compile the logic files (`MetricSample`, `Nvml`, `WddmGpu`,
  `MetricsService`) with the real C# compiler against ~80 lines of
  hand-written stubs for the Windows-only surfaces
  (`PerformanceCounter*`, `System.Management`, `Microsoft.Win32`).
  XAML/code-behind changes stay compiler-unverified until a Windows
  build, so keep them small and mechanical.
- Unit-check invariants per schema change: CSV header column count ==
  `ToCsv()` field count (quote-aware), LUID parsing consistent between
  `GPU Engine` and `GPU Adapter Memory` instance-name forms, engtype
  extraction with and without the `eng_N` infix.
- First `dotnet build` on Windows is the true gate; treat it as part of
  the review, not a formality.

## Process lessons

- **Never build a change-set on a stale base.** One drop was built from
  the GitHub zip while the working tree carried uncommitted UI work;
  file replacement clobbered it and cost a rebuild. Diff the base
  against the working tree first, or commit before integrating.
- Release checklist: bump `<Version>` in the csproj AND
  `Installer/Product-Simple.wxs` (they do not sync themselves), update
  the README version section, confirm CHANGELOG, rebuild MSI.
- Files are pure ASCII with `\uXXXX` escapes for typographic characters
  and CRLF line endings; keep it that way.
- CSV schema changes should be ADDITIVE (append columns at the end)
  whenever possible — v0.9.0's breaking change was a one-time cleanup,
  and external parsers index by position.

## Known limitations / future knobs (deliberately not done)

- **Temp/clock/fan on non-NVIDIA without LibreHardwareMonitor.** The
  proper fix is Intel Level Zero Sysman (or IGCL) P/Invoke — which
  could also provide REAL PCIe throughput on Intel
  (`zesPciGetStats`). Sysman's Windows support is historically
  partial; write a tiny standalone probe and run it on real hardware
  BEFORE committing to the integration.
- Per-adapter selection UI (current heuristic: busiest adapter wins).
- Localized (non-English) Windows counter category names are untested;
  `PerformanceCounterCategory.Exists("GPU Engine")` may behave
  differently there.
- Per-engtype CSV columns (engine discovery/debugging aid).
- CPU temp on modern desktops without LHM remains generally
  unobtainable from stock WMI; that is a Windows limitation, not ours.
