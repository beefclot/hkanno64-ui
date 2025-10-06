# HKAnnoGui (Standalone)

A minimal Windows Forms GUI that wraps `hkanno64.exe` to dump and update annotations in HKX files. The app embeds the `/hkanno` folder as resources and extracts them to a temporary directory at runtime, so the final published EXE is self-contained.

## Build (requires .NET 8 SDK on Windows)

1. Open a Developer PowerShell for VS or any shell with .NET SDK in PATH.
2. Navigate to this folder:

```
cd gui/HKAnnoGui
```

3. Restore and run (debug):

```
dotnet run -c Debug
```

4. Publish a single EXE (self-contained, x64):

```
dotnet publish "gui\HKAnnoGui\HKAnnoGui.csproj" -c Release -r win-x64
```

The output will be under `bin/Release/net8.0-windows/win-x64/publish/` and contains only `HKAnnoGui.exe`. You can copy this single `.exe` to another Windows x64 machine and run it.

## Notes
 - Native runtime dependencies are bundled inside the EXE and are extracted to a temporary location transparently at runtime (no files are placed next to the EXE).
