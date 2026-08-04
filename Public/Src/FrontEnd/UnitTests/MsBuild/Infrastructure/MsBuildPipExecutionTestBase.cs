// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using BuildXL.Engine;
using BuildXL.Native.IO;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Configuration.Mutable;
using Test.BuildXL.EngineTestUtilities;
using Test.BuildXL.TestUtilities;
using Test.BuildXL.TestUtilities.Xunit;
using Test.DScript.Ast;
using Test.BuildXL.FrontEnd.Core;
using Xunit;
using BuildXL.Utilities;
using BuildXL.Utilities.Core;
using BuildXL.Processes;
using BuildXL.FrontEnd.Utilities;

namespace Test.BuildXL.FrontEnd.MsBuild
{
    /// <summary>
    /// Provides facilities to run the engine adding MSBuild specific artifacts.
    /// </summary>
    public abstract class MsBuildPipExecutionTestBase : DsTestWithCacheBase
    {
        /// <summary>
        /// Default out dir to use in projects
        /// </summary>
        protected string OutDir { get; }

        /// <summary>
        /// Root to the source enlistment root
        /// </summary>
        protected string SourceRoot { get; }

        // By default the engine runs e2e
        protected virtual EnginePhases Phase => EnginePhases.Execute;

        // Keep the paths below in sync with Public\Src\FrontEnd\UnitTests\MsBuild\Test.BuildXL.FrontEnd.MsBuild.dsc
        /// <nodoc/>
        protected string RelativePathToFullframeworkMSBuild => "msbuild/net472";
        
        /// <nodoc/>
        protected string RelativePathToDotnetCoreMSBuild => "msbuild/dotnetcore";
        
        /// <nodoc/>
        protected string RelativePathToDotnetExe => "dotnet";

        protected override bool DisableDefaultSourceResolver => true;

        /// <summary>
        /// Path to Microsoft.Build.Tasks.CodeAnalysis.dll, where csc task is located
        /// </summary>
        /// <remarks>
        /// Keep in sync with the deployment
        /// </remarks>
        protected string PathToCscTaskDll(bool shouldRunDotNetCoreMSBuild) => Path.Combine(TestDeploymentDir, "Compilers", shouldRunDotNetCoreMSBuild ? "dotnetcore" : "net472", "tools", "Microsoft.Build.Tasks.CodeAnalysis.dll").Replace("\\", "/");

        protected MsBuildPipExecutionTestBase(ITestOutputHelper output) : base(output, true)
        {
            RegisterEventSource(global::BuildXL.Engine.ETWLogger.Log);
            RegisterEventSource(global::BuildXL.Processes.ETWLogger.Log);
            RegisterEventSource(global::BuildXL.Scheduler.ETWLogger.Log);
            RegisterEventSource(global::BuildXL.Pips.ETWLogger.Log);
            RegisterEventSource(global::BuildXL.FrontEnd.Core.ETWLogger.Log);
            RegisterEventSource(global::BuildXL.FrontEnd.Download.ETWLogger.Log);
            RegisterEventSource(global::BuildXL.FrontEnd.Script.ETWLogger.Log);
            RegisterEventSource(global::BuildXL.FrontEnd.Nuget.ETWLogger.Log);
            RegisterEventSource(global::BuildXL.FrontEnd.MsBuild.ETWLogger.Log);

            SourceRoot = Path.Combine(TestRoot, RelativeSourceRoot);
            OutDir = "target";
        }

        /// <summary>
        /// Allows to specify some paremeters for the MSBuild resolver configuration settings
        /// </summary>
        protected SpecEvaluationBuilder BuildWithEnvironment(Dictionary<string, DiscriminatingUnion<string, UnitValue>> environment)
        {
            return base.Build().Configuration(DefaultMsBuildPrelude(environment));
        }

        protected SpecEvaluationBuilder Build(
            Dictionary<string, string> environment = null, 
            Dictionary<string, string> globalProperties = null, 
            string filenameEntryPoint = null,
            string msBuildRuntime = null,
            string dotnetSearchLocations = null,
            bool useSharedCompilation = false,
            string siblingResolver = null)
        {
            return Build(
                environment != null? environment.ToDictionary(kvp => kvp.Key, kvp => new DiscriminatingUnion<string, UnitValue>(kvp.Value)) : null,
                globalProperties,
                filenameEntryPoint,
                msBuildRuntime,
                dotnetSearchLocations,
                useSharedCompilation,
                siblingResolver);
        }

