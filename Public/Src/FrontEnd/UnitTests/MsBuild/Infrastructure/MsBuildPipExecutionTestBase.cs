// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

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
using Test.DScript.Ast;
using Test.BuildXL.FrontEnd.Core;
using Xunit.Abstractions;

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

        protected MsBuildPipExecutionTestBase(ITestOutputHelper output) : base(output, true)
        {
            RegisterEventSource(global::BuildXL.Engine.ETWLogger.Log);
            RegisterEventSource(global::BuildXL.Processes.ETWLogger.Log);
            RegisterEventSource(global::BuildXL.Scheduler.ETWLogger.Log);
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
        protected SpecEvaluationBuilder BuildWithEnvironment(Dictionary<string, string> environment)
        {
            return base.Build().Configuration(DefaultMsBuildPrelude(runInContainer: false, environment));
        }

        /// <inheritdoc/>
        protected SpecEvaluationBuilder Build(bool runInContainer = false, Dictionary<string, string> environment = null, Dictionary<string, string> globalProperties = null)
        {
            // Let's explicitly pass an empty environment, so the process environment won't affect tests by default
            return base.Build().Configuration(DefaultMsBuildPrelude(runInContainer, environment: environment ?? new Dictionary<string, string>(), globalProperties));
        }

        /// <inheritdoc/>
        protected SpecEvaluationBuilder Build(string configExtraArguments)
        {
            return base.Build().Configuration(DefaultMsBuildPrelude(configExtraArguments));
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

        protected BuildXLEngineResult RunEngineWithConfig(ICommandLineConfiguration config, TestCache testCache = null)
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
                    testCache: testCache);

                return engineResult;
            }
        }

        /// <summary>
        /// Returns a project that just echoes 'Hello World'
        /// </summary>
        protected string CreateHelloWorldProject()
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
        protected string CreateProjectWithTarget(string targetName)
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
        <WriteFile
            Path='$(OutDir)/{outputFilename}'
            Content='Test'/>
    </Target>
</Project>";
        }

        /// <summary>
        /// Returns a task that writes a file with a given content, making sure all directories are created
        /// </summary>
        /// <returns></returns>
        private string GetWriteFileTask()
        {
            return
$@"<UsingTask TaskName='WriteFile' TaskFactory='CodeTaskFactory' AssemblyFile='{TestDeploymentDir}\Microsoft.Build.Tasks.Core.dll'>
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
        /// Returns a 'dirs' project, listing all specified <paramref name="projectNames"/>
        /// </summary>
        protected string CreateDirsProject(params string[] projectNames)
        {
            StringBuilder projectList = new StringBuilder();
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
            bool runInContainer = false, 
            Dictionary<string, string> environment = null, 
            Dictionary<string, string> globalProperties = null,
            bool enableBinLogTracing = false,
            bool enableEngineTracing = false,
            string logVerbosity = null,
            bool allowProjectsToNotSpecifyTargetProtocol = true) => $@"
config({{
    disableDefaultSourceResolver: true,
    resolvers: [
        {{
            kind: 'MsBuild',
            moduleName: 'Test',
            msBuildSearchLocations: [d`{TestDeploymentDir}`],
            root: d`.`,
            allowProjectsToNotSpecifyTargetProtocol: {(allowProjectsToNotSpecifyTargetProtocol ? "true" : "false")},
            runInContainer: {(runInContainer ? "true" : "false")},
            {DictionaryToExpression("environment", environment)}
            {DictionaryToExpression("globalProperties", globalProperties)}
            enableBinLogTracing: {(enableBinLogTracing ? "true" : "false")},
            enableEngineTracing: {(enableEngineTracing? "true" : "false")},
            {(logVerbosity != null ? $"logVerbosity: {logVerbosity}," : string.Empty)}
        }},
    ],
}});";

        private string DefaultMsBuildPrelude(
            string extraArguments) => $@"
config({{
    disableDefaultSourceResolver: true,
    resolvers: [
        {{
            kind: 'MsBuild',
            moduleName: 'Test',
            msBuildSearchLocations: [d`{TestDeploymentDir}`],
            root: d`.`,
            allowProjectsToNotSpecifyTargetProtocol: true,
            {DictionaryToExpression("environment", new Dictionary<string, string>())}
            {extraArguments ?? string.Empty}
        }},
    ],
}});";

        private static string DictionaryToExpression(string memberName, Dictionary<string, string> dictionary)
        {
            return (dictionary == null ? 
                string.Empty : 
                $"{memberName}: Map.empty<string, string>(){string.Join(string.Empty, dictionary.Select(property => $".add('{property.Key}', '{property.Value}')"))},");
        }
    }
}
