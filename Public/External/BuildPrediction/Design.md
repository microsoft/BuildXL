# Microsoft.Build.Prediction Design Notes
BuildPrediction is loosely based on portions of a Microsoft-internal build engine that wraps MSBuild and provides build output caching and multi-machine distribution on top of [CloudBuild](https://www.microsoft.com/en-us/research/publication/cloudbuild-microsofts-distributed-and-caching-build-service/) to speed up builds especially for mid-to-large codebases. A particular component, EnlistmentLibrary or ENL for short, reads MSBuild files and outputs a dependency graph based on both ProjectReferences and predicting file and directory inputs and outputs and cross-comparing them to find dependencies. The file and directory inputs and outputs are then fed into caching algorithms to try to find cached build outputs.

Notably, because the inputs and outputs are predicted from the XML and object model of MSBuild, custom Targets can perform opaque tasks that are not easily parsed by ENL, and newer Microsoft and third-party SDKs can add new build logic that ENL does not understand without dev work to handle the newer patterns, even a high fidelity and bug-free implementation will have prediction holes. This can and does lead to missing links in the dependency graph, requiring downstream logic to watch the actual actions of executing processes and breaking the build on accesses to the outputs of unknown dependencies. Also, because MSBuild itself did not have a native concept of ProjectReferences, and did not perform this input:output matching, executing under the Microsoft internal build engine could produce different results from running MSBuild.exe or Visual Studio locally.

With the advent of the [MSBuild Static Graph](https://github.com/Microsoft/msbuild/blob/master/documentation/specs/static-graph.md), MSBuild is taking a large step toward making proj files single-purpose, and making it possible to determine the dependency graph quickly by making ProjectReferences be the dependency graph. Pivotally, similar tracking and bookkeeping logic similar to the internal build engine's implementation is needed for Graph API to enforce that missing ProjectReferences are a build break.

BuildPrediction is designed to work with the Graph API to make it possible to provide build caching for MSBuild.

## Requirements

### Top-level Requirements
* BuildPrediction is assumed to be downstream from the MSBuild Graph API and can take a dependency on part or all of the resulting graph object model.
* BuildPrediction uses the Project as the unit of execution and caching. This matches the Graph API semantics, and matches the internal Microsoft build engine semantics. Note that this is in opposition to more complicated build models where Project files' Targets take cross-dependencies with the Targets of other Projects in a back-and-forth pattern, e.g. build "passes;" the Graph API model labels these as circular dependencies and requires that such Project graphs be unwound into a larger DAG.
* BuildPrediction must produce a set of source code file input predictions for each Project in the graph. These predictions are intended to be - but are not required to be - as close as possible to the actual list of source files read, to ensure that an initial hash of the contents of these inputs produces a different hash-of-hashes to make lookups of cached outputs more exact. However, given the current nature of caching technology, underspecified source inputs produce more inefficient, but not broken, cache lookups, and overspecification is considered an anti-dependency when the predicted files are missing. The anti-dependency becomes a dependency on the file if in the next cache check the file appeared in the filesystem, and the hash of the newly appears file feeds into the cache lookup.
* BuildPrediction must produce a set of build output directories/folders per Project where build outputs are placed. This information is fed into build execution logic to provide build output sandboxing, including output directory virtualization. Over-prediction here is not a problem.
* BuildPrediction must use all available processor cores to get its work done as fast as possible in wall clock time terms. Practically, since MSBuild Static Graph has already parsed the Project files into an object model before Prediction is invoked, generating predictions should be a CPU-intensive task, not I/O intensive. In datacenters there tend to be a very high number of cores, and most dev machines have several. Let's not keep devs waiting any longer than we have to.
* BuildPrediction must define a minimum version of MSBuild that it makes use of, but must use the ambient MSBuild provided by the user's appdomain environment to make use of the latest bug fixes and features in that version of MSBuild.
* MSBuild APIs used by BuildPrediction must be public; the MSBuild team makes a lot of effort to provide downlevel API compatibility, at least within a single major version number.
* BuildPrediction initially targets .NET Framework for compatibility with Visual Studio and the Framework flavor of MSBuild. It may be updated to provide a .NET Standard flavor depending on the need of downstream consumers, e.g. use in a .Net Core build engine on Linux and Mac.
* BuildPrediction must be shipped to users as a NuGet package, using standard SemVer versioning.
* BuildPrediction implements a set of "standard" predictors maintained within the BuildPrediciton repo and shipped in the package, but may provide a pluggable model for externally written Predictors to be added.
* BuildPrediction may provide a configuration mechanism to disable individual Predictors based on operational needs of a specific build. For example, a Predictor that is producing bad results in a specific repo might be completely turned off, or turned off in favor of a custom replacement Predictor that produces a better result for that repo.
* BuildPrediction must be executed within the scope of a single repo encompassing a specific filesystem subdirectory called the RepositoryRoot and all subdirectories below that. When a build that includes Git submodules is in use, the RepositoryRoot is typically the subdirectory containing the outermost Git repo.

### Requirements for Predictors
Predictors are implementations of logic to read a parsed Project and, possibly, the MSBuild Static Graph, and without executing any logic within the Project or changing the filesystem, produce a set of input and output predictions.

* A Predictor exists in an ecosystem of other Predictors that run at the same time.
* The scope of a predictor is defined by its implementer. It can read properties, items, targets, and other entities in the MSBuild object model and produce zero or more individual file and directory/folder predictions. It can produce a single predicted file or directory item, or it can produce a vast quantity of predictions of varying types from any or all properties of a Project. However, we encourage Predictors to be focused in their purpose: Transforming, say, a single Project property into one, a few, or no predictions allows easier unit testing and makes it easier to turn off the Predictor to snip out its limited functionality.
* Predictor instances are created and destroyed within the context of a single parsing session, and can assume that the filesystem will not change during its lifetime. A parsing session would typically manifest as parsing the current repository contents to run a single build. This can allow optimizations arising from caching filesystem contents during parsing.
* Predictors must act as pure, idempotent functions and are effective singletons: They are provided inputs in the form of configuration, a Project, and possibly the MSBuild Static Graph, and their prediction logic must be able to execute with many threads executing the prediction logic on different Projects at the same time on different threads. They must be idempotent: If executed twice on the same Project they must produce the same results. They should not store any state, except where otherwise noted.
* Each Predictor must have unit tests that cover its logic at a 90% or higher level, including exception cases. See the unit test suite in this repo for examples and approaches.

## Implementation Notes
As you would expect, any difference between this spec, which started aging poorly the moment it was written, and the actual code should use the code as reality. Patch this spec in a PR if you find notable differences.

### Predictors
Project-focused Predictors derive from [IProjectStaticPredictor](BuildPrediction/IProjectStaticPredictor.cs).

### Object Model for Predictions
See [StaticPredictions](BuildPrediction/StaticPredictions.cs) for the object produced for Project-level inputs and outputs. [ProjectStaticPredictionExecutor](BuildPrediction/ProjectStaticPredictionExecutor.cs) orchestrates parallel execution of Predictors.

### RepositoryRoot Directory and Absolute Paths
The source code for a repo is assumed to exist within a filesystem subdirectory, encompassed by the RepositoryRoot property. Build input paths are normalized to be relative to this RepositoryRoot except where the inputs are specified to be absolute paths and those paths lie outside the RepositoryRoot. Build output directories are allowed to exist outside the RepositoryRoot but must be absolute paths in this case.

Absolute paths are detected on Windows using a fully-qualified drive letter and backslash, and by a leading / for Mac and Linux.

### Unresolved Spec Issues
* Determine approach for PathTable trie usage and how it interacts with paths.

## Q&A

**Q:** Why does BuildPrediction not need to generate output file predictions, just directories/folders?
**A:** ENL, from the internal Microsoft build engine, needs to produce all inputs and outputs for cross-matching to produce a more stable and true dependency graph. With MSBuild Static Graph, the dependency graph is wholly dependent on ProjectReferences, cutting the need for this time-consuming cross-matching step. However, build execution sandboxing does need information about where the Project will be writing its outputs.

**Q:** Why not provide Target-level or Exec-level caching? Why is a Project the top-level concept?
**A:** Project-level execution and caching has proved to work well for the internal Microsoft build engine since 2011. Targets are rather under-specified even if their Inputs and Outputs collections are well written, and many Targets tend to rely on unspecified ordering and side effects in the filesystem and in-memory object model. Project-level caching has caused some onboarding pain for teams that had created complex "build passes" that created Target-level round-tripping amongst Projects, which had to be unwound into a larger DAG of single-purpose Projects, but the result has been to provide build caching on developer and datacenter machines, and multi-machine builds in the datacenter, moving single-machine builds from multiple hours down to seconds or minutes in common high cache hit rate cases, and even on cache misses providing much faster results than single-machine builds. Graph API and BuildPrediction build on that base of experience.

**Q:** This spec and the Graph API spec talk about caching and distribution, which the [CloudBuild whitepaper](https://www.microsoft.com/en-us/research/publication/cloudbuild-microsofts-distributed-and-caching-build-service/) discusses as a Microsoft internal system, but I don't see it anywhere in the public Microsoft GitHub repos. Why?
**A:** Have patience. This is but a stepping-stone to cool stuff.
