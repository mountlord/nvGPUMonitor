# Building and Packaging nvGPUMonitor

This is the authoritative build guide. It supersedes the build snippets in
`README.md` and the removed
`Installer/MSI-BUILD-INSTRUCTIONS.md` and `INSTALL_INSTRUCTIONS.md`.

## Prerequisites

| Tool | Needed for | Notes |
|---|---|---|
| [.NET 9.0 SDK](https://dotnet.microsoft.com/download/dotnet/9.0) | Build + package | Verify with `dotnet --version` (9.x) |
| [WiX Toolset v3.14](https://wixtoolset.org/releases/) | MSI packaging only | Install to the default path; the build script finds it there |
| Windows 10/11 x64 | Everything | WPF does not build on Linux/macOS |

Git and Visual Studio are optional. Everything below works from a plain
Command Prompt or PowerShell in the repository root.

## 1. Development build (day-to-day testing)

```batch
dotnet build nvGPUMonitor.Wpf.csproj -c Release
```

Run the result:

```batch
bin\Release\net9.0-windows10.0.19041.0\nvGPUMonitor.exe
```

Or build and launch in one step:

```batch
dotnet run --project nvGPUMonitor.Wpf.csproj -c Release
```

This build requires the .NET 9 Desktop Runtime on any machine that runs it.
That is fine for development; end users get the self-contained MSI instead.

## 2. Version bump checklist (before every release)

The version lives in TWO places and both must be updated by hand:

1. `nvGPUMonitor.Wpf.csproj` - `<Version>x.y.z</Version>`
   (single source of truth for the app; the About window and title bar read
   it at runtime via reflection)
2. `Installer\Product-Simple.wxs` - `<?define ProductVersion="x.y.z.0" ?>`
   (MSI metadata; note the fourth `.0` digit - MSI versions are 4-part)

Also add an entry to `CHANGELOG.md`, and if the CSV schema changed in any
way (columns added, renamed, or semantics changed), call it out there as a
breaking change and update `docs/METRICS_COLUMNS.md`.

## 3. Package the MSI (release build)

```batch
cd Installer
BuildMSI-Advanced.bat
```

The script performs five steps automatically:

1. `dotnet publish` - self-contained win-x64 build (no runtime needed on
   the target machine)
2. Auto-detects the publish folder by locating `nvGPUMonitor.exe`
   (so the .NET target framework path never needs hardcoding)
3. `heat.exe` - harvests every published file into `HarvestedFiles.wxs`
4. `candle.exe` - compiles `Product-Simple.wxs` + harvested files
5. `light.exe` - links everything into the MSI, then cleans up
   intermediate `.wixobj` / `.wixpdb` files

Output:

```
Installer\nvGPUMonitor-Setup.msi
```

Note: `Product-Simple.wxs` is the ACTIVE installer definition.
`Product.wxs` in the same folder is not used by the build script.

## 4. Sanity checks after packaging

- Install the MSI on a clean machine or VM (default target:
  `C:\Program Files\nvGPUMonitor\`); confirm Start Menu and Desktop
  shortcuts appear.
- Launch the app; confirm the title bar / About window shows the new
  version (proves the csproj bump took).
- Check the MSI's own version: right-click the MSI, Properties, Details
  (proves the .wxs bump took).
- Start a log, let it run a minute, open the CSV from
  `Documents\nvGPUMonitor\` and confirm the header matches
  `docs/METRICS_COLUMNS.md`.
- Uninstall via Settings > Apps and confirm clean removal.

## Manual MSI build (only if the script fails)

The equivalent commands, run from the repo root:

```batch
dotnet publish nvGPUMonitor.Wpf.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false

cd Installer

heat.exe dir "..\bin\Release\net9.0-windows10.0.19041.0\win-x64\publish" ^
  -cg PublishedFiles -gg -scom -sreg -sfrag -srd ^
  -dr INSTALLFOLDER -var var.PublishDir -platform x64 ^
  -out HarvestedFiles.wxs

candle.exe Product-Simple.wxs HarvestedFiles.wxs ^
  -ext WixUIExtension ^
  -dPublishDir="..\bin\Release\net9.0-windows10.0.19041.0\win-x64\publish"

light.exe Product-Simple.wixobj HarvestedFiles.wixobj ^
  -ext WixUIExtension -out nvGPUMonitor-Setup.msi ^
  -sice:ICE61 -sice:ICE80
```

WiX tools must be on PATH for the manual route (typically
`C:\Program Files (x86)\WiX Toolset v3.14\bin`). Do not add a trailing
backslash to the `-dPublishDir` value - it breaks candle's argument parsing.

## Troubleshooting

- **`heat`/`candle`/`light` not recognized**: WiX is not installed or not
  at the default path. Install WiX v3.14 or add its `bin` folder to PATH.
- **"Could not find nvGPUMonitor.exe in publish directory"**: the publish
  step failed; scroll up in the script output for the actual dotnet error.
- **App runs from `dotnet build` but MSI-installed copy fails to start**:
  rebuild the MSI - the publish output was probably stale or from a Debug
  configuration.
- **MSI installs but shows an old version**: you bumped the csproj but not
  `Product-Simple.wxs` (or vice versa). See section 2.
