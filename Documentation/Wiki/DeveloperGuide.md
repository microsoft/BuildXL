# Prerequesites
## Windows
* You should use Windows 10 with BuildXL. You do not need to install [Visual Studio](https://visualstudio.microsoft.com/vs/) to get a working build, but it can be very helpful and is recommended for Windows development.
* You will also need to install the Windows development kit. When you build the repo, the build script will determine if you have a compatible version installed and provide an error message with a link if one needs to be installed
## macOS
To run BuildXL on macOS you need to install:

* Microsoft [.NET Core SDK](https://dotnet.microsoft.com/download) for macOS
* The latest [Mono](https://www.mono-project.com/download/stable/) runtime
* If you want to run and load the sandbox to enable fully observed and cacheable builds, you also have to [turn off System Integrity Protection](https://developer.apple.com/library/archive/documentation/Security/Conceptual/System_Integrity_Protection_Guide/ConfiguringSystemIntegrityProtection/ConfiguringSystemIntegrityProtection.html) (SIP) on macOS. SIP blocks the installation of the unsigned kernel extension (or Kext) produced by the build.

# Performing a build
`bxl.cmd` (and `./bxl.sh`) are the entry points to building BuildXL. They provide some shorthands for common tasks to prevent developers from needing to specify longer command line options. While most examples below are based off of bxl.cmd for Windows, there will most times be a bxl.sh equivalent for macOS.

## Minimal Build
From The root of the enlistment run `bxl.cmd -minimal`. This will:
1. Download the latest pre-build version of bxl.exe
1. Use it to pull all package dependencies
1. Perform a build of the BuildXL repo scoped to just bxl.exe and its required dependencies.
1. Deploy a runnable bxl.exe to `out\bin\debug\win-x64`.

Note you do not need administrator (elevated) privileges for your console window.

## Build and Test
Running a vanilla `bxl.cmd` without the `-minimal` flag above will compile a larger set of binaries as well as run tests. The non-minimal build still doesn't build everything, but it builds most tools a developer is likely to interact with. Running `bxl.cmd -all` will build everything in the repo

The `-minimal` and `-all` flags are shorthands that get translated to more complicated pip filter expressions which are eventually passed to `bxl.exe`

## Development workflow
### Browsing source code in Visual Studio
Because we don't have deep [Visual Studio](https://visualstudio.microsoft.com/vs/) integration for BuildXL at this time, you can use `bxl -vs` which will generate MSBuild `.proj` files and a `.sln` for the project under `out\vs\BuildXL\` with a base filename matching the top-level directory of your enlistment. So for example if your enlistment directory is `c:\enlist\BuildXL`, the generated solution file will be `out\vs\BuildXL\BuildXL.sln`.

Prior to opening this solution you will need to [install the Visual Studio plugin](Installation.md).

### Consuming a locally build version of bxl.exe
By default the `bxl` command at the root of the repo will use a pre-build version of bxl.exe. FOr testing it can be helpful to use a locally build version.
1. `bxl -deploy dev -minimal` will create a minimal, debug version of bxl.exe and "deploy" it to an output directory in the repo
1. `bxl -use dev` will then use that locally built version of bxl.exe for the build session. The `-use dev` flag can be added to any invocation using the bxl.cmd convenience wrappers

### Running specific tests
You may want to build only a specific project or test if you are iterating on a single component. This can be achieved with filtering. See the [filtering](How-To-Run-BuildXL/Filtering.md) doc for full details, but a useful shorthand is to specify the spec file that you want to target. For example `bxl IntegrationTest.BuildXL.Scheduler.dsc`