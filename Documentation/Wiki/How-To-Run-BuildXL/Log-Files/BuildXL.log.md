This is the main log file produced during a build. It contains deep diagnostic data that can be useful when debugging build issues. In particular, the following sections may be interesting:

### Pip execution information
This section shows a list of all the pips that executed in the build. Use the PipId (`PipE06EEADA7311885E`) to search for all messages associated with a pip.
```
[1:00.089] verbose DX0235: [PipE06EEADA7311885E, build.exe, MsWin.OnecoreUap.Minkernel.Crts.Crtw32.Heap.Dll_PrePass, Disabled-Release-X86] Cache miss (fingerprint '0014BC3B035CDC436F6B18A8CE1A702F76CD243C'): Process will be executed.
[1:00.647] verbose DX0223: [PipE06EEADA7311885E, build.exe, MsWin.OnecoreUap.Minkernel.Crts.Crtw32.Heap.Dll_PrePass, Disabled-Release-X86] Produced output 'e:\os\src.obj.x86fre\minkernel\crts\crtw32\heap\dll\objfre\i386\_objects.mac' hash: '8D01EC2B020C680FE95E80771FE317D51654620308826E19C4F5CC3467E5815D00'
```

### Factors limiting Build Time
In non-distributed builds, BuildXL will log a message at the end of the execute phase showing what percentage of the build was spent limited on various factors. In the below example, 72% of the 89 seconds in the execute phase was spent limited by the machine's CPU.
```
[2:15.190] -- Done executing pips in 89349 ms.
Factors limiting concurrency by build time: CPU:72%, Graph shape:0%, Disk:2%, Concurrency limit:22%, Other:4%
```

### Critical path
This section shows the longest critical path of Pip operations during the build. Assuming sufficient parallelism, this would be the lower bound to what the build time could be.

The *Pip Duration* is how long BuildXL was processing the pip. This includes cache lookup and post-processing. This will be nonzero even if the pip was a cache hit, since it takes some time to process a cache hit.

The *Exe Duration* is how long the external process was running. This will only be populated on cache misses. 

The *Queue Duration* is how long the pip has waited to acquire a slot in the scheduler. 

It also shows the when the pip is scheduled and completed and the time is displayed as relative to the build start time.

```
[0:16.488] verbose DX5017: Critical path:
Pip Duration(ms) | Exe Duration(ms)| Queue Duration(ms) | Pip Result   | Scheduled Time | Completed Time | Pip
            4849 |            2325 |                182 |              |                |                | *Total
               2 |               0 |                  0 |     Executed |       00:00:15 |       00:00:15 | Pip7998CDAE90476A13 ...
            3840 |            2325 |                  3 |     Executed |       00:00:12 |       00:00:15 | Pip1FC84BBAC24D0F71 ...
              11 |               0 |                  0 |       Cached |       00:00:12 |       00:00:12 | Pip4814212D6E796253 ...
```
### Performance Summary
This section contains a performance summary of the BuildXL run broken down by phases. This is currently only available for single machine builds.
```
[0:16.842] verbose DX0408: Performance Summary:
Time breakdown:
    Application Initialization:            6% (1sec)
    Graph Construction:                    38% (6sec)
        Checking for pip graph reuse:          6% (0sec)
        Reloading pip graph:                   93% (6sec)
        Create graph:                          0% (0sec)
        Other:                                 1%
    Scrubbing:                             1% (0sec)
    Scheduler Initialization:              4% (0sec)
    Execute Phase:                         32% (5sec)
        Executing processes                    4%
        Process running overhead:              95%
            Hashing inputs:                        3%
            Checking for cache hits:               62%
            Processing outputs:                    0%
            Replay outputs from cache:             24%
            Prepare process sandbox:               0%
            Non-process pips:                      0%
            Other:                                 11%
    Other:                                 19%

Process pip cache hits: 99% (360/361)
Incremental scheduling up to date rate: 20% (73/361)
Server mode used: False
Execute phase utilization: Avg CPU:14% Min Available Ram MB:17249 Avg Disk Active:C:30% D:0% E:4% 
Factors limiting concurrency by build time: CPU:0%, Graph shape:61%, Disk:0%, Memory:0%, ProjectedMemory:0%, Semaphore:0%, Concurrency limit:0%, Other:39%
```

### Performance Smells
This section contains information on things that are slowing down the performance of your build. If things are well tuned, this list should be empty
```
[0:29.476] verbose DX14010: ---------- PERFORMANCE SMELLS ----------
[0:29.476] verbose DX14002: No critical path info: This build could not optimize the critical path based on previous runtime information. Either this was the first build on a machine or the engine cache directory was deleted.
[0:29.476] verbose DX14004: Server mode disabled: This build disabled server mode. Unless this is a lab build, server mode should be enabled to speed up back to back builds.
[0:29.476] verbose DX14006: Cache initialization took 12002ms. This long of an initialization may mean that cache metadata needed to be reconstructed because BuildXL was not shut down cleanly in the previous build. Make sure to allow BuildXL to shut down cleanly (single ctrl-c).
[0:29.476] verbose DX14011: The /logprocesses option is enabled which causes BuildXL to capture data about all child processes and all file accesses. This is helpful for diagnosing problems, but slows down builds and should be selectively be enabled only when that data is needed.

```
