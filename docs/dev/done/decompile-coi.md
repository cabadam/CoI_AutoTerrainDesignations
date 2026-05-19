# Decompiling CoI Vanilla Source (`decompile-coi.ps1`)

`tools/decompile-coi.ps1` automates decompiling the four Mafi DLLs that the mod references into the standard local decompiled-source directory.

## Prerequisites

Install [ILSpy's CLI tool](https://github.com/icsharpcode/ILSpy) once as a .NET global tool:

```powershell
dotnet tool install ilspycmd -g
```

## Usage

```powershell
# From the repo root — skip any DLL whose output is already newer than the source DLL:
.\tools\decompile-coi.ps1

# Force a full re-decompile of all four DLLs (e.g. after updating ilspycmd):
.\tools\decompile-coi.ps1 -Force
```

Run the script after every CoI game update so the decompiled source reflects the new version.

## Paths

| What | Default path | Override |
| --- | --- | --- |
| Source DLLs | `%ProgramFiles(x86)%\Steam\steamapps\common\Captain of Industry\Captain of Industry_Data\Managed` | Set `CAPTAIN_INDUSTRY_MANAGED_PATH` env var |
| Decompiled output | `%APPDATA%\Captain of Industry\Mafi` | — |

The `CAPTAIN_INDUSTRY_MANAGED_PATH` env var is the same one the `.csproj` uses for build references, so if you already override it for builds you don't need to do anything extra here.

If CoI is installed in a non-standard Steam library, through Epic Games Store, or via Xbox Game Pass, the default path will not exist. Set `CAPTAIN_INDUSTRY_MANAGED_PATH` to the correct `…\Managed` directory before running the script.

## DLLs decompiled

- `Mafi.dll`
- `Mafi.Core.dll`
- `Mafi.Base.dll`
- `Mafi.Unity.dll`

Each is decompiled into its own subdirectory under the output root (e.g. `…\Mafi\Mafi.Core\`), with `--nested-directories` so the namespace tree is preserved. ILSpy also generates a `.csproj` file in each output directory; these are an artefact of the `--project` flag and are not used by the mod build — they can be safely ignored.

## Change detection

By default the script compares the DLL's last-modified time against the newest file in the matching output directory. If the output is already up to date the DLL is skipped. Pass `-Force` to bypass this check.
