// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;
using System.Linq;
using BuildXL.Scheduler;
using BuildXL.Utilities.Configuration;
using Test.BuildXL.FrontEnd.Core;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace Test.BuildXL.FrontEnd.Rush.IntegrationTests
{
    [Trait("Category", "RushIntegrationTests")]
    public class RushEnforceUndeclaredReadsTest : RushIntegrationTestBase
    {
        public RushEnforceUndeclaredReadsTest(ITestOutputHelper output)
            : base(output)
        {
        }

        // We need to execute in order to verify the presence of DFAs
        protected override EnginePhases Phase => EnginePhases.Execute;

        /// There is nothing Linux-specific with this test, but under CloudBuild we run with additional directory
        /// translations (e.g. Out folder is usually a reparse point) that this test infra is not aware of, so paths
        /// are not properly translated
        [TheoryIfSupported(requiresLinuxBasedOperatingSystem: true)]
        [InlineData(true)]
        [InlineData(false)]
        public void ReadOutOfAllowedUndeclaredReadsConeIsFlagged(bool declareDependency)
        {
            // On CB there is a reparse point involved in the read scopes, so turn on reparse point resolving so scopes consider reparse points
            var config = Build(enforceSourceReadsUnderPackageRoots: true, enableFullReparsePointResolving: true)
                // Package A access its own project root cone
                .AddJavaScriptProject("@ms/project-A", "src/A", "const fs = require('fs'); fs.existsSync('A.txt');")
                // Package B access A's sources
                .AddJavaScriptProject("@ms/project-B", "src/B", "const fs = require('fs'); fs.existsSync('../A/A.txt');", declareDependency? new string[] { "@ms/project-A" } : null)
                .AddSpec("src/A/A.txt", "A source file")
                .PersistSpecsAndGetConfiguration();

            var engineResult = RunRushProjects(
                config, 
                new[] {
                    ("src/A", "@ms/project-A"),
                    ("src/B", "@ms/project-B"),
                }, 
                overrideDisableReparsePointResolution: false);

            if (declareDependency)
            {
                Assert.True(engineResult.IsSuccess);
            }
            else
            {
                Assert.False(engineResult.IsSuccess);
                AssertErrorEventLogged(global::BuildXL.Scheduler.Tracing.LogEventId.DependencyViolationDisallowedUndeclaredSourceRead);
            }
        }

        /// There is nothing Linux-specific with this test, but under CloudBuild we run with additional directory
        /// translations (e.g. Out folder is usually a reparse point) that this test infra is not aware of, so paths
        /// are not properly translated
        [TheoryIfSupported(requiresLinuxBasedOperatingSystem: true)]
        [InlineData(".*FileA", true)]
        [InlineData(".*FileB", false)]
        public void RegexMatchingIsHonored(string regex, bool success)
        {
            var config = Build(enforceSourceReadsUnderPackageRoots: true, enableFullReparsePointResolving: true, additionalSourceReadsScopes: $"['{regex}']")
                // Package A access a file outside of its project root
                .AddJavaScriptProject("@ms/project-A", "src/A", "const fs = require('fs'); fs.existsSync('../FileA');")
                .PersistSpecsAndGetConfiguration();

            // Produce the file A is going to read
            File.WriteAllText(config.Layout.SourceDirectory.Combine(PathTable, "src").Combine(PathTable, "FileA").ToString(PathTable), "test");

            var engineResult = RunRushProjects(
                config,
                new[] {
                    ("src/A", "@ms/project-A")
                },
                overrideDisableReparsePointResolution: false);

            if (success)
            {
                Assert.True(engineResult.IsSuccess);
                // Make sure we use the regex strategy when there are few regexes (OR-ed expressions)
                var process = engineResult.EngineState.RetrieveProcess("@ms/project-A");
                Assert.NotNull(process);
                Assert.True(FileMonitoringViolationAnalyzer.ShouldUseRegexORStrategy(process.AllowedUndeclaredSourceReadRegexes));
            }
            else
            {
                Assert.False(engineResult.IsSuccess);
                AssertErrorEventLogged(global::BuildXL.Scheduler.Tracing.LogEventId.DependencyViolationDisallowedUndeclaredSourceRead);
            }
        }

        /// There is nothing Linux-specific with this test, but under CloudBuild we run with additional directory
        /// translations (e.g. Out folder is usually a reparse point) that this test infra is not aware of, so paths
        /// are not properly translated
        [FactIfSupported(requiresLinuxBasedOperatingSystem: true)]
        public void RegexMatchingStrategyIsHonored()
        {
            // Generate a large number of regexes to force the use of the regex strategy that applies only after more than 100 regexes are present.
            var regexes = Enumerable.Range(0, 100).Select(index => $"'.*File{index}'").ToList();
            // Append at the end the regex we want to test
            regexes.Add("'.*FileA'");

            var config = Build(enforceSourceReadsUnderPackageRoots: true, enableFullReparsePointResolving: true, additionalSourceReadsScopes: $"[{string.Join(",", regexes)}]")
                // Package A access a file outside of its project root
                .AddJavaScriptProject("@ms/project-A", "src/A", "const fs = require('fs'); fs.existsSync('../FileA');")
                .PersistSpecsAndGetConfiguration();

            // Produce the file A is going to read
            File.WriteAllText(config.Layout.SourceDirectory.Combine(PathTable, "src").Combine(PathTable, "FileA").ToString(PathTable), "test");

            var engineResult = RunRushProjects(
                config,
                new[] {
                    ("src/A", "@ms/project-A")
                },
                overrideDisableReparsePointResolution: false);

            // The last regex should match and thus the read is allowed
            Assert.True(engineResult.IsSuccess);

            // Verify that we added enough regexes to trigger the individual matching strategy (rather than OR-ed expressions)
            var process = engineResult.EngineState.RetrieveProcess("@ms/project-A");
            Assert.NotNull(process);
            Assert.False(FileMonitoringViolationAnalyzer.ShouldUseRegexORStrategy(process.AllowedUndeclaredSourceReadRegexes));
        }

        [TheoryIfSupported(requiresLinuxBasedOperatingSystem: true)]
        [InlineData("project-A", true)]
        [InlineData("project-B", false)]
        public void UseProjectSelector(string selectorRegex, bool success)
        {
            var scopeWithSelector = @$"[{{ scope: "".*FileA"", packages: [{{ packageNameRegex: ""{selectorRegex}"" }}] }}]";
            var config = Build(enforceSourceReadsUnderPackageRoots: true, enableFullReparsePointResolving: true, additionalSourceReadsScopes: scopeWithSelector)
                // Package A access a file outside of its project root
                .AddJavaScriptProject("@ms/project-A", "src/A", "const fs = require('fs'); fs.existsSync('../FileA');")
                .PersistSpecsAndGetConfiguration();

            // Produce the file A is going to read
            File.WriteAllText(config.Layout.SourceDirectory.Combine(PathTable, "src").Combine(PathTable, "FileA").ToString(PathTable), "test");

            var engineResult = RunRushProjects(
                config,
                new[] {
                    ("src/A", "@ms/project-A")
                },
                overrideDisableReparsePointResolution: false);

            if (success)
            {
                Assert.True(engineResult.IsSuccess);
            }
            else
            {
                Assert.False(engineResult.IsSuccess);
                AssertErrorEventLogged(global::BuildXL.Scheduler.Tracing.LogEventId.DependencyViolationDisallowedUndeclaredSourceRead);
            }
        }

        [FactIfSupported(requiresLinuxBasedOperatingSystem: true)]
        public void AbsentProbesAreAlwaysAllowed()
        {
            var config = Build(enforceSourceReadsUnderPackageRoots: true, enableFullReparsePointResolving: true)
                // Package A access a non-existent file outside of its project root
                .AddJavaScriptProject("@ms/project-A", "src/A", "const fs = require('fs'); fs.existsSync('../NonExistentFile');")
                .PersistSpecsAndGetConfiguration();

            var engineResult = RunRushProjects(
                config,
                new[] {
                    ("src/A", "@ms/project-A")
                },
                overrideDisableReparsePointResolution: false);

            // The file is not in an allowed scope, but it does not exist, so it is not a violation
            Assert.True(engineResult.IsSuccess);
        }
    }
}
