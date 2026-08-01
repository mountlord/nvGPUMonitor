# Changelog

## v0.10.2 - 2026-08-02 - Copy/DMA gauge on wddm; copy_util column

No OS-level PCIe counters exist outside NVML, so on the wddm backend the
PCIe gauge was permanently dead. It is now repurposed as the **Copy/DMA
engine** dial: percent-busy of the GPU's host<->VRAM transfer engine
(`GPU Engine` types matching `*Copy*`) -- the closest vendor-agnostic
signal to PCIe traffic intensity. Caption changes to "Copy", single-ring
"DMA" mode; on NVML the gauge is unchanged (true PCIe TX/RX).

- CSV: `copy_util` appended at the END (additive; empty on nvml, where
  no equivalent NVML counter exists). Percent 0-100, NOT bandwidth.
- `DualDonutGauge`: an empty ring label now hides that ring and its
  text line (single-value mode).
- `MainWindow.xaml`: gauge caption/labels are now bound
  (`PcieCaption`/`PcieLabel1`/`PcieLabel2`).

## v0.10.1 - 2026-08-02 - Intel media-engine accounting fix

Field finding (Arc A580, QSV av1_qsv encode running, decode on CPU):
Intel exposes ONE fixed-function media engine, reported by WDDM as a
*VideoDecode* engine type, and QSV **encode** work is accounted there
too. There is no separate VideoEncode engine instance, so v0.10.0
reported `encoder_util` as a hard 0 while the encode activity showed on
the decoder value.

- `encoder_util` on wddm is now **empty (unknown), never 0**, when the
  adapter has never exposed an Encode engine type; 0 remains a real
  measured zero on adapters that do expose one.
- Engine-type matching broadened from prefix to substring
  (`*Decode*` / `*Encode*`) to cover vendor naming variants.
- UI: when encode is not separately measurable, the Decoder gauge is
  labeled `Media (dec+enc)` (it carries the combined media-engine load,
  decode + encode) and the Encoder gauge shows `on Media dial` / N/A.
- CSV schema unchanged from v0.10.0.

Reading Intel boxes: watch the *Decoder/Media* dial for QSV encode
activity; `decoder_util` is the combined media-engine utilization.

## v0.10.0 - 2026-08-02 - GPU-agnostic backend (Intel Arc / AMD support)

**CSV schema: ADDITIVE change.** Two columns appended at the END
(`gpu_backend`, `gpu_name`); all existing column positions unchanged, so
v0.9.0 parsers that index by position keep working. See
`docs/METRICS_COLUMNS.md`.

### New: WDDM fallback GPU backend

- When NVML is not present (Intel Arc, AMD, or NVIDIA without drivers),
  GPU metrics now come from the Windows WDDM performance counters
  ("GPU Engine" / "GPU Adapter Memory" -- the same source Task Manager's
  GPU view reads). Vendor-agnostic; nothing to install.
- Provided on WDDM: GPU load (busiest engine group, Task Manager
  semantics), Video Decode / Video Encode engine utilization (QSV, VCN,
  or NVDEC/NVENC alike), VRAM used (dedicated usage), VRAM total +
  adapter name (display driver registry, correct above 4 GB).
- GPU temperature / clock / fan on WDDM come from the
  LibreHardwareMonitor WMI bridge when that app is running -- the same
  mechanism already used for CPU sensors, with the same
  auto-disable-after-5-failures gate. NOTE: on this path `gpu_fan` is
  RPM (LHM), whereas the NVML value is percent-of-max.
- Not available on WDDM (empty CSV / N/A / em-dash in UI): PCIe link
  state and throughput, memory-controller utilization. These are
  NVML-only.
- Multi-adapter heuristic: each tick, the adapter with the largest
  dedicated VRAM usage is reported (an iGPU + dGPU box switches to the
  dGPU as soon as a real workload allocates on it). Per-adapter
  selection UI is a possible future knob.
- Implementation: one `ReadCategory()` per category per tick with
  `CounterSample.Calculate` rate math -- no per-instance
  `PerformanceCounter` objects (those are slow to create and leak as
  pids churn). First tick after startup has no engine rates yet
  (two-sample counters); values appear from the second tick.

### Changed

- `has_nvidia` keeps its exact v0.9.0 meaning (1 = NVML). New
  `gpu_backend` column says which backend produced the GPU columns:
  `nvml`, `wddm`, or empty (no GPU source).
- New `gpu_name` column (NVML device name or WDDM adapter name);
  CSV-quoted if it ever contains a comma.
