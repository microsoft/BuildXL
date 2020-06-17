# Core concepts
This document describes the core concepts with which BuildXL models and optimizes a build. This is meant to describe how BuildXL works at a high level.

Refer to the Glossary at the end of the document for terminology.

## Build phases:
A BuildXL build consists of two core phases:

* **Evaluation**: 
  In the evaluation phase, BuildXL build specifications are *evaluated* to produce a dependency graph. Build-time tools do not execute in this phase, but source code must be available to calculate fingerprints for the graph. This may also be referred to as "Graph Construction"

* **Execution**:
  In the execution phase, BuildXL schedules a dependency graph produced in the *evaluation* phase for execution. Build specifications have already been evaluated by the time this phase starts, and thus are unable to observe the concrete outputs of any build tools. While scheduling a graph, BuildXL may determine that some artifacts are already cached based on their evaluation-time fingerprints.

The BuildXL command line supports the `/phase` parameter which controls which phases run. There are technically some more granular phases under the two listed above. By default all phases are run.

## Build graph
BuildXL represents a build's dependency information as a directed acyclic graph (DAG). Edges represent the flow of data from one build step to another. Each node in the graph - a **task** (aka 'pip') - produces one or more _artifacts_, files and directories.

The representation of tools and their dependencies as first-class artifacts is beneficial to reliability as well as the expressive power of specifications:

* Changes to build tools / SDKs result in dependent code being rebuilt.
* A checked-in or installed tool (a source artifact) can alternatively be produced from within the same build (as an output artifact). This same substitution principle also applies to compiling checked-in versus generated code.

## Deferred execution: Building the dependency graph
Since the DScript specification language is purely-functional, specifications cannot observe the outputs of pips (including success or failure indication). Instead, evaluation of a specification produces a _dependency graph_ that may optionally be scheduled for execution later. Since some parts of the generated graph may already
be up-to-date, the existence of a pip in the graph does not guarantee that it will be executed when the graph is scheduled. 

DScript specifications represent pips and their dependencies as function calls and explicit data-flow, e.g. ``var exe = link(objA, objB);``. In the mental model of specification authors, the incantation of these function calls should be seen as a declaration of *how* to build an artifact 'from scratch' (such as ``exe``), rather than as a synchronous execution of a program at that instant.

## Fingerprinting, caching, and incrementality
From the perspective of a specification author, every software artifact built using BuildXL is built from scratch. In practice, BuildXL can substitute equivalent, cached results:

* In the DScript language, a particular function will return the same value for a particular set of arguments.
* In a dependency graph, an artifact should be identical (assuming deterministic tools) so long as none of its transitive dependencies change.

Executing build tools such as compilers and linkers tends to be much more expensive than the evaluation of a build specification. As such, we will focus on BuildXL's approach to fingerprinting and caching artifacts from *process* pips.

When a process pip is executed, all declared input files to the process are available (since all dependencies must have finished executing) with known content hashes. First, BuildXL computes a _fingerprint_ for the process by hashing its static description (command-line, environment block, etc.) and input hashes (content hashes of declared inputs, such as source files). The fingerprint acts as a key to lookup a _cache descriptor_, which (if found) details the results some prior and equivalent execution - primarily, a cache descriptor records the hashes of prior outputs.

If a cache descriptor is found, and all of its required output content is available, then the prior execution as described by the descriptor is reusable. This is a _cache hit_. The actual two phase cache lookup algorithm is slightly more complicated in practice, but at a high level this is how caching in BuildXL works.

## Sandboxing
A cornerstone of BuildXL is reliable caching. This as achieved through an observation based sandbox. This allows BuildXL to verify that all of a pip's filesystem based dependencies are tracked during fingerprinting. Pips observed to consume files that are not declared as dependencies will not be eligible for caching and produce an error or warning depending on configuration.

Sandboxing is limited to observation of the machine local filesystem. Among other things, registry access, communication between processes on the machine, and communication to other machines are not tracked by BuildXL. Build graphs containing processes that access these resources may not be correctly cached by BuildXL since those dependencies are not tracked.

The sandbox implementation varies based on the platform. See the [Sandboxing](../Specs/Sandboxing.md) document for more details.


# Glossary 
* CopyFile - A primitive pip that copies a file from one location to another. The implementation is free to perform an equivalent operation, like hardlinking.
* Dependency Graph - A Graph that describes the workflow of Pips and their dependencies represented as file accesses
* Pip - Primitive Indivisible Process. The smallest unit of work tracked by BuildXL's dependency graph. Generally these are process invocations but may also include primitives like WriteFile, CopyFile
* Sandbox - In BuildXL this refers to a mechanism to observe file accesses to ensure correct caching
* WriteFile - A primitive Pip that writes content to a file. The content is determined by evaluation, not execution, of the build graph.