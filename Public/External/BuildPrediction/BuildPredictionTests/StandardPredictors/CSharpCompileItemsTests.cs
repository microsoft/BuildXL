// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using Microsoft.Build.Construction;
using Microsoft.Build.Definition;
using Microsoft.Build.Evaluation;
using Microsoft.Build.Execution;
using Microsoft.Build.Prediction.StandardPredictors;
using Xunit;

namespace Microsoft.Build.Prediction.Tests.StandardPredictors
{
    // TODO: Need to add .NET Core and .NET Framework based examples including use of SDK includes.
    public class CSharpCompileItemsTests
    {
        [SuppressMessage("Microsoft.Usage", "CA1801:ReviewUnusedParameters", MessageId = "testContext", Justification = "Needed for reflection")]
        static CSharpCompileItemsTests()
        {
            MsBuildEnvironment.Setup(TestHelpers.GetAssemblyLocation());
        }

        [Fact]
        public void CSharpFilesFoundFromDirectListingInCsproj()
        {
            Project project = CreateTestProject("Test.cs");
            ProjectInstance projectInstance = project.CreateProjectInstance(ProjectInstanceSettings.ImmutableWithFastItemLookup);
            var predictor = new CSharpCompileItems();
            bool hasPredictions = predictor.TryPredictInputsAndOutputs(project, projectInstance, @"C:\repo", out StaticPredictions predictions);
            Assert.True(hasPredictions);
            predictions.AssertPredictions(new[] { new BuildInput(Path.Combine(project.DirectoryPath, "Test.cs"), isDirectory: false) }, null);
        }

        private static Project CreateTestProject(params string[] compileItemIncludes)
        {
            ProjectRootElement projectRootElement = ProjectRootElement.Create();
            ProjectItemGroupElement itemGroup = projectRootElement.AddItemGroup();
            foreach (string compileItemInclude in compileItemIncludes)
            {
                itemGroup.AddItem("Compile", compileItemInclude);
            }

            return TestHelpers.CreateProjectFromRootElement(projectRootElement);
        }
    }
}
