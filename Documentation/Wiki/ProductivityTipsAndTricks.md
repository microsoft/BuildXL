# BuildXL Productivity Tips and Tricks

## Inner loop productivity tips and tricks

### How can I compile the entire codebase without running the tests?
`.\bxl compile`
Note,this command will compile the entire codebase with all the possible target frameworks, like Debug + net472, Debug + DotNetCore + Win, Debug + DotNetCore + Mac etc.

### How to compile the entire codebase for one framework only?
The following commands compile the codebase in debug mode:
* `.\bxl CompileDebugNet472` - to compile targeting net472 framework.
* `.\bxl CompileDebugNetCoreWin` - to compile targeting .net core.
* `.\bxl CompileWin` - to combine the two options mentioned above.
* `.\bxl CompileOsx` - to compile targeting .net core for OSX.
* `.\bxl CompileLinux` - to compile targeting .net core for Linux.

Lower level filter for compiling a specific qualifier:
* `.\bxl "/f:tag='targetFramework=net472'" /q:Release` to compile targeting net472 in Release mode.
* `.\bxl "/f:tag='targetRuntime=win-x64'"` an alternative way to compile everyting targeting Windows OS.
* `.\bxl "/f:(tag='targetFramework=netcoreapp3.1')and(tag='targetRuntime=win-x64') -Use Dev"` to only target netcore app for Windows.

### How to run a single unit test?

* Running a specific test method: `.\bxl TestProject.dsc -TestMethod FullyQualifiedTestMethodName`.
* Running all the tests in a specific class: `.\bxl TestProject.dsc -TestClass FullyQualifiedTestClassName`.

You also can run a test in Visual Studio either from Test Explorer or via ReSharper (see [Browsing source code in Visual Studio](Wiki/DeveloperGuide.md#Browsing-source-code-in-Visual-Studio) for how to generate the solution usable in VS).

Two very important caveats:
1. Always specify the spec file that corresponds to to the test project, otherwise the full build would be performed.
2. Any typos in the fully qualified names will cause the build to silently pass because all the tests would be filtered out.

### How to debug a single unit test?
You can debug a test in Visual Studio, but if this option is not working, you can change the test method by adding `System.Diagnostics.Debugger.Launch()` and run a test using the command mentioned above.

## Producing local nuget packages
In some cases you need to test that the nuget packages produced by this repo can be successfully integrated into another repository.
One option is to use an AzDev testing nuget feed, and another option is to use a local file-based nuget feed.

1. Creating nuget packages.
The local feed works only for nuget packages with relatively simple versioning scheme. For instance, the default scheme used by this repo like "0.1.0-20200601.1" won't work and a nuget package with this version won't be added to a file-based nuget feed.
The following command produces all the nuget packages in `Debug` mode, specifies the version `1.0.5` and removes all the suffixes from the version.

`.\bxl NugetPackages.dsc /p:[BuildXL.Branding]SemanticVersion=1.0.5 /p:[BuildXL.Branding]SourceIdentification='1' /q:Debug`

2. Upload the nuget packages to a local feed.
```powershell
# Change the next 3 lines to adjust your source location, the mode (debug or release)
# and the location of your local nuget feed.
$sourceRoot = "C:\Sources\BuildXL.Internal"
$mode = "debug"
$localFeedPath = "C:\localPackages"

$packagesFolder = "$sourceRoot\Out\Bin\$mode\pkgs\"
$nugetExe = "$sourceRoot\Shared\Tools\NuGet.exe"

# Clean the local feed first to avoid too many versions there. Comment it out if needed.
remove-item C:\localPackages -Recurse -ErrorAction Ignore

ls $packagesFolder | %{&$nugetExe add $_.FullName -source $localFeedPath}
```

3. Configure a target application to use a local nuget feed
Add the following line at the end of `<packagesSources>` section of nuget.config:

```xml
 <add key="BuildXL.Testing" value="c:/localPackages" />
```