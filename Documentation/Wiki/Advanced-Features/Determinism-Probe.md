# Determinism Probe
Determinism probe is a special mode that BuildXL can run under. Its goal is to determine whether child process pips deterministically produce the same output every time they are run. It should be used with the following workflow workflow:
1. User performs a BuildXL invocation to fully populate the cache
1. User performs a second BuildXL invocation with the `/determinismprobe` command line option. All inputs should be unchanged from the first BuildXL invocation. The expectation is that this second build should be capable of getting 100% cache hits.
1. The second invocation will perform cache lookups like a normal build session but it will unconditionally execute process pips regardless of cache state.
1. After executing the process pip BuildXL will attempt to add the pip to the cache. It expects to have a collision against what is already in the cache. Based on whether there is a collision, BuildXL can determine whether the tool is deterministic.

## Analyzing results
High level results are best determined through looking at counters in the `.stats` log file in the log directory or in telemetry. Look for the following counters:

* PipExecution.ProcessPipDeterminismProbeProcessCannotRunFromCache - The count of processes that were not in the cache. Because data form these pips was not in the cache, Determinism Probe was unable to provide data on these process pips
* ProcessPipDeterminismProbeSameFiles - The count of files that process pips that produced exactly the same output given the same inputs.
* ProcessPipDeterminismProbeDifferentFiles - The count of files that process pips produced that were different in spite of the pip having the same inputs. This should be zero if all pips are completely deterministic in a build.
* ProcessPipDeterminismProbeSameDirectories - Similar to ProcessPipDeterminismProbeSameFiles but for output directories.
* ProcessPipDeterminismProbeDifferentDirectories - Similar to ProcessPipDeterminismProbeDifferentFiles but for output directories.

Per-pip details can be found in the log file with events in the `DX13000` - `DX13007` range.
