# Support Summary
|               | Windows | macOS |
|-----------|:-----------:|-----------|
| File symlinks | Fully supported | Fully supported |
| Directory symlinks | Partially supported; treated like junctions. See details below | Fully supported |
| Directory junctions | Partially supported. See details below| N/A|

[[_TOC_]]

# File Symlinks
BuildXL supports file symlinks. A process pip can safely (a) consume file symlinks, including the files the symlinks point to, and (b) produce file symlinks. A copy-file pip has a limited support for copying symlinks, in the sense that the symlink to be copied should only point to a "read-only" target, i.e., the target should not be produced during the build.

## Specification

### Input symlinks
When a process pip wants to access a file via a file symlink or a chain of file symlinks, then all symlinks in the chain and the target file itself must be specified as dependencies. BuildXL enforces this requirement using its sandbox. BuildXL imposes this requirement to ease file change tracking.

### Output symlinks
A process pip can produces a file symlink, and that file symlink may point to non-existent target.

### Copying symlinks using copy-file pip
Copying a symlink using a copy-file pip means copying the final target file of that symlink. This semantics requires that the final target file must exist, otherwise the copy-file pip fails. Since the copy-file pip only has a single dependency, on copying a symlink, BuildXL also requires that the target file and all symlinks in the chain to the target file, except the symlink to be copied itself, must be read-only, i.e., they are not produced during the build. 

BuildXL imposes these requirements to avoid a race condition, which can then lead to unreliable and unpredictable builds. Suppose that BuildXL allowed the source of a copy-file to be a symlink that points to a file that is produced by another pip P. Since the copy-file pip only has a single dependency, the dependency between the copy-file pip and pip P cannot be established. If the copy-file pip executes first, then it will fail because the target file has not been produced yet.

## Hashing and tracking 

### Input symlinks
For normal files, BuildXL uses content hashes of those files to perform up-to-date checks. If the content hashes change, then the consuming and the producing pips need to execute. Because file symlinks can point to non-existent targets, BuildXL instead hashes the paths to the immediate target files, but not the final target files. If the targets of symlinks change, then the consuming pips need to execute.

With a similar reason, on tracking file symlinks, BuildXL tracks the symlinks themselves, and not the target files. Note that, since we require the chain of symlinks to be specified as dependencies, any change to one of the symlink in the chain can be detected by our file change tracker. Such a detection is important for our [Incremental Scheduling](./Incremental-Scheduling.md) feature. In the future, we may want to relax this condition by only requiring users to specify the symlink and its final target as dependencies.

### Output symlinks
Output symlinks are hashed and tracked in the same way as input symlinks. That is, BuildXL hashes the path to the immediate target of the output symlink and tracks the output symlink itself. 

Currently our cache's content addressable store does not support storing file symlinks. To replay output symlinks from cache, we include the information indicating whether a file is a symlink file as well as the path to the immediate target, if symlink, into the pip metadata. This metadata is stored in the cache, and, with this metadata, BuildXL can replay output symlinks by re-creating them using this new information.

### Copying symlinks
Before copying a symlink, BuildXL validates that the symlink does not point to a file that is produced during the build. BuildXL then discovers and track the chain of symlinks as well as the target file itself. 

## Command line configuration 
BuildXL enforces chains of symlinks, as described above, by default. One can pass the unsafe flag '/unsafe_IgnoreReparsePoints` to disable this enforcement. This flag is unsafe because BuildXL may not be able to detect any change on the middle symlink of a chain of symlinks. This issue can result in an underbuild.

# Directory symlinks

## Complications introduced by directory symlinks

Without directory symlinks, file system hierarchy is *tree*-shaped: every node (file or directory) except the root has exactly one parent node.  In presence of directory symlinks, file hierarchy becomes a *directed graph* - and not necessarily acyclic either!

Consider the following directory layout, which is the standard *framework* layout on macOS:
```
/PluginManager.framework/ 
├── PluginManager -> Versions/Current/PluginManager 
├── Resources -> Versions/Current/Resources 
└── Versions 
    ├── A 
    │   └── ... 
    ├── B 
    │   ├── PluginManager 
    │   └── Resources  
    │       └── Info.plist 
    └── Current -> B
```
File `Versions/B/Resources/Info.plist` in the hierarchy above can be reached via multiple paths; all the following paths resolve to that same file:
  - `Resources/Info.plist`  
  - `Versions/Current/Resources/Info.plist`  
  - `Versions/B/Resources/Info.plist` 

