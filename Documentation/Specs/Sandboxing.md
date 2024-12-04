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

## Linux Sandboxing

Akin to Detouring on Windows, the [dynamic loader](https://www.man7.org/linux/man-pages/man8/ld.so.8.html) allows for *library preloading* (a.k.a. function interposing) to hook various system calls.  Just like on Windows, the BuildXL sandbox leverages this feature to intercept all relevant filesystem-related system calls from the standard C library (`libc`) and handle them appropriately (e.g., report all requested accesses to the BuildXL process, block disallowed accesses, etc.).

For a full list of all interposed system calls, see [syscalls.md](/Public/Src/Sandbox/Linux/syscalls.md).

The semantics of how various high-level filesystem operations (e.g., absent probes, directory enumerations, reads, writes, etc.) are handled is expected to be the same on all supported operating systems.

A clear **limitation** of this approach is that in only applies to executables that are **dynamically linked** to `libc`.  In Linux, that is the case for the vast majority of executables.  Notable exceptions, however, are programs written in [`Go`](https://go.dev/) which are by default statically linked.

## Sandbox Demos
See the [Demos](../../Public/Src/Demos/Demos.md) page which includes sandbox projects to help understand how sandboxing works.
