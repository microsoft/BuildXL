# BuildXL (Microsoft Build Accelerator)

<img alt="BuildXL Icon" src="Public/Src/Branding/BuildXL.png" width=15%>

## Introduction

BuildXL (Microsoft Build Accelerator), is a build engine originally developed for large internal teams at Microsoft. Internally at Microsoft, BuildXL runs 150,000+ builds per day on [monorepo](https://en.wikipedia.org/wiki/Monorepo) codebases up to a half-terabyte in size with a half-million process executions per build. It leverages distribution to thousands of data center machines and petabytes of source code, package, and build output caching. Thousands of developers use BuildXL on their desktops for faster builds.

BuildXL accelerates multiple build languages, including:

* [JavaScript](Documentation/Wiki/Frontends/js-onboarding.md)
* MSBuild (experimental: using new features under development in MSBuild 16 which will ship in future versions of Visual Studio 2019 and the .NET Core SDK)
* CMake / Ninja (beta)
* Its own internal scripting language, DScript, an experimental TypeScript based format used as an intermediate language by a small number of teams inside Microsoft

BuildXL has a command-line interface. There are currently no plans to integrate it into Visual Studio. The project is open source in the spirit of transparency of our engineering system. You may find our technology useful if you face similar issues of scale. Note that BuildXL is not intended as a replacement for MSBuild or to indicate any future direction of build languages from Microsoft.

## Examples
See the `Examples/` folder for basic project examples. 

## Documentation
The BuildXL documentation landing page is [here](Documentation/INDEX.md) and look at [the developer guide](Documentation/Wiki/DeveloperGuide.md) in order to understand how to build and use BuildXL.

## Build Status - Azure DevOps Pipelines
[![Build status](https://dev.azure.com/mseng/Domino/_apis/build/status/8196?branchName=master)](https://dev.azure.com/mseng/Domino/_build/latest?definitionId=8196)

