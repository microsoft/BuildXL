# Microsoft Build Accelerator Preview: Windows and MacOS Sandboxes



## Introduction
Build Accelerator, BuildXL for short, is a build engine originally developed for large internal teams at Microsoft, and owned by the [Tools for Software Engineers](https://www.microsoft.com/en-us/research/project/tools-for-software-engineers/) team, part of the Microsoft internal engineering systems group. BuildXL runs 30,000+ builds per day on mono-repo codebases up to a half-terabyte in size with a half-million pips (see below) per build, using distribution to thousands of datacenter machines and petabytes of source code, package, and build output caching.

The full engine will be committed to this repo in 2019. In the interim we are releasing a few specific portions of the code related to sandboxing processes. Multiple developers in the engineering systems community outside of Microsoft have requested we share this technology and allow us to work together on common approaches.

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

In terms of performance, this sandbox adds anywhere from 15-40% time overhead to process execution.

## Demos
Check out these [demos](docs/demos.md) to learn the basics about BuildXL sandboxing and key features.

# Building the Code

## Build Status - Azure DevOps Pipelines
[![Build Status](https://dev.azure.com/mses/BuildXL/_apis/build/status/Microsoft.BuildXL)](https://dev.azure.com/mses/BuildXL/_build/latest?definitionId=1)

## Windows
You should use [Visual Studio](https://visualstudio.microsoft.com/vs/), e.g. Community Edition, to build with MSBuild. You need to install the ".NET desktop development", "Desktop development with C++" and ".NET core cross-platform development" workloads. Additionally, the Windows code in this release contains a hard-coded, required dependency on Windows SDK version 10.0.10240.0. The BuildXL team will be updating the SDK version over time, but upgrading the SDK now will result in build errors.

To install this SDK version in Visual Studio:

1. Open the Visual Studio Installer
1. Click Modify, then click the Individual Components tab.
1. Scroll to near the bottom of the list and add a check next to "Windows SDK (10.0.10240.0)".
1. Click Modify and wait for installation to complete.

## MacOS
1. Install [.Net Core for macOS](https://www.imore.com/how-turn-system-integrity-protection-macos)
1. Turn off System Integrity Protection in macOS. SIP blocks the installation of the unsigned kernel extension (or Kext) produced by the build. [Instructions](https://www.imore.com/how-turn-system-integrity-protection-macos)
1. Run ```./macbuild.sh [Release]``` in your terminal emulator

# Contributing 
This project welcomes contributions and suggestions.  Most contributions require you to agree to a Contributor License Agreement (CLA) declaring that you have the right to, and actually do, grant us the rights to use your contribution. For details, visit https://cla.microsoft.com.

When you submit a pull request, a CLA-bot will automatically determine whether you need to provide a CLA and decorate the PR appropriately (e.g., label, comment). Simply follow the instructions provided by the bot. You will only need to do this once across all repos using our CLA.

This project has adopted the [Microsoft Open Source Code of Conduct](https://opensource.microsoft.com/codeofconduct/).

For more information see the [Code of Conduct FAQ](https://opensource.microsoft.com/codeofconduct/faq/) or contact [opencode@microsoft.com](mailto:opencode@microsoft.com) with any additional questions or comments.
