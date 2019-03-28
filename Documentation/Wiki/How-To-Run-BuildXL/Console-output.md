BuildXL's console output can help you understand how far along your build is and what's currently being run.

## Fancy Console
Below is an example of BuildXL's "FancyConsole" output. This is controlled by the `/fancyconsole` flag which is on by default
```
Microsoft (R) BuildXL Build Tool. Version: [Developer Build]
Copyright (C) Microsoft Corporation. All rights reserved.

[0:01] -- Starting viewer @ http://localhost:9700/Domino/101400/ (async)
[0:06] -- Using pip filter: ~(dpt(spec='private\Guests\WDG\*'))and~(spec='private\internal\src\transformers\*')
[1:52] 87.21% - 409 done (398 hit), 4 executing, 54 waiting. CPU:86% RAM:73% F:21%
   [1:10] BuildXL.Core.UnitTests - Engine.dll - XUnitWrapper.exe
   [0:58] BuildXL.Private.Core.UnitTests - Processes.Detours.test_Domino_Processes_Detours_x86_dll - XUnitWrapper.exe
   [0:57] BuildXL.Private.Core.UnitTests - Processes.Detours.test_Domino_Processes_Detours_x64_dll - XUnitWrapper.exe
   [0:24] BuildXL.Private.Core.UnitTests - Processes.test_Domino_Processes_dll - XUnitWrapper.exe
```

### Understanding output
This line shows the overall build status. It gets updated in place every few seconds
```
[1:52] 87.21% - 409 done (398 hit), 4 executing, 54 waiting. CPU:48% RAM:62% D:53%
```
Reading left to right, the line first describes the Process [pips](/BuildXL/BuildXL-Under-the-Hood/Core-Concepts) in the build
* `87.21% - 409 done (398 hit` - 409 Process pips are done. 398 of those were cache hits. The percentage represents the overall number of process pips that are done out of the total for what is filtered in for the build. Cache hits/misses are given equal weight.
* `6 executing` - There are 6 pips that are currently executing. Details on those processes are below
* `54 waiting` - This is the count of pips that need to execute before the build can complete.
* `CPU:48% ` - This shows the CPU utilization of the machine during that period.
* `RAM:62%` - 62% of the machine's physical RAM was being consumed.
* `D:53%` - This shows the percent active time of the most active logical disk. In this case the D drive had 53% active time. This can switch between drives.

Below that line you will see details of the process pips currently being run:
```
   [1:10] BuildXL.Core.UnitTests - Engine.dll - XUnitWrapper.exe
   [0:24] BuildXL.Private.Core.UnitTests - Processes.test_Domino_Processes_dll - XUnitWrapper.exe
```
Here, it's showing that the `XUnitWrapper.exe` process, from the `Engine.dll` value in the `BuildXL.Core.UnitTests` package has been executing for 1 minute, 10 seconds (wall clock time).

### Known issues
* Flickering output - If your machine is heavily loaded, console updates can happen slowly, causing FancyConsole to be hard to read. You can disable the mode as described below, or you can throttle the build to not run so many parallel tasks to make UI more responsible
* Line wrapping - The lines representing the running process pips are based on the Pip's description, which has user specified components. These can get long, causing lines to wrap. These descriptions can be shortened, or the console width can be increased.

## Simple Output
If FancyConsole isn't your cup of tea, you can go to a console that doesn't update lines by passing `/fancyConsole-`. This option disabled the console rewriting and prints the BuildXL progress in consecutive lines.