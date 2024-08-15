# Observation reclassifications
## Background: observation classification
The BuildXL engine classifies dynamic observations for the sake of [fingerprinting](./Two-Phase-Cache-Lookup.md) and violation analysis. This happens in [cache lookup](./Two-Phase-Cache-Lookup.md) for every pathset, in order to produce a strong fingerprint for the lookup, and correspondingly after a pip’s execution, to produce a strong fingerprint to store in the cache.  

In this context, the operations are all “read-like” operations (hence “observed inputs”): file reads, [path probes, and directory enumerations](./Filesystem-modes-and-Enumerations.md#directory-enumerations--absent-file-probes)).. We can essentially define an operation as: 

```
Operation = Read | Enumeration | Probe 
```

The core of the observed input processor processes the dynamic observations, represented as an operation on a path, and outputs a collection of “observation types” for every path, and an associated fingerprint for the observation: 

```
[(Path, Operation)] -> [(Path, ObservedInputType, Fingerprint)] 
```

where  

```
ObservedInputType = AbsentPathProbe | ExistingDirectoryProbe | ExistingFileProbe | FileContentRead | DirectoryEnumeration
```

- AbsentPathProbe: A path was probed, but did not exist 
- FileContentRead:  A file with known content was read
- DirectoryEnumeration: A directory was enumerated 
- ExistingDirectoryProbe: An existing directory was probed for existence 
- ExistingFileProbe: An existing file was probed for existence 
 
As one can see from these definitions, the observed input type necessarily includes knowledge of the existence of the observed paths. This means that observation processing must query the file system to understand which paths exist (and further, if they are files or directories). So in reality we might represent the observation processing as: 

```
 [(Path, Operation)], FileSystemState -> [(Path, ObservedInputType, Fingerprint)] 
```

It’s easy to see how the naïve approach of just using the real file system existence would cause a very unstable result: for once, the real filesystem will vary build over build as output files first don't exist, and then later are created: it would take multiple builds to achieve stable cache hits. With this, BuildXL heuristically uses different “file system views”, which represent projections and approximations of the real state of the filesystem, in order to produce stable fingerprints across time without sacrificing build correctness. Details on the different file system views can be [found in this section](./Filesystem-modes-and-Enumerations.md)).

Turning our processing into a function: 

    [(Path, Operation)], FileSystemViews -> [(Path, ObservedInputType, Fingerprint)] 

## Configuration-based reclassifications
In some scenarios, some of the observations might want to be ignored or treated specially with respect to fingerprinting and violation analysis. We provide different mechanisms to achieve this, with various levels of granularity.
### Untracked files and scopes
A file (or a directory scope) can be flagged as `untracked`. This means BuildXL will ignore all accesses on that particular file/scope. 
### File access allowlists
File access allowlists can be specified in the [main configuration file](../Configuration.md#configuration-settings).
There are 2 types of allowlists:
- `cacheableFileAccessAllowlist` - BuildXL completely ignores accesses to these files and caches pips as though the access didn't happen. The state of the files is not checked on future cache checks.
- `fileAccessAllowlist` - BuildXL ignores the access to the file but does not add the pip to the cache. The cache will be a miss for future builds until it stops accessing the allowlisted files.

Here is an example allowlist entry to allow accesses to vsjitdebugger.exe. There are 3 fields to set on the allowlist object:
1. Name - this is a name for the allowlist entry. It is used for instrumentation to know which allowlist(s) were in use. It must be unique across all allowlist entries
1. toolpath - the full path to the tool performing the file access. This is specific within the process tree. For example, if the parent pip of the process tree is cmd.exe, but the tool performing the access to the allowlisted file is cl.exe, you must specify cl.exe for the tool path.
1. pathRegex - A regular expression describing the path that should be allowed.

```ts
    cacheableFileAccessAllowlist:
    [
        // Allow the debugger to be able to be launched from BuildXL Builds
        {
            name: "JitDebugger",
            toolPath: f`${Environment.getDirectoryValue("SystemRoot")}/system32/vsjitdebugger.exe`,
            pathRegex: `.*${Environment.getStringValue("CommonProgramFiles").replace("\\", "\\\\")}\\\\Microsoft Shared\\\\VS7Debug\\\\.*`
        }
    ]
```

4. We can also specify an allowlist entry without the toolPath or valuePath as follows:
In this case any tool that accesses the path which matches with the pathRegex is allowed.

```ts
    cacheableFileAccessAllowlist:
    [
        // Allow the debugger to be able to be launched from BuildXL Builds
        {
            name: "JitDebugger",
            pathRegex: `.*${Environment.getStringValue("CommonProgramFiles").replace("\\", "\\\\")}\\\\Microsoft Shared\\\\VS7Debug\\\\.*`
        }
    ]
```

## Processed observation reclassification
There is a way to directly influence the result of the observation processing that is carried out for every operation intercepted for a process. This can be done by specifying **reclassification rules**:

```ts
/** Observation types to define reclassification rules. The special value 'All' will match against any observation type */
type ObservationType = "AbsentPathProbe" | "FileContentRead" | "DirectoryEnumeration" | "ExistingDirectoryProbe" | "ExistingFileProbe" | "All";

interface ReclassificationRule 
{ 
    /** An optional name, for display purposes */
    name?: string; 

    /** Pattern to match against accessed paths. Mandatory. */ 
    pathRegex: string;

    /** 
     * The rule matches if the observation is resolved to any of these types.
    */ 
    resolvedObservationTypes: ObservationType[];
 
    /** 
     *  When this rule applies, the observation is reclassified to this type
     *  A value of Unit means 'ignore this observation'.
     *  Leaving this undefined will make the reclassification a no-op. 
    */
    reclassifyTo?: ObservationType | Unit;
}
```

These rules can be specified globally, in the [main configuration file](../Configuration.md), and will apply to all pips, or individually for a pip, as part of its **unsafe configuration**, for example:

```ts
Transformer.execute({
    tool: cmdTool,
    dependencies: [ ...  ],
    arguments: [ ... ],
    unsafe: 
    {
        reclassificationRules : [
            {
                name: "ExistingDirProbeIsAbsent",
                pathRegex: "C:\\\\.*\\\\OUTPUTS\\\\.*",
                resolvedObservationTypes: ["ExistingDirectoryProbe"],
                reclassifyTo: "AbsentPathProbe"
            },
            {
                name: "IgnoreAllThesePaths",
                pathRegex: "C:\\\\CACHE\\\\.*",
                resolvedObservationTypes: ["All"],
                reclassifyTo: Unit.unit(),
            }
        ],
    },
    workingDirectory: d`.`
});
```

The first rule will make the engine reclassify an existing directory probe that matches that regex to an absent probe. This rule can make the pip have cache hits even when the presence of that directory changes for different builds. But note that if a file probe matches the regular expression, it won't be reclassified. The second rule will ignore any observations under the paths that match the regex. 

Note:
- At most one rule is applied for an observation:
    - If many rules are defined, the first one that applies for an observation is applied and the rest are discarded
    - Global rules are evaluated for an observation only after individual rules 
- These rules affect the pip's (weak) fingerprint: any change in the global rules force a rerun of all the pips, and changes in the individual rules for a pip also force a rerun of that pip.

These rules must be applied with great caution, as they might cause both overbuilds and underbuilds: applying these rules means that some knowledge of what the processes are doing is lost to BuildXL, effectively negating the whole purpose of the sandboxing.
