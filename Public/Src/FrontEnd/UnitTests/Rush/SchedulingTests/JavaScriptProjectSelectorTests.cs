// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Linq;
using BuildXL.FrontEnd.JavaScript;
using BuildXL.FrontEnd.JavaScript.ProjectGraph;
using BuildXL.Utilities.Core;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Configuration.Mutable;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using System.Collections.Generic;
using System;
using BuildXL.Utilities.Configuration;

namespace Test.BuildXL.FrontEnd.Rush.SchedulingTests
{
    public class JavaScriptProjectSelectorTests
    {
        private readonly PathTable m_pathTable;

        public JavaScriptProjectSelectorTests()
        {
            m_pathTable = new PathTable();
        }

        [Fact]
        public void ValidateStringMatch()
        {
            var aBuild = CreateJavaScriptProject("project-A", "build");
            var aTest = CreateJavaScriptProject("project-A", "test");
            var bBuild = CreateJavaScriptProject("project-B", "build");
            var matcher = new JavaScriptProjectSelector(new[] { aBuild, aTest, bBuild });
            var selector = "project-A";
            List<JavaScriptProject> allProjects = [ aBuild, aTest, bBuild ];
            XAssert.IsTrue(matcher.TryGetMatches(new(selector), out var result, out _));
            XAssert.Contains(result, aBuild, aTest);
            XAssert.ContainsNot(result, bBuild);

            ValidateTryMatchConsistency(allProjects, result, new(selector));
        }

        [Theory]
        [InlineData("project-A", new[] { "build", "test" }, 2)]
        [InlineData("project-B", new[] { "build", "test" }, 0)]
        [InlineData("project-B", new[] { "lint" }, 1)]
        [InlineData("project-B", new[] { "foo" }, 0)]
        [InlineData("project-C", new[] { "build", "test" }, 0)]
        [InlineData("project-A", null, 3)]
        public void ValidateSimpleMatch(string packageName, string[] commands, int expectedProjectCount)
        {
            var aBuild = CreateJavaScriptProject("project-A", "build");
            var aTest = CreateJavaScriptProject("project-A", "test");
            var aLint = CreateJavaScriptProject("project-A", "lint");
            var bLint = CreateJavaScriptProject("project-B", "lint");

            var matcher = new JavaScriptProjectSelector(new[] { aBuild, aTest, aLint, bLint });
            var selector = new JavaScriptProjectSimpleSelector() { PackageName = packageName, Commands = commands };
            List<JavaScriptProject> allProjects = [ aBuild, aTest, aLint, bLint ];
            XAssert.IsTrue(matcher.TryGetMatches(new(selector), out var result, out _));

            XAssert.AreEqual(expectedProjectCount,  result.Count);
            XAssert.IsTrue(result.All(project => project.Name == packageName));
            XAssert.IsTrue(result.All(project => commands?.Contains(project.ScriptCommandName) != false));

            ValidateTryMatchConsistency(allProjects, result, new(selector));
        }

        [Theory]
        [InlineData(".*", null, 5)]
        [InlineData(".*", ".*", 5)]
        [InlineData(".*", "test", 2)]
        [InlineData("project-.", null, 5)]
        [InlineData(".*B", null, 2)]
        [InlineData("project-.", "te.*", 2)]
        public void ValidateRegexMatch(string packageName, string commands, int expectedProjectCount)
        {
            var aBuild = CreateJavaScriptProject("project-A", "build");
            var aTest = CreateJavaScriptProject("project-A", "test");
            var aLint = CreateJavaScriptProject("project-A", "lint");
            var bTest = CreateJavaScriptProject("project-B", "test");
            var bLint = CreateJavaScriptProject("project-B", "lint");

            var matcher = new JavaScriptProjectSelector(new[] { aBuild, aTest, aLint, bTest, bLint });
            var selector = new JavaScriptProjectRegexSelector() { PackageNameRegex = packageName, CommandRegex = commands };
            List<JavaScriptProject> allProjects = [aBuild, aTest, aLint, bTest, bLint];
            XAssert.IsTrue(matcher.TryGetMatches(new(selector), out var result, out _));
            XAssert.AreEqual(expectedProjectCount, result.Count);

            ValidateTryMatchConsistency(allProjects, result, new(selector));
        }

        private void ValidateTryMatchConsistency(IReadOnlyCollection<JavaScriptProject> allProjects, IReadOnlyCollection<JavaScriptProject> result, DiscriminatingUnion<string, IJavaScriptProjectSimpleSelector, IJavaScriptProjectRegexSelector> selector)
        {
            // Every project in the result should match the selector individually
            foreach (var project in allProjects)
            {
                XAssert.IsTrue(JavaScriptProjectSelector.TryMatch(selector, project, out var isMatch, out _));
                if (result.Any(p => p.ToString() == project.ToString())) // Basic equality enough for this test
                {
                    XAssert.IsTrue(isMatch);
                }
                else
                {
                    XAssert.IsFalse(isMatch);
                }
            }
        }

        private JavaScriptProject CreateJavaScriptProject(string name, string scriptCommandName)
        {
            return new JavaScriptProject(
                name, 
                AbsolutePath.Create(m_pathTable, @"\\path\to\foo"), 
                scriptCommandName, 
                "a script command", 
                AbsolutePath.Create(m_pathTable, @"\\path\to\temp"), 
                CollectionUtilities.EmptyArray<AbsolutePath>(), 
                CollectionUtilities.EmptyArray<FileArtifact>(), 
                CollectionUtilities.EmptyArray<DirectoryArtifact>());
        }
    }
}
