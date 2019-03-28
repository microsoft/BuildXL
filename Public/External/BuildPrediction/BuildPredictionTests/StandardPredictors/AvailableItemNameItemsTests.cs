// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
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
    public class AvailableItemNameItemsTests
    {
        [SuppressMessage("Microsoft.Usage", "CA1801:ReviewUnusedParameters", MessageId = "testContext", Justification = "Needed for reflection")]
        static AvailableItemNameItemsTests()
        {
            MsBuildEnvironment.Setup(TestHelpers.GetAssemblyLocation());
        }

        [Fact]
        public void AvailableItemNamesFindItems()
        {
            Project project = CreateTestProject(
                new[] { "Available1", "Available2" },
                Tuple.Create("Available1", "available1Value"),
                Tuple.Create("Available1", "available1Value2"),
                Tuple.Create("Available2", "available2Value"),
                Tuple.Create("NotAvailable", "shouldNotGetThisAsAnInput"));
            ProjectInstance projectInstance = project.CreateProjectInstance(ProjectInstanceSettings.ImmutableWithFastItemLookup);
            var predictor = new AvailableItemNameItems();
            bool hasPredictions = predictor.TryPredictInputsAndOutputs(project, projectInstance, @"C:\repo", out StaticPredictions predictions);
            Assert.True(hasPredictions);
            predictions.AssertPredictions(
                new[]
                {
                    new BuildInput(Path.Combine(project.DirectoryPath, "available1Value"), isDirectory: false),
                    new BuildInput(Path.Combine(project.DirectoryPath,"available1Value2"), isDirectory: false),
                    new BuildInput(Path.Combine(project.DirectoryPath,"available2Value"), isDirectory: false),
                }, null);
        }

        private static Project CreateTestProject(IEnumerable<string> availableItemNames, params Tuple<string, string>[] itemNamesAndValues)
        {
            ProjectRootElement projectRootElement = ProjectRootElement.Create();

            // Add Items.
            ProjectItemGroupElement itemGroup = projectRootElement.AddItemGroup();
            foreach (Tuple<string, string> itemNameAndValue in itemNamesAndValues)
            {
                itemGroup.AddItem(itemNameAndValue.Item1, itemNameAndValue.Item2);
            }

            // Add AvailableItemName items referring to the item names we'll add soon.
            ProjectItemGroupElement namesItemGroup = projectRootElement.AddItemGroup();
            foreach (string availableItemName in availableItemNames)
            {
                namesItemGroup.AddItem(AvailableItemNameItems.AvailableItemName, availableItemName);
            }

            return TestHelpers.CreateProjectFromRootElement(projectRootElement);
        }
    }
}
