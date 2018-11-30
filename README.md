# GenCorefxOobLayout

This tools is inteded to generate a folder containing the reference assemblies from the Out of Band packages shipped from CoreFX.

In order to use this tool, you need to have a local copy of CoreFX pointing to the branch that you want to use to generate the layout for. (Should be release/2.1 or later)

Your CoreFX local copy needs to be previously built for netcoreapp by running:
```
build.cmd
```

Command Line Arguments:
```
Usage:
 GenCorefxOobLayout.exe corefxDir externalIndex [-netcoreappRef value] [-out value] [-framework value] [-rid value] [-runtimeVersion value] [-dotnetcli value] [-restoreSources value]
  - corefxDir      : Corefx Directory (string, required)
  - externalIndex  : Json index containing external dependencies (string, required)
  - netcoreappRef  : Corefx's netcoreapp ref path (string, default=artifacts\bin\ref\netcoreapp)
  - out            : Output Directory (string, default=PlatformExtensions)
  - framework      : Framework to restore external dependencies (string, default=netcoreapp3.0)
  - rid            : Runtime Identifier (string, default=win7-x64)
  - runtimeVersion : Runtime Framework Version (string, default=2.1)
  - dotnetcli      : Dotnet CLI Path (string, default=dotnet)
  - restoreSources : NuGet restore sources separated by ; (string, default=https://dotnetfeed.blob.core.windows.net/dotnet-core/index.json;https://api.nuget.org/v3/index.json)
  ```
