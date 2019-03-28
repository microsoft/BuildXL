The collection of source, project and script files in this folder builds the Content Store and Memoization Store Interfaces BuildXL depends on against .NET Core using .NetStandard2.0.

The process is as follows:

- Install the latest .NET Core SDK from https://download.microsoft.com/download/7/3/A/73A3E4DC-F019-47D1-9951-0453676E059B/dotnet-sdk-2.0.2-win-gs-x64.exe
- Enlist in the CloudStore source
- Start a VS2015/2017 developer console
- Run init.cmd in the CloudStore root folder
- Change directory into this folder
- Run build.cmd to build the interfaces against .NET 4.5.1 and .NetStandard20, this step also creates Nuget packages for consumption by the BuildXL build engine
- Run push.cmd to publish the created Nuget packages into the Domino.Public.Experimental feed
- Adjust the package dependencies in the BuildXL repo to consume the new packages

Note: The Nuget packages created by the build script use the current version (CloudStoreReleaseVersion) of the CloudStore release from the \Build\Versions.props file!