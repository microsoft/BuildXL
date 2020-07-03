# Examples

## Find pips with highest memory consumption

```javascript
// select all process pips 
$processes := Pips[PipType ~ 'Process'] // alternative: GroupedBy.Pips.ByType.Process

> array[615]

// select 5 highest memory peaks
$memoryPeaks := $processes.ExecutionPerformance.MemoryCounters.PeakWorkingSetMb | $sort -n -r | $head -n 5

> array[5]

// select processes with those peaks
$topProcesses := $processes[#(ExecutionPerformance.MemoryCounters.PeakWorkingSetMb & $memoryPeaks) > 0]

> array[5]

// render the result in a nice JSON
$topProcesses.{PipId: PipId, Exe: EXE, Memory: ExecutionPerformance.MemoryCounters.PeakWorkingSetMb} | $toJson

> 
[
  {
    "PipId": 9281,
    "Exe": "xcodebuild [source]",
    "Memory": 729
  },
  {
    "PipId": 9367,
    "Exe": "xcodebuild [source]",
    "Memory": 809
  },
  {
    "PipId": 12113,
    "Exe": "ilc [source]",
    "Memory": 729
  },
  {
    "PipId": 24315,
    "Exe": "dotnet [output]",
    "Memory": 863
  },
  {
    "PipId": 34813,
    "Exe": "xcodebuild [source]",
    "Memory": 698
  }
]
```

## Find pips that produce shared opaque directories

```javascript
Pips[Outputs.Directories.Kind ~ "shared opaque"]

> array[5]
0:<PROCESS> BuildXL.Ide - VsCode.Client.clientCopy - rsync [{}]
1:<PROCESS> BuildXL.Ide - VsCode.Client.npmInstallResult - node [{}]
2:<PROCESS> BuildXL.Ide - VsCode.Client.compileOutDir - node [{}]
3:<PROCESS> BuildXL.Ide - LanguageService.Server.vsix - rsync [DebugDotNetCoreMac]
4:<PROCESS> BuildXL.Ide - LanguageService.Server.vsix - rsync [DebugDotNetCoreMac]
```

or

```javascript
Directories[Kind ~ "shared opaque"].Producer

> array[5]
0:<PROCESS> BuildXL.Ide - VsCode.Client.clientCopy - rsync [{}]
1:<PROCESS> BuildXL.Ide - VsCode.Client.npmInstallResult - node [{}]
2:<PROCESS> BuildXL.Ide - VsCode.Client.compileOutDir - node [{}]
3:<PROCESS> BuildXL.Ide - LanguageService.Server.vsix - rsync [DebugDotNetCoreMac]
4:<PROCESS> BuildXL.Ide - LanguageService.Server.vsix - rsync [DebugDotNetCoreMac]
```

## Find all the places the same output file is copied to

```javascript
// find all output files named 'BuildXL.Utilities.dll`
$files := Files[Kind ~ 'output'][Path ~ !(?i)/BuildXL.Utilities.dll$!]

> array[112]

// get unique file path count
$files.Path | $uniq | $count

> 112

// double check that those are all unique paths
$files.Path | $uniq | $count = #$files

> True

// sort and save those paths to a file
$files.Path | $sort |> "out-paths.txt"

> Saved 112 lines to file '.../out-paths.txt'
```