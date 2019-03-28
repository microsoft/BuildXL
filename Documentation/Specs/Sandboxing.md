# BuildXL Sandboxing
Process sandboxing is required by BuildXL to observe the actions of processes and, in some cases, to prevent processes from taking certain actions. Each operating system requires different approaches.

## Nomenclature
Glossary for some of the unfamiliar words you'll see in the code.

* A <i>pip</i> is a generic term, an acronym of Primitive Indivisible Processing, a unit of accounting in a build dependency graph. Examples include running real, sandboxed processes or process trees, or waiting for parallel graph nodes to complete.
* A <i>file probe</i> is a filesystem action that tests for a file's presence. Depending on the filesystem and OS, this may map to any of several actual API call patterns.
* "Domino": The internal code name for BuildXL. Its name remains in a few places in the code.

## Windows Sandboxing
For Windows sandboxing we utilize a much more refined and battle-tested version of the [Detours](https://github.com/Microsoft/Detours) codebase, forked a couple of years ago. Detours allows starting a process suspended and then hooking any desired set of Win32 APIs to provide callbacks into custom code, which can implement patterns like counting calls, tracking filesystem calls for accesses, redirecting filesystem paths, blocking access to paths, tracking registry usage, and so on.

Most of the core Detours file names from that repo appear in the Windows sandboxing codebase, but with numerous notes about changes made versus the original. We add a DetoursServices wrapper that implements many important layered enhancements atop the base Detours framework, including detouring a large number of Win32 APIs related to filesystem I/O, adding the ability to block accesses to paths, and reporting out directory enumerations, file probes (checking for file presence), file opens and closes, data reads, and data writes. We also properly handle transitions across 32-bit <-> 64-bit process boundaries.

The blocking capabilities are utilized by the BuildXL sandbox code to block access to disallowed paths, e.g. paths that are known to have been created by other pips that have not been declared as dependencies in the pip dependency graph.

The accounting capabilities are used for bookkeeping and post-execution rule enforcement.

In terms of performance, this implementation adds 1-5% of time overhead to running a process.

Technical note: The top-level process initiating Detours calls must be a 64-bit process. Detours bootstrapping code is hard-coded to start from 64-bit, matching the requirements for large memory needs for the BuildXL engine for parsing and tracking large repos.

## MacOS Sandboxing
Detouring is not a viable pattern on MacOS, so we use a kernel based implementation instead, but producing similar data and blocking capabilities as noted above for Windows.

An initial implementation of the sandbox, not provided here, used KAuth + Interpose, but it ran into trouble with Interpose skipping "protected processes," and KAuth caching optimizing away some callbacks and not showing some filesystem operations.

The sandbox implementation used here is based instead on KAuth + TrustedBSD Mandatory Access Control (MAC). This implementation taps into TrustedBSD's MAC, the same subsystem used by the MacOS App Sandbox. It provides full process-tree observability and access control, including getting callbacks for all reads, writes, probes, and enumerations, plus seeing all spawn and exec calls for child process tracking.

In terms of performance, this sandbox adds anywhere from 10-25% time overhead to process execution.

## Sandbox Demos
See the [Demos](../../Public/Src/Demos/Demos.md) page which includes sandbox projects to help understand how sandboxing works.
