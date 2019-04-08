# Filesystem Modes
BuildXL's general algorithm for determine when tools need to rerun is to track their inputs and rerun them when the inputs change. This is pretty simple for things like files and environment variables. But it gets more complicated when a tool tries to access a file that doesn't exist or enumerates a directory. To solve some of the complexities of these scenarios, BuildXL uses a combination of the real filesystem and various build graph based virtual filesystems.

## Directory enumerations & absent file probes 
Processes are required to fully specify the input files they consume. But they are allowed to freely enumerate directories and probe for files that don't exist on disk. This policy of not needing to specify enumerations and absent file probes exists because full specification can be extremely difficult or impossible. The set of directories enumerated and absent files probed can be quite large and sometimes depends on the files that actually exist.

To be conservative, BuildXL takes the strategy of automatically tracking enumerations & absent file probes and rerunning the process if the files exist in the future or if the result of that enumeration changes. It tracks the identity of the directory by creating a `DirectoryMembershipFingerprint`. This fingerprint is calculated as part of checking the fingerprint of a process. If it changes, the process is rerun.

## Problems with the physical filesystem 
But this is problematic when processes enumerate directories that change as a result of the build. Imagine ten process pips that enumerate and then produce an output file in the same output directory. The directory fingerprint would change 10 times throughout the course of the build. This would cause cache misses more often than necessary.


## Virtual filesystems
To address the mutating directory fingerprint, BuildXL only uses the actual filesystem to compute the directory fingerprints for directories under read-only mounts. For writable mounts, BuildXL doesn't query the physical filesytem. Instead it looks at one of two virtual filesystems, depending on the configuration:

* Full graph based filesystem
* Minimal Pip filesystem

The full graph filesystem looks at all of the declared inputs, outputs, and sealed directory members to figure out what files are contained within a directory. This has the advantage of representing the existence of files that will eventually be produced in the build. This way, if a process probes for a file that is currently absent and will eventually exist, BuildXL can produce an error for the undeclared dependency. It also has the property that the directory fingerprints are stable over the course of the build.

The full graph filesystem has the problem that it may not necessarily be stable build over build. Depending on the filter provided, BuildXL may elect to only partially evaluate the build graph if the entire graph isn't needed to satisfy the filter. This poses problems for the full graph filesystem because the fingerprint for the same directory may change based on how much of the graph is included.

To address that, BuildXL may also use the Minimal Pip filesystem. It is conceptually similar to the full graph filesystem, except that it is stable when parts of the graph are excluded because it only consumes the dependencies of the pip the directory fingerprint is being computed for. This has the advantage of build over build stability when the graph is filtered (more cache hits), but it has the disadvantage of being less performant because the directory fingerprint must be recomputed for each pip that enumerates it (slower cache hit processing).

### Limitations
It is easy to imagine a process that isn't deterministic with this behavior. For example: a process that enumerates the directory contents to a file. That file would get different content depending on what files existed in the directory when the pip was run. A process may also enumerate a directory and only open produced files if they exist, skipping them if they do not. In both cases, fingerprinting the pip with the virtual filesystem is technically incorrect because it is not what the process really sees. But it's the trade-off that has been made to balance correctness and cacheability.

In practice, it would be very difficult to fully specify the time dependency of enumerations if the file names returned by the enumeration were not also consumed. This is a known gap where race conditions and non determinism can be introduced into a build. In practice, it hasn't been observed to be a problem. Remember that accessing any of the files returned by the enumeration still requires specifying the file as an input.

## Configuration

The default behavior depends on whether partial evaluation is enabled (<code>/usePartialEvaluation</code>). This is implicitly enabled for DScript based builds. When partial evaluation is used, the Minimal Pip filesystem is used for fingerprinting writable directories. When partial evaluation is not used, the Full Graph filesystem is used for writable directories.

The filesystem may be overridden by using the <code>/filesystemMode</code> option. It has the following settings:
1. RealAndMinimalPipGraph - real filesystem for non-writable mounts, Minimal Pip filesystem for writable mounts. This is the default when partial evaluation is enabled.
1. RealAndPipGraph - same as above, except uses the Full Graph filesystem instead of the Minimal Pip filesytem. Default when partial evaluation is disabled
1. AlwaysMinimalGraph - Uses the Minimal Pip filesytem for writable and non-writable mounts.