        /// <inheritdoc/>
        protected SpecEvaluationBuilder Build(
            Dictionary<string, DiscriminatingUnion<string, UnitValue>> environment, 
            Dictionary<string, string> globalProperties, 
            string filenameEntryPoint, 
            string msBuildRuntime,
            string dotnetSearchLocations,
            bool useSharedCompilation = false,
            string siblingResolver = null)
        {
            // Let's explicitly pass an empty environment, so the process environment won't affect tests by default
            return base.Build().Configuration(
                DefaultMsBuildPrelude(
                    environment: environment ?? new Dictionary<string, DiscriminatingUnion<string, UnitValue>>(), 
                    globalProperties, 
                    filenameEntryPoint: filenameEntryPoint, 
                    msBuildRuntime: msBuildRuntime,
                    dotnetSearchLocations: dotnetSearchLocations,
                    useSharedCompilation: useSharedCompilation,
                    siblingResolver: siblingResolver));
        }

        /// <inheritdoc/>
        protected SpecEvaluationBuilder Build(string configExtraArguments, string environment = null)
        {
            return base.Build().Configuration(DefaultMsBuildPrelude(configExtraArguments, environment));
        }

        /// <summary>
        /// Deletes the engine cache of the given configuration
        /// </summary>
        protected void DeleteEngineCache(IConfiguration configuration)
        {
            // Clean the Engine Cache
            var engineCacheDirectoryPath = configuration.Layout.EngineCacheDirectory.ToString(PathTable);
            FileUtilities.DeleteDirectoryContents(engineCacheDirectoryPath);
        }

        protected BuildXLEngineResult RunEngineWithConfig(ICommandLineConfiguration config, TestCache testCache = null, IDetoursEventListener detoursListener = null)
        {
            using (var tempFiles = new TempFileStorage(canGetFileNames: true, rootPath: TestOutputDirectory))
            {
                var appDeployment = CreateAppDeployment(tempFiles);

                // Set the specified phase
                ((CommandLineConfiguration)config).Engine.Phase = Phase;

                var engineResult = CreateAndRunEngine(
                    config,
                    appDeployment,
                    testRootDirectory: null,
                    rememberAllChangedTrackedInputs: true,
                    engine: out var engine,
                    testCache: testCache,
                    detoursListener: detoursListener);

                return engineResult;
            }
        }

        /// <summary>
        /// Returns an empty project
        /// </summary>
        protected static string CreateEmptyProject()
        {
            return
$@"<?xml version='1.0' encoding='utf-8'?>
<Project xmlns='http://schemas.microsoft.com/developer/msbuild/2003'>
    <Target Name='Build'/>
</Project>";
        }

        /// <summary>
        /// Returns a project that just echoes 'Hello World'
        /// </summary>
        protected static string CreateHelloWorldProject()
        {
            return
$@"<?xml version='1.0' encoding='utf-8'?>
<Project xmlns='http://schemas.microsoft.com/developer/msbuild/2003'>
    <Target Name='Build'>
        <Message Text='Hello World'/>
    </Target>
</Project>";
        }

        /// <summary>
        /// Returns a project that just echoes 'Hello World' when calling <param name="targetName"/>
        /// </summary>
        /// <remarks>
        /// The project explicitly declares its project reference protocol, and just propagates the given target
        /// name to its children
        /// </remarks>
        protected static string CreateProjectWithTarget(string targetName)
        {
            return
                $@"<Project>
    <ItemGroup>
		<ProjectReferenceTargets Include=""{targetName}"" Targets=""{targetName}""/>
    </ItemGroup >
    <Target Name=""{targetName}"">
        <Message Text=""Hello World""/>
    </Target>
</Project>";
        }

        /// <summary>
        /// Returns a very simple MSBuild project that writes to <paramref name="outputFilename"/>
        /// and optionally declares a project reference to <paramref name="projectReference"/>
        /// </summary>
        protected string CreateWriteFileTestProject(string outputFilename, string projectReference = null)
        {
            var reference = projectReference != null ?
                $"<ProjectReference Include='{Path.Combine(SourceRoot, projectReference)}'/>" :
                string.Empty;

            return
$@"<?xml version='1.0' encoding='utf-8'?>
<Project xmlns='http://schemas.microsoft.com/developer/msbuild/2003'>
    {GetWriteFileTask()}
    <PropertyGroup>
        <OutDir>{OutDir}</OutDir>
    </PropertyGroup>
    <ItemGroup>
        {reference}
    </ItemGroup>
    <Target Name='Build'>
        {GetWriteFileTaskInvocation(outputFilename)}
    </Target>
</Project>";
        }