If BuildXL were oblivious to this and tracked all those paths, that could lead to several problems: 
  - **Inconsistency**: BuildXL may refer to the same file by different absolute paths at different times, propagating unnecessary confusion to its logs, telemetry, cache, user messages, etc. 
  - **Redundant file materialization**: if BuildXL were to restore the `PluginManager.framework` directory layout from the example above, and in its "Path Set" it contained all 3 different paths to file `Versions/B/Resources/Info.plist` (e.g., because that file was accesses via all those paths), BuildXL could end up materializing the same file 3 different times. 
  - **Order constraints during file materialization**: to materialize Resources/Info.plist, BuildXL would have to make sure that it first materialized both Resources and Current symlinks. 

## Solution
All the problems identified in the previous section can be mitigated by following this principle:

> BuildXL should never observe or track any paths that go through any intermediate directory symlinks. 

In other words, instead of observing path lookups requested by the process at the application level, BuildXL should observe and track *true file system-level dependencies*, which correspond to how the file system resolves path lookups. Concretely, these are the file system operations involved when looking up `Resources/Info.plist`:
```
> cd /PluginManager.framework 
> ls Resources/Info.plist 
  (readlink) /PluginManager.framework/Resources 
  (readlink) /PluginManager.framework/Versions/Current 
  (stat)     /PluginManager.framework/Versions/B/Resources/Info.plist
```
BuildXL captures each of the "readlink" and "stat" operations above and treats them as "read" dependencies.

## Prototypical use case

The most user-friendly way to use this feature is to have a *producer* pip that creates a directory layout with arbitrary symlinks inside, and declares the root of that directory as an opaque directory output. The consumer pips can then specify a single dependency on that opaque directory artifact which allows them to perform arbitrary path lookups within that directory. This use case is supported by BuildXL's ability to use dynamically observed file accesses as the real pip dependencies, so that the user doesn't have to explicitly specify them all.

