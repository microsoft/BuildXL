# Why BuildXL?
Microsoft has many products with massive code repositories that are under active development. These products use build tools such as Nmake and MSBuild along with homegrown wrappers that add various customizations and optimizations. The wrappers and languages were not meant to be distributed across machines, meaning single massive and massively expensive machines were used for build. As an example, in 2016, code forks of the Office client took about 90 hours to build and release to dogfood. To see their code in action, engineers making changes had to wait up to 90 hours for the build to complete. This caused dissatisfaction and reduced Office's ability to quickly respond to customer critical situations. BuildXL was born out of the need to build efficiently at the scale of Office and Windows.

## Performance

### Reliable Incremental builds
BuildXL constructs a directed acyclic graph ([DAG](http://en.wikipedia.org/wiki/Directed_acyclic_graph)) of build dependencies, making it possible for BuildXL to execute only the parts of the build that were dependent on the change made. Other tools such as MSBuild provided some limited forms of incremental build. Given their incomplete understanding of inputs and outputs of build tasks, other tools tend to be fragile with respect to some kinds of file operations, especially those affecting file timestamps. BuildXL can augment these build languages and track inputs and outputs in order to cache and distribute existing projects.

### Caching
The public version of BuildXL has local caching: storing build graphs, filesystem state, and previous build outputs locally in a disk region for later reuse. See the [Cache](../../Public/Src/Cache/README.md) page for more details.

BuildXL is also designed to take advantage of multiple cache layers. Inside Microsoft, the "dev cache" provides build outputs from datacenter based builds in [CloudBuild](https://www.microsoft.com/en-us/research/publication/cloudbuild-microsofts-distributed-and-caching-build-service/) and other build systems. And within each datacenter, a peer-to-peer cache passes cached artifacts between machines at high speed.

Currently there are no public versions of the dev or datacenter caches.

### Distribution
Distribution is fundamentally built into BuildXL. Since BuildXL requires all the inputs and outputs to be explicitly defined in BuildXL build specs, it is possible for BuildXL builds to minimize the amount of code that must be "copied" to a worker and optimize the flow of results between them. BuildXL thus becomes an ideal choice for shaving time off long running builds run on build server pools.

## Correctness
BuildXL hashes file contents, command-line arguments, environment variables, and other factors to determine if previously built build outputs can be reliably reused. Because of BuildXL'a ability to deeply understand dependencies, it always builds the minimum amount to get a fully consistent build. No more cherry-picking what directories to build based on the change you make. No more forgetting about a dependency you needed to build resulting in inconsistent binaries and wasted time debugging. No more custom specialized build scripts maintained by individual developers and teams. No more clean rebuilds. Developers and build labs can confidently rely on incremental builds - and gracefully recovering from canceled builds.

BuildXL's DAG has varying levels of input and output declaration for wrapping a process execution (a *pip* in BuildXL parlance).

### Input Strictness
BuildXL uses the [Cache](../../Public/Src/Cache/README.md) subsystem's two-phase cache lookup pattern to take the best initial guesses on inputs and select amongst the possible sets of outputs based on comparing current filesystem state to that stored in cache entries. This works well if the inputs predicted for the pip include all or most of the files and directories that would be read if the process were to run, especially the files that are likely to change often. Inaccurate input predictions lead to lots of filesystem state comparisons but can still get a cache hit.

Amongst the frontends, DScript is the most strict and typically leads to a build break on incorrect inputs to force improvements to input predictions. MSBuild and CMake have much less certain input predictions, and rely much more on two-phase caching for correctness.

### Output Strictness
In the ideal case, an exact mapping of input files to output files leads to very predictable builds. Build predictions arising from the DScript language generally have this property and must meet stringent requirements or else the build breaks to make the DScript owner improve predictions.

MSBuild and CMake DAG generation and input-output predictions produce much less certain results. These frontends generally make much less precise output predictions, for MSBuild at the proj file level, for CMake at the Ninja target level. For these cases, the build engine can employ less strict semantics when watching for process outputs, employing more dynamic post-execution bookkeeping for each process, but this can lead to over-building if the engine runtime cannot narrow its predictions enough to avoid rebuilding everything that is known to output to a subdirectory tree.