        /// <summary>
        /// Returns a task that writes a file with a given content, making sure all directories are created
        /// </summary>
        /// <returns></returns>
        private string GetWriteFileTask()
        {
            // On Windows the tests use the full framework MSBuild, whose CodeTaskFactory can load the net472 tasks assembly.
            // On non-Windows only dotnet core MSBuild is available, where CodeTaskFactory is not supported. There the built-in
            // WriteLinesToFile task is used instead (see GetWriteFileTaskInvocation), so no custom task declaration is needed.
            if (!OperatingSystemHelper.IsWindowsOS)
            {
                // The minimal dotnet core MSBuild deployment doesn't register the default (built-in) tasks, so explicitly
                // register the real WriteLinesToFile task from the dotnet core tasks assembly.
                return $"<UsingTask TaskName='WriteLinesToFile' AssemblyFile='{TestDeploymentDir}/{RelativePathToDotnetCoreMSBuild}/Microsoft.Build.Tasks.Core.dll'/>";
            }

            return
$@"<UsingTask TaskName='WriteFile' TaskFactory='CodeTaskFactory' AssemblyFile='{TestDeploymentDir}\{RelativePathToFullframeworkMSBuild}\Microsoft.Build.Tasks.Core.dll'>
    <ParameterGroup>
        <Path ParameterType='System.String' Required='true' />
        <Content ParameterType ='System.String' Required='true' />
    </ParameterGroup>
    <Task>
        <Reference Include='mscorlib'/>
        <Code Type='Fragment' Language='cs'>
            <![CDATA[
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path));
                System.IO.File.WriteAllText(Path, Content);
            ]]>
        </Code>
    </Task >
</UsingTask>";
        }

        /// <summary>
        /// Returns the invocation that writes a file with content 'Test' to <paramref name="outputFilename"/>. On Windows it uses
        /// the custom CodeTaskFactory-based WriteFile task; on non-Windows it uses MSBuild's built-in WriteLinesToFile task, since
        /// CodeTaskFactory is not available on dotnet core MSBuild.
        /// </summary>
        private string GetWriteFileTaskInvocation(string outputFilename)
        {
            return OperatingSystemHelper.IsWindowsOS
                ? $@"<WriteFile
            Path='$(OutDir)/{outputFilename}'
            Content='Test'/>"
                : $@"<WriteLinesToFile
            File='$(OutDir)/{outputFilename}'
            Lines='Test'
            Overwrite='true'/>";
        }

        /// <summary>
        /// Returns a 'dirs' project, listing all specified <paramref name="projectNames"/>
        /// </summary>
        protected static string CreateDirsProject(params string[] projectNames)
        {
            var projectList = new StringBuilder();
            foreach (var projectName in projectNames)
            {
                projectList.AppendLine($"<ProjectReference Include='{projectName}'/>");
            }

            return
$@"<?xml version='1.0' encoding='utf-8'?>
<Project xmlns='http://schemas.microsoft.com/developer/msbuild/2003'>
    <ItemGroup>
        {projectList.ToString()}
    </ItemGroup>
    <ItemGroup>
        <ProjectReferenceTargets Include=""Build"" Targets=""Build""/>
    </ItemGroup>
    <Target Name=""Build""/>
</Project>";
        }

        private string DefaultMsBuildPrelude(
            Dictionary<string, DiscriminatingUnion<string, UnitValue>> environment = null, 
            Dictionary<string, string> globalProperties = null,
            bool enableBinLogTracing = false,
            bool enableEngineTracing = false,
            string logVerbosity = null,
            bool allowProjectsToNotSpecifyTargetProtocol = true,
            string filenameEntryPoint = null,
            string msBuildRuntime = null,
            string dotnetSearchLocations = null,
            bool useSharedCompilation = false,
            string siblingResolver = null) => $@"
