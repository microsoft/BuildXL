# Microsoft Build Accelerator

<img alt="BuildXL Icon" src="Public/Src/Branding/BuildXL.png" width=15%>

## Introduction

Build Accelerator, BuildXL for short, is a build engine originally developed for large internal teams at Microsoft, and owned by the [Tools for Software Engineers](https://www.microsoft.com/en-us/research/project/tools-for-software-engineers/) team, part of the Microsoft One Engineering System internal engineering group. Internally at Microsoft, BuildXL runs 30,000+ builds per day on [monorepo](https://en.wikipedia.org/wiki/Monorepo)  codebases up to a half-terabyte in size with a half-million process executions per build, using distribution to thousands of datacenter machines and petabytes of source code, package, and build output caching. Thousands of developers use BuildXL on their desktops for faster builds even on mega-sized codebases.

BuildXL accelerates multiple build languages, including:

* MSBuild (using new features under development in MSBuild 16 which will ship in future versions of Visual Studio 2019 and the .NET Core SDK)
* CMake (under development)
* Its own internal scripting language, DScript, an experimental TypeScript based format used as an intermediate language by a small number of teams inside Microsoft

BuildXL has a command-line interface. There are currently no plans to integrate it into Visual Studio. The project is open source in the spirit of transparency of our engineering system. You may find our technology useful if you face similar issues of scale. Note that BuildXL is not intended as a replacement for MSBuild or to indicate any future direction of build languages from Microsoft.

## Documentation
The BuildXL documentation main page is [here](Documentation/INDEX.md).

## Examples and Demos
See the `Examples/` folder for basic project examples. See the [Demos](Public/Src/Demos/Demos.md) page for information about various technical demos like using the process sandboxing code.

# Building the Code

## Build Status - Azure DevOps Pipelines
[![Build status](https://dev.azure.com/mseng/Domino/_apis/build/status/BuildXL/BuildXL%20Rolling?branchName=master)](https://dev.azure.com/mseng/Domino/_build/latest?definitionId=8196)

## Command Line Build and Test
This repo uses DScript files for its own build. From the root of the enlistment run: `bxl.cmd` which will:

1. Download the latest self-host engine release.
1. Pull all needed packages from NuGet.org and other package sources.
1. Run a debug build as well as the unit tests locally.
1. Deploy a runnable bxl.exe to: `out\bin\debug\win-x64`.

Note you do not need administrator (elevated) privileges for your console window.

If you just want to compile BuildXL without running tests you can use: `bxl.cmd -minimal` after which you can find the binaries in `out\bin\debug\win-x64`.

Other build types can be performed as well:
* `bxl -deployConfig release` : Retail build
* `bxl /vs` : Converts DScript files into MSBuild `.proj` files and generates a `.sln` for the project at `out\vs\BuildXL\BuildXL.sln`

### Windows
You should use Windows 10 with BuildXL. You do not need to install [Visual Studio](https://visualstudio.microsoft.com/vs/) to get a working build, but see the section below on using VS with BuildXL for developing in the BuildXL codebase.

### macOS

To run BuildXL on macOS you need to install:

* Microsoft [.NET Core SDK](https://dotnet.microsoft.com/download) for macOS
* The latest [Mono](https://www.mono-project.com/download/stable/) runtime
* If you want to run and load the sandbox to enable fully observed and cacheable builds, you also have to [turn off System Integrity Protection](https://developer.apple.com/library/archive/documentation/Security/Conceptual/System_Integrity_Protection_Guide/ConfiguringSystemIntegrityProtection/ConfiguringSystemIntegrityProtection.html) (SIP) on macOS. SIP blocks the installation of the unsigned kernel extension (or Kext) produced by the build.

To start building, go to the root of the repository and run `./bxl.sh --minimal` in your preferred terminal. Just like `bxl.cmd`, this bash script also supports several flags for your convenience.

## Using BuildXL With Visual Studio
Because we don't have deep [VS](https://visualstudio.microsoft.com/vs/) integration for BuildXL at this time, you can use `bxl /vs` which will convert the DScript files into MSBuild `.proj` files and generates a `.sln` for the project under `out\vs\BuildXL\` with a base filename matching the top-level directory of your enlistment. So for example if your enlistment directory is `c:\enlist\BuildXL`, the generated solution file will be `out\vs\BuildXL\BuildXL.sln`.

# Contributing
See [CONTRIBUTING](CONTRIBUTING.md).
