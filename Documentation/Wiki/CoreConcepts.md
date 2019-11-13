# Core concepts

BuildXL is an internally-developed build system that reliably provides incremental, cached, parallel, and distributed builds. Unlike traditional systems such as ``msbuild`` or ``make``, BuildXL operates upon _purely-functional_ (rather than imperative) build specifications with fully described dependency information. These properties allow performance optimizations (such as building in parallel or incrementally) to be applied transparently, without extra work on the part of the specification author.

Existing systems leave optimizations visible, possibly affecting the semantics of the build. An incremental or parallel build may unexpectedly fail or produce a different result than its unoptimized equivalent. Because of the burden and uncertainty associated with this status quo, automated builds at Microsoft are often 'clean' (non-incremental) and sometimes sequential. A low-impact code change may require tens of minutes or even hours of wasted work re-building what has already been built before.

We will now describe the core concepts with which BuildXL models and optimizes a build, and subsequently how these concepts are represented in a human-authored build specification.

## Build phases: evaluation and execution
A BuildXL build consists of two separable phases:

* **Evaluation**: 
  In the evaluation phase, BuildXL build specifications are *evaluated* to produce a dependency graph. Build-time tools do not execute in this phase, but source code must be available to calculate fingerprints for the graph.

* **Execution**:
  In the execution phase, BuildXL schedules a dependency graph produced in the *evaluation* phase for execution. Build specifications have already been evaluated by the time this phase starts, and thus are unable to observe the concrete outputs of any build tools. While scheduling a graph, BuildXL may determine that some artifacts are already cached based on their evaluation-time fingerprints.

## The build graph
BuildXL represents a build's dependency information as a directed acyclic graph (DAG). Edges represent the flow of data from one build step to another. Each node in the graph - a **task** (aka 'pip') - produces one or more _artifacts_, files and directories.

The representation of tools and their dependencies as first-class artifacts is beneficial to reliability as well as the expressive power of specifications:

* Changes to build tools / SDKs result in dependent code being rebuilt.
* A checked-in or installed tool (a source artifact) can alternatively be produced from within the same build (as an output artifact). This same substitution principle also applies to compiling checked-in versus generated code. 

## Fingerprinting, caching, and incrementality
From the perspective of a specification author, every software artifact built using BuildXL is built from scratch. In practice, BuildXL can substitute equivalent, cached results:

* In the DScript language, a particular function will return the same value for a particular set of arguments.
* In a dependency graph, an artifact should be identical (assuming deterministic tools) so long as none of its transitive dependencies change.

Executing build tools such as compilers and linkers tends to be much more expensive than the evaluation of a build specification. As such, we will focus on BuildXL's approach to fingerprinting and caching artifacts from *process* pips.

When a process pip is executed, all declared input files to the process are available (since all dependencies must have finished executing) with known content hashes. First, BuildXL computes a _fingerprint_ for the process by hashing its static description (command-line, environment block, etc.) and input hashes (content hashes of declared inputs, such as source files). The fingerprint acts as a key to lookup a _cache descriptor_, which (if found) details the results some prior and equivalent execution - primarily, a cache descriptor records the hashes of prior outputs.

If a cache descriptor is found, and all of its required output content is available, then the prior execution as described by the descriptor is reusable. This is a _cache hit_.

## Deferred execution: Building the dependency graph
Since the DScript specification language is purely-functional, specifications cannot observe the outputs of pips (including success or failure indication). Instead, evaluation of a specification produces a _dependency graph_ that may optionally be scheduled for execution later. Since some parts of the generated graph may already
be up-to-date, the existence of a pip in the graph does not guarantee that it will be executed when the graph is scheduled. 

DScript specifications represent pips and their dependencies as function calls and explicit data-flow, e.g. ``var exe = link(objA, objB);``. In the mental model of specification authors, the incantation of these function calls should be seen as a declaration of *how* to build an artifact 'from scratch' (such as ``exe``), rather than as a synchronous execution of a program at that instant.
