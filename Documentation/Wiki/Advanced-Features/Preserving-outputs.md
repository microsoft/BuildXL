# Preserving outputs

Normally, BuildXL deletes all declared outputs of a Pip before running it to ensure determinism. Some processes, like incremental linker, have their own up to date checks for whether to produce output files or to run their incremental logic. BuildXL cannot vouch for the correctness of their incrementality, so it conservatively deletes the outputs to make sure that those outputs are produced each time the inputs change.

However, it may be desirable to leverage certain processes' incremental behavior, if it is well understood and trusted. For example, enabling incrementality during linking can shave the run time from minutes to just seconds. There are 2 steps required to leverage this:

1. The `Unsafe.AllowPreserveOutputs` property must be set on a per-pip basis as it is added to the build graph. This setting is at the PipBuilder level. For DScript, this property is declared as `allowPreservedOutputs` field in `UnsafeExecuteArguments` of `ExecuteArguments`, the argument type passed to the `Transformer.execute`.
1. The `/unsafe_preserveOutputs` command-line option must be set on the build session. Only the pips decorated with the `Unsafe.AllowPreserveOutputs` property will get the modified behavior.

When a pip is run on the preserved-output mode, the output files that it produces are not stored to the cache as long as they will not be rewritten by downstream pips. If they are rewritten, then those outputs will be copied to the cache, instead of hardlinking them from the cache. This is done to prevent the cache from modifying those output files' timestamps (some tools' incremental logic relies on timestamps). If those output files already exist on disk, and they are hardlinked from the cache or have no writable ACL, then BuildXL first creates copies of those output files and moves them back to the original locations. This is done to unlink the hardlinks and to have writable ACL that the process launched by the pip may need. Note that the output directories are still stored to the cache.

Whether a pip is run with its outputs being preserved is included in the cache fingerprint for that pip. This means that if you perform a build with preserveOutputs enabled and a second build with it disabled, the second build will get cache misses. But cache hits are allowed going from preserveOutputs disabled to preserveOutputs enabled.

It is important to also consider BuildXL's timestamp behavior when using this feature. Tools may use timestamps as a hit for determining whether outputs are up to date. When a pip is run on the preserved-output mode, the read timestamp normalization is disabled. See [Timestamp Faking](Timestamp-Faking.md) for details.

## Compatibility
### Shared opaque directory outputs
Preserving outputs is not supported on pips that also produce files into shared opaque directories. THe limitation is because the output files of pips are essentially unknown when using shared opaque directories. Preserving output files from a previous run may cause the process to not recreate the output file and thus BuildXL would not observe and cache the output.
