# Overview
There are two variants of BuildXL development: Public (default) and Internal (for Microsoft internal developers). The difference comes down to a few dependencies which are only available internally within Microsoft today, like the connections to an internal cache server. The acquisition path for machine prerequisites may also differ slightly. 

If you are a Microsoft internal developer, the Internal variant is automatically selected based on your user domain on Windows. On Linux you need to specify --internal in bxl.sh.

# Prerequesites
## Windows
* Windows 10 is the minimum requirement for BuildXL. You do not need to install [Visual Studio](https://visualstudio.microsoft.com/vs/) to get a working build, but it can be very helpful and is recommended for Windows development.
* (For the Public build) [Visual Studio 2022 Build Tools](https://aka.ms/vs/17/release/vs_BuildTools.exe) build tools must be installed. Within the Visual Studio installer under "Individual Components", search for and install "MSVC (v143) - VS 2022 C++ x64/x86 Spectre-mitigated libs (latest)". If you get an error about the build tools not being found after this installation, try setting the MSVC_VERSION environment variable to the exact version that was installed under "%Programfiles%\Microsoft Visual Studio\2022\BuildTools\VC\Tools\MSVC". See [visualCpp.dsc](../../Public/Sdk/Experimental/Msvc/VisualCpp/visualCpp.dsc) for more details about how this path is resolved

## Linux
See [Prepare Linux VM](/Documentation/Wiki/LinuxDevelopment/How_to_prep_VM.md)

# Performing a build
`bxl.cmd` (and `./bxl.sh`) are the entry points to building BuildXL. They provide some shorthands for common tasks to prevent developers from needing to specify longer command line options. While most examples below are based off of bxl.cmd for Windows, there will most times be a bxl.sh equivalent for Linux: `bxl.sh -h` shows the custom arguments for this script.


## Minimal Build
From the root of the enlistment run `bxl.cmd -minimal`. This will:
1. Download the latest pre-build version of bxl.exe.
1. Use it to pull all package dependencies.
1. Perform a build of the BuildXL repo scoped to just bxl.exe and its required dependencies.
1. Deploy a runnable bxl.exe to `out\bin\debug\win-x64`.

Note you do not need administrator (elevated) privileges for your console window.

## Build and Test
Running a vanilla `bxl.cmd` without the `-minimal` flag above will compile a larger set of binaries as well as run tests. The non-minimal build still doesn't build everything, but it builds most tools a developer is likely to interact with. Running `bxl.cmd -all` will build everything in the repo

The `-minimal` and `-all` flags are shorthands that get translated to more complicated pip filter expressions which are eventually passed to `bxl.exe`

## Build and Test for Linux
BuildXL can be run on Linux via the `bxl.sh` script. The `--minimal` flag can be passed to run a minimal build (as described in the section above).

One can also run `./bxl.sh "/f:tag='test'"` to only run the tests.

## Development workflow
### Browsing source code in Visual Studio
Because we don't have deep [Visual Studio](https://visualstudio.microsoft.com/vs/) integration for BuildXL at this time, you should use BuildXL's solution generation feature to generate  MSBuild `.proj` files and a `.sln`. Prior to opening this solution you will need to [install the Visual Studio plugin](Installation.md).

Once installed you can run the solution generation. The result will be placed in `out\vs\BuildXL\` with a base filename matching the top-level directory of your enlistment. So for example if your enlistment directory is `c:\enlist\BuildXL`, the generated solution file will be `out\vs\BuildXL\BuildXL.sln`.
 
 There are two modes for what to generate
 1. `bxl -vs` Generates most projects
 1. `bxl -vs -cache` Generates cache projects
 1. `bxl -vsall` Generates almost all flavors of all projects. If you are missing something try this

The `bxl.sh` script has a corresponding `--vs` argument.

### Consuming a locally build version of BuildXL
By default the `bxl` command at the root of the repo will use a pre-build version of bxl.exe. For testing it can be helpful to use a locally build version.
1. `bxl -deploy dev -minimal` will create a minimal, debug version of bxl.exe and "deploy" it to an output directory in the repo
1. `bxl -use dev` will then use that locally built version of bxl.exe for the build session. The `-use dev` flag can be added to any invocation using the bxl.cmd convenience wrappers

The `bxl.sh` script has corresponding `--deploy-dev` and `--use-dev` arguments.

### Targeting your build (filtering)
You may want to build only a specific project or test if you are iterating on a single component. This can be achieved with filtering. See the [filtering](How-To-Run-BuildXL/Filtering.md) doc for full details, but a useful shorthand is to specify the spec file that you want to target. For example `bxl IntegrationTest.BuildXL.Scheduler.dsc`. See the filtering doc for more details of filtering shorthands.

#### Running specific tests
You can take this a step farther and specify a specific test method. This example sets a property which is consumed by the DScript test SDK. It causes a test case filter to be passed down to the test runner to run a specific test method based on a fully qualified method name.

`bxl IntegrationTest.BuildXL.Scheduler.dsc -TestMethod IntegrationTest.BuildXL.Scheduler.BaselineTests.VerifyGracefulTeardownOnPipFailure`

Be careful with typos in the method name. If the filter doesn't match any test cases the run will still pass. For a sense of security it can help to make the unit test fail the first time you use a filter to make sure your filter is correct.

You can also filter by test class. Again, be careful to make sure you don't inadvertently filter out all tests. For example specifying both a testClass and a testMethod will cause no tests to match.

`bxl IntegrationTest.BuildXL.Scheduler.dsc -TestClass IntegrationTest.BuildXL.Scheduler.BaselineTests`

The `bxl.sh` corresponding arguments are `--test-method` and `--test-class`.

### Contributions
If you wish to contribute to the repo, please refer to [this](https://mseng.visualstudio.com/Domino/_git/BuildXL.Internal?path=/CONTRIBUTING.md&version=GBdev/kkaroth/shardset&anchor=before-you-start) guide first.

### Debugging
The easiest way to get a debugger attached to bxl.exe is to specify an environment variable called `BuildXLDebugOnStart` and set it to 1. This will cause a debugger window to pop up and will let you choose a running Visual Studio instance to attach to the process and start debugging. Alternatively, placing a good old `System.Diagnostics.Debugger.Launch();` inside the code you want to debug, re-compiling BuildXL and running it with the `-use Dev` flag does the trick too.

# Source Code Layout

## Build System
This repository uses **BuildXL with DScript** as its build system, not MSBuild. The `.dsc` files are the authoritative build definitions. The `.csproj` and `.sln` files are generated for IDE support (via `bxl -vs`) and should not be edited directly.

## Directory Structure
```
BuildXL.Internal/
├── Public/                    # Open source components
│   ├── Src/
│   │   ├── App/              # Application entry points
│   │   ├── Cache/            # Caching infrastructure
│   │   ├── Engine/           # Core build engine (scheduler, processes, etc.)
│   │   ├── FrontEnd/         # Language frontends (MSBuild, JavaScript, Ninja, DScript)
│   │   ├── Pips/             # Process/pip definitions
│   │   ├── Sandbox/          # OS sandboxing (Windows Detours, macOS, Linux)
│   │   ├── Tools/            # Supporting tools and analyzers
│   │   └── Utilities/        # Shared utility libraries
│   └── Sdk/                  # Build SDK and standard libraries
├── Private/                   # Microsoft-internal components
├── Shared/                    # Shared scripts and tools
├── Documentation/             # All documentation
├── config.dsc                 # Root build configuration
└── bxl.cmd / bxl.sh          # Build entry points
```

## Test Location
Unit tests are located in `UnitTests/` subdirectories within each component. For example:
- `Public/Src/Engine/UnitTests/` - Engine unit tests
- `Public/Src/Cache/ContentStore/Test/` - Cache content store tests

# Code Style

Code style is enforced via `.editorconfig` at the repository root. Key conventions:

## Naming Conventions
| Element | Convention | Example |
|---------|------------|---------|
| Private instance fields | `m_` prefix + camelCase | `m_processCount` |
| Private static fields | `s_` prefix + camelCase | `s_defaultTimeout` |
| Constants | PascalCase | `MaxRetryCount` |
| Public members | PascalCase | `GetProcessInfo()` |
| Parameters & locals | camelCase | `processId`, `filePath` |

## Other Style Rules
- Always use braces for control statements (even single-line)
- Use `var` when type is apparent from the right-hand side
- Prefer expression-bodied members for simple properties and accessors
- Sort `using` directives with `System.*` first

# Build Qualifiers

BuildXL uses qualifiers to build different configurations. Common qualifiers:

| Qualifier | Description |
|-----------|-------------|
| `Debug` | Debug build (default) |
| `Release` | Release/optimized build |
| `DebugDotNetCore` | Debug build targeting .NET Core |
| `ReleaseDotNetCore` | Release build targeting .NET Core |
| `DebugNet472` | Debug build targeting .NET Framework 4.7.2 |
| `ReleaseNet472` | Release build targeting .NET Framework 4.7.2 |

Specify qualifiers with `/q:` flag: `bxl.cmd /q:ReleaseDotNetCore`

# Quick Validation

Before submitting changes, run validation appropriate to your change scope:

| Scope | Command |
|-------|---------|
| Quick build check | `bxl.cmd -minimal` |
| Build specific component | `bxl.cmd <component>.dsc` |
| Run specific test | `bxl.cmd <test>.dsc -TestMethod <FullName>` |
| Full pre-checkin validation | `RunCheckInTests.cmd` |

**Note:** `RunCheckInTests.cmd` performs comprehensive validation including multiple build configurations and fingerprint verification. It can take significant time. Typically running unit tests (`bxl.cmd`) locally is sufficient and the additional `RunCheckinTests.cmd` can be left to pull request validations.