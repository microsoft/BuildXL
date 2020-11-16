# Microsoft Build Accelerator

## Guide to Documentation
This is the primary documentation for Microsoft Build Accelerator (BuildXL). If you are an internal Microsoft employee, you may also want to visit the [BuildXL Internal](https://aka.ms/buildxl) documentation where you'll find documentation about interactions with systems that are not publicly available.

Keep this as the sole primary landing page for documentation and avoid creating nested navigation pages for navigation.

# Overview
* [ReadMe](../README.md)
* [Why BuildXL?](Wiki/WhyBuildXL.md)
* [Demos](../Public/Src/Demos/Demos.md)

# Project Documentation
* [Release Notes](Wiki/Release-Notes.md)
* [Installation Instructions](Wiki/Installation.md)
* [Developer Guide](Wiki/DeveloperGuide.md)
* [Development Productivity Tips and Tricks](Wiki/ProductivityTipsAndTricks.md)
* [Code of Conduct](../CODE_OF_CONDUCT.md)
* [Security](../SECURITY.md)
* [Contributing](../CONTRIBUTING.md)

# Product Documentation
## Architecture
* [Core Concepts and Terminology](Wiki/CoreConcepts.md)
* [Frontends](Wiki/Frontends.md)
* [Sandboxing](Specs/Sandboxing.md)

## Setting up a build
* [Configuration](Wiki/Configuration.md)
* [Modules](Wiki/Modules.md)
* [Command line](Wiki/How-to-run-BuildXL.md)
* [Mounts](Wiki/Advanced-Features/Mounts.md)
* [Build Parameters (Environment Variables)](Wiki/Advanced-Features/Build-Parameters-(Environment-variables).md)

## Build Execution
* [Filtering](Wiki/How-To-Run-BuildXL/Filtering.md)
* [Graph Reuse](Wiki/Advanced-Features/Graph-Reuse.md)
* [Content and Metadata Cache](../Public/Src/Cache/README.md)
* [Paged Hashes](Specs/PagedHash.md)
* [Filesystem modes and enumerations](Wiki/Advanced-Features/Filesystem-modes-and-Enumerations.md)
* [Incremental Scheduling](Wiki/Advanced-Features/Incremental-Scheduling.md)
* [Cancellation](Wiki/How-To-Run-BuildXL/Cancellation-(CtrlC).md)
* [Resource tuning](Wiki/How-To-Run-BuildXL/Resource-Usage-Configuration.md) 
* [Pip Weight](Wiki/Advanced-Features/Pip-Weight.md) 
* [Scheduler Prioritization](Wiki/Advanced-Features/Scheduler-Prioritization.md)
* [Server Mode](Wiki/Advanced-Features/Server-Mode.md) 
* [Timestamp Faking](Wiki/Advanced-Features/Timestamp-Faking.md)
* [Symlinks and Junctions](Wiki/Advanced-Features/Symlinks-and-Junctions.md)
* [Service Pips](Wiki/Service-Pips.md)
* [Pip requested file materialization](Wiki/External-OnDemand-File-Materialization-API.md)
* [Determinism Probe](Wiki/Advanced-Features/Determinism-Probe.md)
* [Source Change Affected Inputs](Wiki/Advanced-Features/Source-Change-Affected-Inputs.md)
* [Dirty Build](Wiki/How-To-Run-BuildXL/Dirty-Build.md)
* [Unsafe Flags](Wiki/How-To-Run-BuildXL/Unsafe-flags.md)
* [Incremental Tools](Wiki/Advanced-Features/Incremental-tools.md)
* [Preserve Outputs](Wiki/Advanced-Features/Preserving-outputs.md)
* [Process Timeouts](Wiki/Advanced-Features/Process-Timeouts.md)
* [Sealed Directories](Wiki/Advanced-Features/Sealed-Directories.md)
* [Search Path Enumeration](Wiki/Advanced-Features/Search-Path-Enumeration.md)
* [Escaping the sandbox](Wiki/Advanced-Features/Process-breakaway.md)

## Logging and Analysis
* [Console Output](Wiki/How-To-Run-BuildXL/Console-output.md)
* [Log Files](Wiki/How-To-Run-BuildXL/Log-Files.md)
* [Primary log file](Wiki/How-To-Run-BuildXL/Log-Files/BuildXL.log.md)
* [Stats log file](Wiki/How-To-Run-BuildXL/Log-Files/BuildXL.stats.md)
* [Logging Options](Wiki/How-To-Run-BuildXL/Logging-Options.md)
* [Execution Log](Wiki/How-To-Run-BuildXL/Log-Files/BuildXL.xlg.md)
* [Execution Analyzer](Wiki/Advanced-Features/Execution-Analyzer.md) 
* [XLG Debugger](Wiki/Advanced-Features/XLG-Debugger/INDEX.md) 
* [Cache Miss Analysis](Wiki/Advanced-Features/Cache-Miss-Analysis.md)

## Onboarding
* [Onboarding a Rush repo](Wiki/Frontends/rush-onboarding.md)

## DScript
* [Introduction](Wiki/DScript/Introduction.md)
* [Comments](Wiki/DScript/Comments.md)
* [Debugging](Wiki/DScript/Debugging.md)
* [DScript vs Typescript](Wiki/DScript/DScript-vs-Typescript.md)
* [Enumerations](Wiki/DScript/Enums-vs-typed-strings.md)
* [Functions](Wiki/DScript/Functions.md)
* [Globbling](Wiki/DScript/Globbing.md)
* [Import and Export](Wiki/DScript/Import-export.md)
* [List files](Wiki/DScript/List-files.md)
* [Merge and Override](Wiki/DScript/Merge-and-Override.md)
* [Policies (Lint rules)](Wiki/DScript/Policies-(Lint-rules).md)
* [Qualifiers](Wiki/DScript/Qualifiers.md)
* [Reusing Declarations (factoring)](Wiki/DScript/Reusing-Declarations-(factoring).md)
* [Templates](Wiki/DScript/Templates.md)
* [Types](Wiki/DScript/Types.md)


## Troubleshooting
* [DX Error Codes](Wiki/Error-Codes)
