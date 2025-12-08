# Creating MSI Installer for nvGPUMonitor

This guide will help you create a professional MSI installer for your nvGPUMonitor application.

## Prerequisites

1. **WiX Toolset v3.14 or later**
   - Download from: https://wixtoolset.org/releases/
   - Install the WiX Toolset build tools
   - Add WiX to your PATH (usually: `C:\Program Files (x86)\WiX Toolset v3.14\bin`)

2. **.NET 6.0 SDK** (already installed if you built the app)

## Quick Start - Automated Build

### Option 1: Simple Build (Recommended for First Time)
```batch
cd Installer
BuildMSI-Advanced.bat
```

This script will:
1. Publish your application as a self-contained executable
2. Automatically harvest ALL published files (including dependencies)
3. Compile the WiX installer
4. Create `nvGPUMonitor-Setup.msi`

### Option 2: Manual Build (More Control)

Follow these steps if you want to understand or customize the process:

#### Step 1: Publish the Application
```batch
dotnet publish nvGPUMonitor.Wpf.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false
```

This creates a self-contained application in:
`bin\Release\net6.0-windows\win-x64\publish\`

#### Step 2: Harvest Published Files (Using Heat)
```batch
cd Installer
heat dir "..\bin\Release\net6.0-windows\win-x64\publish" ^
  -cg PublishedFiles ^
  -gg -scom -sreg -sfrag -srd ^
  -dr INSTALLFOLDER ^
  -var var.PublishDir ^
  -out HarvestedFiles.wxs
```

This automatically creates a WiX file containing all your application files.

#### Step 3: Compile WiX Sources
```batch
candle Product-Simple.wxs HarvestedFiles.wxs ^
  -ext WixUIExtension ^
  -dPublishDir="..\bin\Release\net6.0-windows\win-x64\publish\"
```

This creates `.wixobj` files.

#### Step 4: Link and Create MSI
```batch
light Product-Simple.wixobj HarvestedFiles.wixobj ^
  -ext WixUIExtension ^
  -out nvGPUMonitor-Setup.msi
```

This creates the final `nvGPUMonitor-Setup.msi` file.

## Customization

### Change Product Information

Edit `Product-Simple.wxs` and modify these values:

```xml
<?define ProductVersion="1.0.0.0" ?>
<?define ProductName="nvGPUMonitor" ?>
<?define Manufacturer="Your Company" ?>
```

### Change Install Location

By default, the app installs to: `C:\Program Files\nvGPUMonitor\`

To change this, edit the `INSTALLFOLDER` in `Product-Simple.wxs`:
```xml
<Directory Id="ProgramFiles64Folder">
  <Directory Id="INSTALLFOLDER" Name="YourFolderName" />
</Directory>
```

### Add/Remove Shortcuts

The installer creates:
- Start Menu shortcut
- Desktop shortcut

To remove desktop shortcut, delete the `DesktopShortcut` component reference:
```xml
<!-- Remove this line -->
<ComponentRef Id="DesktopShortcut" />
```

## Troubleshooting

### "WiX Toolset not found"
- Install WiX from https://wixtoolset.org/releases/
- Add WiX bin folder to PATH: `C:\Program Files (x86)\WiX Toolset v3.14\bin`
- Or run from "WiX Toolset Command Prompt"

### "Failed to harvest files"
- Make sure you published the app first
- Check that the publish path exists: `bin\Release\net6.0-windows\win-x64\publish\`

### "Error LGHT0204: ICE03"
This is a warning about shortcuts. It's safe to ignore or suppress:
```batch
light.exe ... -sice:ICE03
```

### MSI won't install on target machine
- Make sure target machine is Windows 10/11 x64
- The app is self-contained, so .NET is not required

## Advanced Options

### Create MSI without Heat (Manual File List)

If you prefer to manually list files (for more control), use `Product.wxs` instead:

1. Edit `Product.wxs` and add each file manually to `ProductComponents`
2. Run: `candle Product.wxs -ext WixUIExtension`
3. Run: `light Product.wixobj -ext WixUIExtension -out nvGPUMonitor-Setup.msi`

### Sign the MSI (Code Signing)

If you have a code signing certificate:
```batch
signtool sign /f YourCertificate.pfx /p YourPassword /t http://timestamp.digicert.com nvGPUMonitor-Setup.msi
```

### Create MSI in Visual Studio

1. Add WiX project to your solution
2. Right-click solution → Add → New Project → WiX Toolset → Setup Project
3. Add the Installer files (Product-Simple.wxs, License.rtf)
4. Build the solution

## Distribution

Once you have `nvGPUMonitor-Setup.msi`:

1. **Copy to target machines** and double-click to install
2. **Install silently** from command line:
   ```batch
   msiexec /i nvGPUMonitor-Setup.msi /quiet
   ```
3. **Uninstall silently**:
   ```batch
   msiexec /x nvGPUMonitor-Setup.msi /quiet
   ```
4. **Install with log**:
   ```batch
   msiexec /i nvGPUMonitor-Setup.msi /l*v install.log
   ```

## What Gets Installed

The MSI installer will:
- Extract all application files to `C:\Program Files\nvGPUMonitor\`
- Create Start Menu shortcut
- Create Desktop shortcut
- Register with Windows Add/Remove Programs
- Support clean uninstallation

## Files Included in MSI

The installer includes:
- nvGPUMonitor.Wpf.exe (main executable)
- All .NET runtime files (self-contained)
- All dependency DLLs
- Configuration files (.json)
- All other published files

Total size: ~150-200 MB (self-contained with .NET runtime)

## Version Updates

To create a new version:
1. Update `ProductVersion` in `Product-Simple.wxs`
2. Rebuild the MSI
3. The installer will automatically detect and upgrade previous versions

Note: Keep the same `UpgradeCode` for all versions of your product!
