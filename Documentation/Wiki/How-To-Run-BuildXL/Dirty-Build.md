# BuildXL Dirty Builds

BuildXL aims to create correct, deterministic builds. This behavior however can come at a tradeoff for speed. Sometimes it's helpful to explicitly perform an incorrect build, if the correctness doesn't matter.

BuildXL supports two options to tune the "dirtiness" of the build.  Both are disabled by default and should be used with extreme care, as implied by the *unsafe* prefix to the controlling arguments.

## Force Skipping Dependencies

The `/unsafe_forceSkipDeps` option allows can be leveraged to make build iterations on a project without bringing its dependencies up to date. This can be desirable if rebuilding the dependencies is very expensive and changes to them do not matter.

For example: You might change a header file that's consumed by the project you care about, but also all of its dependencies. If you only care about the manifestations of that change in your project, you can target it directly without bringing all of the dependencies up to date.

The option works by ignoring changes to inputs of pips that are not explicitly matched by the filter. If the outputs of those pips exist on disk, they will be used regardless of whether they are correct. Only the Pips explicitly matching the filter will have their inputs considered when determining whether to run the Pip.

If the upstream dependencies' outputs do not exist on disk, they will still be run to produce the output files needed by downstream consumers. So the build will always have all required files present, but they are not guaranteed to be correct.