In other cases, it is incumbent upon the user to specify file dependencies exactly how BuildXL will observe them, meaning declaring read access on all symlinks that may be resolved during path lookups as well as declaring permissions for the final file in terms of its physical path (one that doesn't contain any intermediate symlinks).

## Summary
  - Symlinks are treated as files (regardless of what they point to)
  - Paths observed and tracked by BuildXL never contain any intermediate directory symlinks
  - During pip process execution, every time a directory symlink is resolved, BuildXL detects that and captures the path to that directory symlink as a read dependency
  - The burden is on the user to specify pip dependencies exactly as BuildXL will observe them
  - If the user specifies a dependency via a path that contains intermediate directory symlinks:
    - BuildXL will use it as-is (i.e., BuildXL will not try to canonicalize it by resolving symlinks)
    - Actual paths observed while running a pip process will not contain any such paths, which will likely lead to BuildXL reporting file violations; for example:
      - User specifies process is allowed to read `Resources/Info.plist`
      - BuildXL runs process `ls Resources/Info.plist`
      - BuildXL observes that the process read `Resources`: that path is not specified by the user so BuildXL reports it as a read violation
      - BuildXL observes that the process read `Versions/Current`: that path is not specified by the user so BuildXL reports it as a read violation
      - BuildXL observes that the process read `Versions/B/Resources/Info.plist`: that path is not specified by the user so BuildXL reports it as a read violation.

# Junctions
In builds, particularly BuildXL builds, junctions are mostly used to avoid changing specification files and, in turn, make a previously built pip graph reusable. For example, one can create a junction from a NuGet package directory `NuGetCache\PackageX` to a directory `NuGetCache\PackageX-1.0` where all files of `PackageX` version `1.0` are located. All paths referring to `PackageX` in the spec files are in terms of the unversioned path `NuGetCache\PackageX`. If the user wants to test a new version of `PackageX-2.0`, then the user simply re-routes the junction from `NuGetCache\PackageX` to `NuGetCache\PackageX-2.0` without changing the spec files.

## Supported scenarios
BuildXL has limited support for junctions. BuildXL currently only supports input file accesses via junctions. BuildXL does not support junction productions. 
For input file accesses via junctions, BuildXL handles junctions that cross volume boundaries, e.g., a junction from `X:\A\B` to `Y:\A\B`. However, BuildXL does not infer and track all incarnations of paths that are caused by junctions. For example, given a junction `D` to `D'`, where `D'` can also be a junction, if BuildXL is requested to track a file `D\f.txt`, then it just tracks `D\f.txt` and not `D'\f.txt`. Thus, any change to `D'`, like re-routing junction target if `D'` is a junction, will not be detected by BuildXL.

## Directory translations
Accessing files via junctions can cause file access violations. Let's consider again the junction from `NuGetCache\PackageX` to a directory `NuGetCache\PackageX-1.0`. The tool that accesses files in that package executes may open a file by specifying a path containing `NuGetCache\PackageX-1.0` (e.g., the tool calls `GetFinalPathByHandle`). However, because the spec file do not contain paths containing `NuGetCache\PackageX-1.0`, BuildXL will report a file access violation.

To resolve this issue, BuildXL provides a directory translation feature. In the above case, the user specifies `/translateDirectory:NuGetCache\PackageX-1.0<NuGetCache\PackageX` in the command line. With this directory translation, whenever BuildXL sees a path containing `NuGetCache\PackageX-1.0`, like `NuGetCache\PackageX-1.0\f.txt`, it modifies it into `NuGetCache\PackageX\f.txt`. Now, since `NuGetCache\PackageX\f.txt` is specified in the spec file, BuildXL will no longer see file access violations.

One can create a chain of directory translations, and BuildXL will validate that the translations are acyclic. For example, suppose that `D` is a junction to `D'`, which in turn a junction to `D''`, and all paths in the spec file are in term of `D`.  One can then have the directory translations `/translateDirectory:D''<D'` and `/translateDirectory:D'<D` to resolve file access violations. Given a path, if two directory translations are possible, BuildXL respect the order as these translations are specified in the command line argument.

## Junction tracking implementation
Internally BuildXL maintains a map from `FileID` to `(Path, USN)` for tracking existing files and directories. In tracking a file `A\B\C\f.txt`, BuildXL tracks the file as well as its parent directories. That is, the tracker will have the following mappings:
```
FileID(A) -> (A, USN(A))
FileID(A\B) -> (A\B, USN(A\B))
FileID(A\B\C) -> (A\B\C, USN(A\B\C))
FileID(A\B\C\f.txt) -> (A\B\C\f.txt, USN(A\B\C\f.txt))
```

These mappings allow BuildXL to detect changes to the file itself as well as its parent directories. When a file is accessed via a junction, BuildXL currently does not track all incarnations of the file paths. For example, suppose that the directory `A\B` is a junction to `A\B'`. The file `A\B\C\f.txt` can be reached via `A\B\C\f.txt` itself or via `A\B'\C\f.txt`.
If BuildXL is asked to track `A\B\C\f.txt`, then we will have the same mappings as above; there is no mapping `FileID(A\B') -> (A\B', USN(A\B'))`, nor mapping `FileID(A\B'\C) -> (A\B'\C, USN(A\B'\C))`, nor mapping `FileID(A\B'\C\f.txt) -> (A\B'\C\f.txt, USN(A\B'\C\f.txt))`. 

If the user modifies `A\B'\C\f.txt`, then BuildXL will detect the change because the `FileID`s of `A\B'\C\f.txt` and `A\B\C\f.txt` are the same. If the user re-routes the junction `A\B` to another directory, say `A\B''`, then BuildXL will detect it as well because there is a mapping from `FileID(A\B)` in the tracker. Such a change will also cause the tracker to treat all  paths underneath `A\B` as being removed.

There is a little quirk in the behavior of our tracker. When the junction `A\B` is re-routed to `A\B''`, the tracker still maintain the mapping `FileID(A\B\C\f.txt) -> (A\B\C\f.txt, USN(A\B\C\f.txt))`. After the re-routing, `FileID(A\B\C\f.txt)` is different from what we have in the map. That is, now we have in the map the following mappings:
```
FileID(A\B\C\f.txt) -> (A\B\C\f.txt, USN(A\B\C\f.txt)) -- before re-route, where FileID(A\B\C\f.txt) == FileID(A\B`\C\f.txt)
FileID(A\B\C\f.txt) -> (A\B\C\f.txt, USN(A\B\C\f.txt)) -- after re-route, where FileID(A\B\C\f.txt) == FileID(A\B'`\C\f.txt)
```
The `FileID`s and `USN`s are different for before and after re-route, but the paths are identical. Now, although `A\B'\C\f.txt` is no longer referred during the build, but if someone modifies it, then the tracker will be notified that the path `A\B\C\f.txt` has changed because `FileID(A\B'\C\f.txt)` is still in the map. This issue causes overbuild.

The fact that BuildXL does not track all incarnations of file paths can lead to underbuilding. Let's consider another example involving a chain of junctions. Suppose that `D` is a junction to `D'`, which in turn a junction to `D''`. In tracking`D\f.txt`, BuildXL will have the following mappings in its tracker:
```
FileID(D) -> (D, USN(D))
FileID(D\f.txt) -> (D\f.txt, USN(D\f.txt))
```
Note that the junctions `D` and `D'` have different `FileID` because they are two different "files" in principle. Thus, if the user re-routes `D'` to `D'''`, then BuildXL will not detect this change, and the resulting build is an underbuild.
