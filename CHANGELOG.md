# Changelog

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

### Docs and release housekeeping

- New `docs/` folder: `docs/BUILDING.md` (authoritative build/packaging
  guide) and `docs/METRICS_COLUMNS.md` (CSV column reference).
- Removed stale `INSTALL_INSTRUCTIONS.md` and
  `Installer/MSI-BUILD-INSTRUCTIONS.md` (superseded by `docs/BUILDING.md`).
- README refreshed: corrected VRAM/PCIe metric descriptions, current
  version, links to the new docs.
- Installer version (`Product-Simple.wxs`) synced to 0.9.0.0.

### UI

- PCIe gauges now clamp to 100% and use link capability as the denominator.
- PCIe label shows current vs. max link state, e.g.
  `PCIe 1.0 x8 (max 4.0 x8)` when downtrained.
- Missing readings render as `N/A` instead of fake zeros; python row shows
  `no python` when no interpreter is running.

## v0.8.0

- PCIe link-state fix (superseded by v0.9.0; see above).

## v0.7.0

- VRAM dial corrected to occupancy; resource/handle leak fixes; NVML
  shutdown; encoder/decoder display; branding, About window, Donate button,
  GPL v3, WiX MSI installer.