- GPU summary line now shows the adapter name and backend, e.g.
  `Intel(R) Arc(TM) A580 Graphics [wddm]`.
- "No NVIDIA GPU (NVML not found)" now only appears when NEITHER
  backend works, reworded to "No GPU metrics source".

## v0.9.0 - 2026-07-18 - Metrics audit release

**BREAKING: CSV schema changed.** Header, column names, column count, and
empty-field semantics all changed. External parsers must be updated. See
`docs/METRICS_COLUMNS.md` for the full reference.

### Wrong data - fixed

- **PCIe max bandwidth / link state**: the utilization denominator was
  derived from the *current* negotiated link read once at startup. With ASPM
  the link is downtrained at idle (e.g. gen3 x8 on a gen4 x8 RTX 4060), so
  under load the throughput columns "exceeded" the recorded bus max. Now:
  max gen/width are read once (capability, the denominator); current
  gen/width are re-read every sample and logged separately. Includes and
  supersedes the v0.8.0 fix.
- **cpu_clock**: was `Win32_Processor.CurrentClockSpeed`, which reports the
  static rated clock on most systems (constant 3301 MHz). Now computed live
  from the `% Processor Performance` counter times the rated base clock; can
  exceed base under boost.
- **python_cpu / python_rss**: only matched processes named exactly
  `python`; venv/store/embedded interpreters (`python3`, `python3.11`,
  `pythonw`, ...) were missed, yielding a constant 0. Now prefix-matches
  `python*`. Correctly reports empty (not 0) when no interpreter is running,
  on the first tick after one appears, and when one exits mid-interval.
- **Locale safety**: CSV now written with InvariantCulture (previously a
  comma-decimal locale would corrupt the file).
- **GlobalMemoryStatusEx**: P/Invoke signature corrected (BOOL return now
  checked).

### Misleading names / semantics - renamed

- `vram_util` -> `mem_ctrl_util`: this is NVML memory-CONTROLLER busy time,
  not VRAM occupancy. Occupancy remains derivable as
  `gpu_mem_used / gpu_mem_total`.
- `gpu_pcie_tx_kbps`, `gpu_pcie_rx_kbps`, `pcie_max_bw_kbps` ->
  `*_kbytes_s`: values are kilobytes/s, never kilobits.
- `pcie_gen`, `pcie_width` -> `pcie_cur_gen`, `pcie_cur_width` (now live
  per-sample values); new `pcie_max_gen`, `pcie_max_width` columns.

### Error-handling contract

- An empty CSV field now always means "could not read / not applicable"; 0
  is always a real zero. All NVML-derived columns are empty when the read
  fails (previously they silently logged 0).

### Missing sensors - improved and documented

- `cpu_temp` / `cpu_fan`: now query the LibreHardwareMonitor /
  OpenHardwareMonitor WMI bridge first (the reliable source on modern
  desktops), falling back to ACPI thermal zone / `Win32_Fan`. Sources that
  fail 5 times in a row are disabled for the run instead of costing a WMI
  round-trip every tick.

### Sampling loop

- Sampling moved off the UI thread; only the UI update is dispatched. A
  reentrancy guard skips (never queues) a tick when the previous sample is
  still running. This removes the main source of the 75-350 ms jitter seen
  in field captures (WMI queries on the dispatcher thread).

### UI

- PCIe gauges now clamp to 100% and use link capability as the denominator.
- PCIe label shows current vs. max link state, e.g.
  `PCIe 1.0 x8 (max 4.0 x8)` when downtrained.
- Missing readings render as `N/A` instead of fake zeros; python row shows
  `no python` when no interpreter is running.

### Docs and release housekeeping

- New `docs/` folder: `docs/BUILDING.md` (authoritative build/packaging
  guide) and `docs/METRICS_COLUMNS.md` (CSV column reference).
- Removed stale `INSTALL_INSTRUCTIONS.md` and
  `Installer/MSI-BUILD-INSTRUCTIONS.md` (superseded by `docs/BUILDING.md`).
- README refreshed: corrected VRAM/PCIe metric descriptions, current
  version, links to the new docs.
- Installer version (`Product-Simple.wxs`) synced to 0.9.0.0.

## v0.8.0

- PCIe link-state fix (superseded by v0.9.0; see above).

## v0.7.0

- VRAM dial corrected to occupancy; resource/handle leak fixes; NVML
  shutdown; encoder/decoder display; branding, About window, Donate button,
  GPL v3, WiX MSI installer.
