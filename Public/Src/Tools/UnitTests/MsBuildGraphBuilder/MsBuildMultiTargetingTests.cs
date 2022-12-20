// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BuildXL.FrontEnd.MsBuild.Serialization;
using Microsoft.Build.Graph;
using Test.BuildXL.TestUtilities.Xunit;
using Test.ProjectGraphBuilder.Utilities;
using Xunit;
using Xunit.Abstractions;

namespace Test.Tool.ProjectGraphBuilder
{
    public class MsBuildMultiTargetingTests : GraphBuilderToolTestBase
    {
        private readonly MsBuildProjectBuilder m_builder;

        public MsBuildMultiTargetingTests(ITestOutputHelper output) : base(output) => m_builder = new MsBuildProjectBuilder(TemporaryDirectory);

        [Theory]
        [MemberData(nameof(TruthTable.GetTable), 1, MemberType = typeof(TruthTable))]
        public void SimpleMultiTargetingTest(bool useLegacyProjectIsolation)
        {
            const string MultiTargetingProject =
@"
<Project>
    <PropertyGroup>
        <TargetFrameworks>net472;netstandard2.0</TargetFrameworks>
        <Configuration>Debug</Configuration>
        <Platform>x64</Platform>
        <OutDir>objd\amd64</OutDir>
        <InnerBuildProperty>TargetFramework</InnerBuildProperty>
        <InnerBuildPropertyValues>TargetFrameworks</InnerBuildPropertyValues>
    </PropertyGroup>
</Project>
";
            string entryPoint = m_builder.WriteProjectsWithReferences(("Multi.proj", MultiTargetingProject));
            MSBuildGraphBuilderArguments args = CreateBuilderArguments(new[] {entryPoint}, useLegacyProjectIsolation: useLegacyProjectIsolation);
            ProjectGraphWithPredictionsResult<string> buildResult = BuildGraphAndDeserialize(args);
            XAssert.IsTrue(buildResult.Succeeded, buildResult.Failure?.Message ?? string.Empty);

            ProjectGraphWithPredictions<string> projectGraph = buildResult.Result;
            XAssert.AreEqual(useLegacyProjectIsolation ? 1 : 3, projectGraph.ProjectNodes.Length);
            XAssert.All(projectGraph.ProjectNodes, p => p.GlobalProperties.TryGetValue("IsGraphBuild", out string value) && value == "true");
            HashSet<string> tfs = projectGraph.ProjectNodes.Select(p => p.GlobalProperties.TryGetValue("TargetFramework", out string tf) ? tf : string.Empty).ToHashSet();
            HashSet<string> expectedTfs = new HashSet<string>() { string.Empty };

            if (!useLegacyProjectIsolation) 
            {
                expectedTfs.Add("net472");
                expectedTfs.Add("netstandard2.0");
            }

            XAssert.SetEqual(expectedTfs, tfs);
        }

        [Fact]
        public void ProjectsAreNotMergedWhenProjectDimensionIsUnspecified()
        {
            const string MultiTargetingProject =
@"
<Project>
    <PropertyGroup>
        <TargetFrameworks>net472;netstandard2.0</TargetFrameworks>
        <OutDir>objd\amd64</OutDir>
        <InnerBuildProperty>TargetFramework</InnerBuildProperty>
        <InnerBuildPropertyValues>TargetFrameworks</InnerBuildPropertyValues>
    </PropertyGroup>
</Project>
";
            string entryPoint = m_builder.WriteProjectsWithReferences(("Multi.proj", MultiTargetingProject));
            MSBuildGraphBuilderArguments args = CreateBuilderArguments(new[] { entryPoint }, useLegacyProjectIsolation: true);
            ProjectGraphWithPredictionsResult<string> buildResult = BuildGraphAndDeserialize(args);
            XAssert.IsTrue(buildResult.Succeeded);
            XAssert.AreEqual(3, buildResult.Result.ProjectNodes.Length);
        }

        [Fact]
        public void ChainMultiTargetingTest()
        {
            const string ProjRefTemplate =
@"
    <ItemGroup>
        <ProjectReference Include=""__REF__""/>
    </ItemGroup>
";
            const string MultiTargetingProjectTemplate =
@"
<Project>
    <PropertyGroup>
        <TargetFrameworks>net472;netstandard2.0</TargetFrameworks>
        <Configuration>Debug</Configuration>
        <Platform>x64</Platform>
        <OutDir>objd\amd64</OutDir>
        <InnerBuildProperty>TargetFramework</InnerBuildProperty>
        <InnerBuildPropertyValues>TargetFrameworks</InnerBuildPropertyValues>
    </PropertyGroup>
    __PROJ_REF__
</Project>
";
            string projRef1 = ProjRefTemplate.Replace("__REF__", "Multi2.proj");
            string multiTargetingProject1 = MultiTargetingProjectTemplate.Replace("__PROJ_REF__", projRef1);
            string projRef2 = ProjRefTemplate.Replace("__REF__", "Multi3.proj");
            string multiTargetingProject2 = MultiTargetingProjectTemplate.Replace("__PROJ_REF__", projRef2);
            string multiTargetingProject3 = MultiTargetingProjectTemplate.Replace("__PROJ_REF__", string.Empty);
            string entryPoint = m_builder.WriteProjectsWithReferences(
                ("Multi1.proj", multiTargetingProject1),
                ("Multi2.proj", multiTargetingProject2),
                ("Multi3.proj", multiTargetingProject3));

            MSBuildGraphBuilderArguments args = CreateBuilderArguments(new[] { entryPoint }, useLegacyProjectIsolation: true);
            ProjectGraphWithPredictionsResult<string> buildResult = BuildGraphAndDeserialize(args);
            XAssert.IsTrue(buildResult.Succeeded, buildResult.Failure?.Message ?? string.Empty);

            ProjectGraphWithPredictions<string> projectGraph = buildResult.Result;
            XAssert.AreEqual(3, projectGraph.ProjectNodes.Length);

            ProjectWithPredictions<string> multi1Node = projectGraph.ProjectNodes.FirstOrDefault(p => p.FullPath.EndsWith("Multi1.proj"));
            XAssert.IsNotNull(multi1Node);

            ProjectWithPredictions<string> multi2Node = projectGraph.ProjectNodes.FirstOrDefault(p => p.FullPath.EndsWith("Multi2.proj"));
            XAssert.IsNotNull(multi2Node);

            ProjectWithPredictions<string> multi3Node = projectGraph.ProjectNodes.FirstOrDefault(p => p.FullPath.EndsWith("Multi3.proj"));
            XAssert.IsNotNull(multi3Node);

            XAssert.AreEqual(1, multi1Node.Dependencies.Count);
            XAssert.AreEqual(multi2Node, multi1Node.Dependencies.First());

            XAssert.AreEqual(1, multi2Node.Dependencies.Count);
            XAssert.AreEqual(multi3Node, multi2Node.Dependencies.First());
        }

        private MSBuildGraphBuilderArguments CreateBuilderArguments(
            string[] entryPointPaths,
            bool useLegacyProjectIsolation)
        {
            string outputFile = Path.Combine(TemporaryDirectory, Guid.NewGuid().ToString());
            var arguments = GetStandardBuilderArguments(
                entryPointPaths,
                outputFile,
                globalProperties: GlobalProperties.Empty,
                entryPointTargets: Array.Empty<string>(),
                requestedQualifiers: new GlobalProperties[] { GlobalProperties.Empty },
                allowProjectsWithoutTargetProtocol: true,
                useLegacyProjectIsolation: useLegacyProjectIsolation);

            return arguments;
        }
    }
}
