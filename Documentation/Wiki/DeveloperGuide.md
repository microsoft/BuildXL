# Prerequesites
## Windows
* Windows 10 is the minimum requirement for BuildXL. You do not need to install [Visual Studio](https://visualstudio.microsoft.com/vs/) to get a working build, but it can be very helpful and is recommended for Windows development.
* You will also need to install the Windows development kit. When you build the repo, the build script will determine if you have a compatible version installed and provide an error message with a link if one needs to be installed
* [Visual Studio 2019 Build Tools](https://visualstudio.microsoft.com/downloads/) build tools must be installed. Scroll down to the "Tools for Visual Studio 2019" section, download and run the installer for "Build Tools for Visual Studio 2019". Within the Visual Studio installer under "Individual Components", search for and install "MSVC (v142) - VS 2019 C++ x64/x86 Spectre-mitigated libs (v14.29-16.10)".


## Linux
See [Prepare Linux VM](/Documentation/Wiki/LinuxDevelopment/How_to_prep_VM.md)


## macOS
To run BuildXL on macOS you need to install:

* Microsoft [.NET Core SDK](https://dotnet.microsoft.com/download) for macOS
* The latest [Mono](https://www.mono-project.com/download/stable/) runtime
* If you want to run and load the sandbox to enable fully observed and cacheable builds, you also have to [turn off System Integrity Protection](https://developer.apple.com/library/archive/documentation/Security/Conceptual/System_Integrity_Protection_Guide/ConfiguringSystemIntegrityProtection/ConfiguringSystemIntegrityProtection.html) (SIP) on macOS. SIP blocks the installation of the unsigned kernel extension (or Kext) produced by the build.
* Latest version of Xcode


# Performing a build
`bxl.cmd` (and `./bxl.sh`) are the entry points to building BuildXL. They provide some shorthands for common tasks to prevent developers from needing to specify longer command line options. While most examples below are based off of bxl.cmd for Windows, there will most times be a bxl.sh equivalent for macOS/Linux: `bxl.sh -h` shows the custom arguments for this script.


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

## Build and Test for macOS and Linux
BuildXL can be run on Linux and macOS systems via the `bxl.sh` script. The `--minimal` flag can be passed to run a minimal build (as described in the section above).

One can also run `./bxl.sh "/f:tag='test'"` to only run the tests.

## Development workflow
### Browsing source code in Visual Studio
Because we don't have deep [Visual Studio](https://visualstudio.microsoft.com/vs/) integration for BuildXL at this time, you should use BuildXL's solution generation feature to generate  MSBuild `.proj` files and a `.sln`. Prior to opening this solution you will need to [install the Visual Studio plugin](Installation.md).

Once installed you can run the solution generation. The result will be placed in `out\vs\BuildXL\` with a base filename matching the top-level directory of your enlistment. So for example if your enlistment directory is `c:\enlist\BuildXL`, the generated solution file will be `out\vs\BuildXL\BuildXL.sln`.
 
 There are two modes for what to generate
 1. `bxl -vs` Generates most projects
 1. `bxl -vsall` Generates all flavors of all projects. If you are missing something try this

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

### Debugging
The easiest way to get a debugger attached to bxl.exe is to specify an environment variable called `BuildXLDebugOnStart` and set it to 1. This will cause a debugger window to pop up and will let you choose a running Visual Studio instance to attach to the process and start debugging. Alternatively, placing a good old `System.Diagnostics.Debugger.Launch();` inside the code you want to debug, re-compiling BuildXL and running it with the `-use Dev` flag does the trick too.