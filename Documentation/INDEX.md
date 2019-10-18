# Microsoft Build Accelerator

## Guide to Documentation
This is the primary documentation for Microsoft Build Accelerator (BuildXL). If you are an internal Microsoft employee you may also want to visit the [BuildXL Internal](https://aka.ms/buildxl) documentation where you'll find documentation about interations with systems that are not publically available.

Keep this as the sole primary landing page for documentation and avoid creating nested navigation pages for navigation.

# Overview
* [ReadMe](../README.md) TODO: groom
* [Why BuildXL?](Wiki/WhyBuildXL.md) TODO: groom
* [Core Concepts](Wiki/CoreConcepts.md) TODO: author
* [Demos](../Public/Src/Demos/Demos.md) TODO: groom

# Project Documentation
* [Contributing](../CONTRIBUTING.md) TODO: groom
* [Code of Conduct](../CODE_OF_CONDUCT.md)
* [Installation Instructions](Wiki/Installation.md) TODO: groom
* [Developer Guide](Wiki/DeveloperGuide.md)
* [Security](../SECURITY.md)
* [Release Notes](Wiki/Release-Notes.md)

# Product Documentation
## Architecture
* [Overview](Wiki/ArchitectureOverview.md) TODO: author
* [Sandboxing](Specs/Sandboxing.md) TODO: groom

## Setting up a build
* [Command line](Wiki/How-to-run-BuildXL.md) TODO: groom
* [Frontends](Wiki/Frontends.md) TODO: groom
* [Mounts](Wiki/Advanced-Features/Mounts.md) TODO: groom
* [Build Parameters (Environment Variables)](Wiki/Advanced-Features/Build-Parameters-(Environment-variables).md) TODO: groom
* [Dirty Build](Wiki/How-To-Run-BuildXL/Dirty-Build.md)
* [Unsafe Flags](Wiki/How-To-Run-BuildXL/Unsafe-flags.md) todo: groom
* [Incremental Tools](Wiki/Advanced-Features/Incremental-tools.md) TODO: groom
* [Preserve Outputs](Wiki/Advanced-Features/Preserving-outputs.md) TODO: groom
* [Process Timeouts](Wiki/Advanced-Features/Process-Timeouts.md)
* [Sealed Directories](Wiki/Advanced-Features/Sealed-Directories.md) TODO: groom
* [Search Path Enumeration](Wiki/Advanced-Features/Search-Path-Enumeration.md)

## Build Execution
* [Filtering](Wiki/How-To-Run-BuildXL/Filtering.md) TODO: groom
* [Cache Algorithm]() TODO: author
* [Content and Metadata Cache](../Public/Src/Cache/README.md) TODO: groom
* [Paged Hashes](Specs/PagedHash.md) TODO: groom
* [Filesystem modes and enumerations](Wiki/Advanced-Features/Filesystem-modes-and-Enumerations.md) TODO: groom
* [Incremental Scheduling](Wiki/Advanced-Features/Incremental-Scheduling.md) TODO: groom
* [Cancellation](Wiki/How-To-Run-BuildXL/Cancellation-(CtrlC).md)
* [Resource tuning](Wiki/How-To-Run-BuildXL/Resource-Usage-Configuration.md) 
* [Pip Weight](Wiki/Advanced-Features/Pip-Weight.md) 
* [Scheduler Prioritization](Wiki/Advanced-Features/Scheduler-Prioritization.md)
* [Server Mode](Wiki/Advanced-Features/Server-Mode.md) 
* [Timestamp Faking](Wiki/Advanced-Features/Timestamp-Faking.md) TODO: groom
* [Symlinks and Junctions](Wiki/Advanced-Features/Symlinks-and-Junctions.md) TODO: groom
* [Service Pips](Wiki/Service-Pips.md)
* [Pip requested file materialization](Wiki/External-OnDemand-File-Materialization-API.md)

## Logging and Analysis
* [Console Output](Wiki/How-To-Run-BuildXL/Console-output.md)
* [Log Files](Wiki/How-To-Run-BuildXL/Log-Files.md)
* [Primary log file](Wiki/How-To-Run-BuildXL/Log-Files/BuildXL.log.md)
* [Stats log file](Wiki/How-To-Run-BuildXL/Log-Files/BuildXL.stats.md)
* [Logging Options](Wiki/How-To-Run-BuildXL/Logging-Options.md)
* [Execution Log](Wiki/How-To-Run-BuildXL/Log-Files/BuildXL.xlg.md)
* [Execution Analyzer](wiki/Advanced-Features/Execution-Analyzer.md) 
* [Cache Miss Analysis](wiki/Advanced-Features/Cache-Miss-Analysis.md)

## Troubleshooting
* [DX Error Codes](Wiki/Error-Codes)
* [Common Issues]()
