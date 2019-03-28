// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml;
using Microsoft.Build.Construction;
using Microsoft.Build.Definition;
using Microsoft.Build.Evaluation;
using Xunit;

namespace Microsoft.Build.Prediction.Tests
{
    internal static class TestHelpers
    {
        public static void AssertPredictions(
            this StaticPredictions predictions,
            IReadOnlyCollection<BuildInput> expectedBuildInputs,
            IReadOnlyCollection<BuildOutputDirectory> expectedBuildOutputDirectories)
        {
            Assert.NotNull(predictions);

            if (expectedBuildInputs == null)
            {
                Assert.Equal(0, predictions.BuildInputs.Count);
            }
            else
            {
                CheckCollection(expectedBuildInputs, predictions.BuildInputs, BuildInput.ComparerInstance, "inputs");
            }

            if (expectedBuildOutputDirectories == null)
            {
                Assert.Equal(0, predictions.BuildOutputDirectories.Count);
            }
            else
            {
                CheckCollection(expectedBuildOutputDirectories, predictions.BuildOutputDirectories, BuildOutputDirectory.ComparerInstance, "outputs");
            }
        }

        public static Project ProjectFromXml(string xml)
        {
            var settings = new XmlReaderSettings
            {
                XmlResolver = null,  // Prevent external calls for namespaces.
            };

            using (var stringReader = new StringReader(xml))
            using (var xmlReader = XmlReader.Create(stringReader, settings))
            {
                ProjectRootElement projectRootElement = ProjectRootElement.Create(xmlReader);
                return Project.FromProjectRootElement(projectRootElement, new ProjectOptions());
            }
        }

        public static Project CreateProjectFromRootElement(ProjectRootElement projectRootElement)
        {
            var globalProperties = new Dictionary<string, string>
                                   {
                                       { "Platform", "amd64" },
                                       { "Configuration", "debug" },
                                   };
            // TODO: Remove the hardcoded toolsVersion.
            // We are hardcoding the toolVersion value since the Microsoft.Build version that we are currently using
            // throws when calling Project.FromProjectRootElement(projectRootElement, new ProjectOptions());
            return new Project(projectRootElement, globalProperties, toolsVersion: "4.0");
        }

        private static void CheckCollection<T>(IReadOnlyCollection<T> expected, IReadOnlyCollection<T> actual, IEqualityComparer<T> comparer, string type)
        {
            var actualSet = new HashSet<T>(actual, comparer);
            var expectedSet = new HashSet<T>(expected, comparer);

            List<T> expectedNotInActual = expected.Where(i => !actualSet.Contains(i)).ToList();
            List<T> actualNotExpected = actual.Where(i => !expectedSet.Contains(i)).ToList();
            if (expectedSet.Count != actualSet.Count)
            {
                throw new ArgumentException(
                    $"Mismatched count - expected {expectedSet.Count} but got {actualSet.Count}. \r\n" +
                    $"Expected {type} [[{string.Join(Environment.NewLine, expected)}]] \r\n" +
                    $"Actual [[{string.Join(Environment.NewLine, actual)}]] \r\n" +
                    $"Extra expected [[{string.Join(Environment.NewLine, expectedNotInActual)}]] \r\n" +
                    $"Extra actual [[{string.Join(Environment.NewLine, actualNotExpected)}]]");
            }

            foreach (T expectedItem in expectedSet)
            {
                Assert.True(
                    actualSet.Contains(expectedItem),
                    $"Missed value in the {type}: {expectedItem} from among actual list {string.Join(":: ", actual)}");
            }
        }

        public static string GetAssemblyLocation()
        {
            return Path.GetDirectoryName(new Uri(Assembly.GetExecutingAssembly().CodeBase).LocalPath);
        }
    }
}