config({{
    resolvers: [
        {{
            kind: 'MsBuild',
            moduleName: 'Test',
            msBuildSearchLocations: [d`{TestDeploymentDir}/{((msBuildRuntime == "DotNetCore" || !OperatingSystemHelper.IsWindowsOS) ? RelativePathToDotnetCoreMSBuild : RelativePathToFullframeworkMSBuild)}`],
            root: d`.`,
            allowProjectsToNotSpecifyTargetProtocol: {(allowProjectsToNotSpecifyTargetProtocol ? "true" : "false")},
            {DictionaryToExpression("environment", environment)}
            {DictionaryToExpression("globalProperties", globalProperties)}
            enableBinLogTracing: {(enableBinLogTracing ? "true" : "false")},
            enableEngineTracing: {(enableEngineTracing? "true" : "false")},
            {(logVerbosity != null ? $"logVerbosity: {logVerbosity}," : string.Empty)}
            {(filenameEntryPoint != null ? $"fileNameEntryPoints: [r`{filenameEntryPoint}`]," : string.Empty)}
            {(msBuildRuntime != null ? $"msBuildRuntime: \"{msBuildRuntime}\"," : string.Empty)}
            {(dotnetSearchLocations != null ? $"dotNetSearchLocations: {dotnetSearchLocations}," : (!OperatingSystemHelper.IsWindowsOS ? $"dotNetSearchLocations: [d`{TestDeploymentDir}/{RelativePathToDotnetExe}`]," : string.Empty))}
            useManagedSharedCompilation: {(useSharedCompilation ? "true" : "false")},
        }},
        {(siblingResolver != null ? siblingResolver + "," : string.Empty)}  
    ],
    engine: {{unsafeAllowOutOfMountWrites: true}},
    mounts: [
        {{
            name: a`working-dir`,
            path: d`{TestOutputDirectory}`,
            trackSourceFileChanges: true,
            isWritable: true,
            isReadable: true,
            isScrubbable: false,
        }}
    ]
}});";

        private string DefaultMsBuildPrelude(
            string extraArguments,
            string environment) => $@"
config({{
    disableDefaultSourceResolver: true,
    resolvers: [
        {{
            kind: 'MsBuild',
            moduleName: 'Test',
            msBuildSearchLocations: [d`{TestDeploymentDir}/{(OperatingSystemHelper.IsWindowsOS ? RelativePathToFullframeworkMSBuild : RelativePathToDotnetCoreMSBuild)}`],
            root: d`.`,
            allowProjectsToNotSpecifyTargetProtocol: true,
            useManagedSharedCompilation: false,
            {(OperatingSystemHelper.IsWindowsOS ? string.Empty : $"msBuildRuntime: \"DotNetCore\", dotNetSearchLocations: [d`{TestDeploymentDir}/{RelativePathToDotnetExe}`],")}
            {(environment != null? $"environment: {environment}," : DictionaryToExpression("environment", new Dictionary<string, string>()))}
            {extraArguments ?? string.Empty}
        }},
    ],
    engine: {{unsafeAllowOutOfMountWrites: true}},
    mounts: [
        {{
            name: a`working-dir`,
            path: d`{TestOutputDirectory}`,
            trackSourceFileChanges: true,
            isWritable: true,
            isReadable: true,
            isScrubbable: false,
        }}
    ]
}});";

        private static string DictionaryToExpression(string memberName, Dictionary<string, string> dictionary)
        {
            return (dictionary == null ? 
                string.Empty : 
                $"{memberName}: Map.empty<string, string>(){string.Join(string.Empty, dictionary.Select(property => $".add('{property.Key}', '{property.Value}')"))},");
        }

        private static string DictionaryToExpression(string memberName, Dictionary<string, DiscriminatingUnion<string, UnitValue>> dictionary)
        {
            return (dictionary == null ?
                string.Empty :
                $"{memberName}: Map.empty<string, (PassthroughEnvironmentVariable | string)>(){ string.Join(string.Empty, dictionary.Select(property => $".add('{property.Key}', {(property.Value?.GetValue() is UnitValue ? "Unit.unit()" : $"'{property.Value?.GetValue()}'")})")) },");
        }

        protected static FullSymbol GetValueSymbolFromProjectRelPath(SymbolTable symbolTable, StringTable stringTable, string relativePath)
        {
            var p = RelativePath.Create(stringTable, relativePath).RemoveExtension(stringTable);
            return FullSymbol.Create(symbolTable, PipConstructionUtilities.SanitizeStringForSymbol(p.ToString(stringTable)));
        }
    }
}
