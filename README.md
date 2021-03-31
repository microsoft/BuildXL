# Microsoft Build Accelerator

<img alt="BuildXL Icon" src="Public/Src/Branding/BuildXL.png" width=15%>

## Introduction

Build Accelerator, BuildXL for short, is a build engine originally developed for large internal teams at Microsoft, and owned by the [Tools for Software Engineers](https://www.microsoft.com/en-us/research/project/tools-for-software-engineers/) team, part of the Microsoft One Engineering System internal engineering group. Internally at Microsoft, BuildXL runs 30,000+ builds per day on [monorepo](https://en.wikipedia.org/wiki/Monorepo) codebases up to a half-terabyte in size with a half-million process executions per build, using distribution to thousands of data center machines and petabytes of source code, package, and build output caching. Thousands of developers use BuildXL on their desktops for faster builds even on mega-sized codebases.

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
[![Build status](https://dev.azure.com/mseng/Domino/_apis/build/status/8196?branchName=master)](https://dev.azure.com/mseng/Domino/_build/latest?definitionId=8196)

## Command Line Build and Test
See the [Developer Guide](Documentation/Wiki/DeveloperGuide.md) for instructions on compiling BuildXL.

# Contributing
See [CONTRIBUTING](CONTRIBUTING.md).
