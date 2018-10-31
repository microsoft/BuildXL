# Microsoft Build Accelerator Preview: Windows and MacOS Sandboxes

## Introduction
Build Accelerator, BuildXL for short, is a build engine originally developed for large internal teams at Microsoft, and owned by the [Tools for Software Engineers](https://www.microsoft.com/en-us/research/project/tools-for-software-engineers/) team, part of the Microsoft internal engineering systems group. The full engine will be committed to this repo in the first half of 2019.

In the interim we are releasing a few specific portions of the code related to sandboxing processes. Several companies have requested we share this technology to provide some common code and approaches.

## Nomenclature
Glossary for some of the unfamiliar words you'll see in the code.

* A <i>pip</i> is a generic term, an acronym of Primitive Indivisible Processing, a unit of accounting in a build dependency graph. Examples include running real, sandboxed processes or process trees, or waiting for parallel graph nodes to complete.
* A <i>file probe</i> is a filesystem action that tests for a file's presence. Depending on the filesystem and OS, this may map to any of several actual API call patterns.

## Windows Sandboxing
For Windows sandboxing we utilize a much more refined and battle-tested version of the [Detours](https://github.com/Microsoft/Detours) codebase, forked a couple of years ago. Detours allows starting a process suspended and then hooking any desired set of Win32 APIs to provide callbacks into custom code, which can implement patterns like counting calls, tracking filesystem calls for accesses, redirecting filesystem paths, blocking access to paths, tracking registry usage, and so on.

Most of the core Detours file names from that repo appear in the Windows sandboxing codebase, but with numerous notes about changes made versus the original. We add a DetoursServices wrapper that implements many important layered enhancements atop the base Detours framework, including detouring a large number of Win32 APIs related to filesystem I/O, adding the ability to block accesses to paths, and reporting out directory enumerations, file probes (checking for file presence), file opens and closes, data reads, and data writes. We also properly handle transitions across 32-bit <-> 64-bit process boundaries.

The blocking capabilities are utilized by the BuildXL sandbox code to block access to disallowed paths, e.g. paths that are known to have been created by other pips that have not been declared as dependencies in the pip dependency graph.

The accounting capabilities are used for bookkeeping and post-execution rule enforcement.

In terms of performance, this implementation adds 1-5% of time overhead to running a process.

## MacOS Sandboxing
Detouring is not a viable pattern on MacOS, so we use a kernel based implementation instead, but producing similar data and blocking capabilities as noted above for Windows.

An initial implementation of the sandbox, not provided here, used KAuth + Interpose, but it ran into trouble with Interpose skipping "protected processes," and KAuth caching optimizing away some callbacks and not showing some filesystem operations.

The sandbox implementation used here is based instead on KAuth + TrustedBSD Mandatory Access Control (MAC). This implementation taps into TrustedBSD's MAC, the same subsystem used by the MacOS App Sandbox. It provides full process-tree observability and access control, including getting callbacks for all reads, writes, probes, and enumerations, plus seeing all spawn and exec calls for child process tracking.

In terms of performance, this sandbox adds anywhere from 15-40% time overhead to process execution.

## Demos
Check out these [demos](docs/demos.md) to learn the basics about BuildXL sandboxing and key features.


# Contributing 
This project welcomes contributions and suggestions.  Most contributions require you to agree to a Contributor License Agreement (CLA) declaring that you have the right to, and actually do, grant us the rights to use your contribution. For details, visit https://cla.microsoft.com.

When you submit a pull request, a CLA-bot will automatically determine whether you need to provide a CLA and decorate the PR appropriately (e.g., label, comment). Simply follow the instructions provided by the bot. You will only need to do this once across all repos using our CLA.

This project has adopted the [Microsoft Open Source Code of Conduct](https://opensource.microsoft.com/codeofconduct/).

For more information see the [Code of Conduct FAQ](https://opensource.microsoft.com/codeofconduct/faq/) or contact [opencode@microsoft.com](mailto:opencode@microsoft.com) with any additional questions or comments.
