// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using BuildXL.Engine;
using BuildXL.Utilities;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.FrontEnd.Script.Constants;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Configuration.Mutable;
using BuildXL.FrontEnd.Script;
using BuildXL.FrontEnd.Script.Tracing;
using BuildXL.FrontEnd.Script.Evaluator;
using Test.BuildXL.FrontEnd.Core;
using Xunit;
using Xunit.Abstractions;

namespace Test.DScript.Ast.Configuration
{
    /// <summary>
    /// This test is very similar to ConfigurationConverterTests from Test.DScript
    /// but it converts string representation to the <see cref="ObjectLiteral"/> first
    /// then calls conversion function.
    /// This approach greatly improves readability of the tests and should be preferable way of testing.
    /// </summary>
    public class TestConfiguration : DsTest
    {
        public TestConfiguration(ITestOutputHelper output) : base(output)
        {}

        [Theory]
        [InlineData("inCloudBuild: true", /*originalValue*/null, /*expectedValue*/ true)]
        [InlineData("inCloudBuild: true", /*originalValue*/false, /*expectedValue*/ false)]
        [InlineData("inCloudBuild: false", /*originalValue*/null, /*expectedValue*/ false)]

        // Undefined and missing properties should behave similarly
        [InlineData("inCloudBuild: undefined", /*originalValue*/true, /*expectedValue*/ true)]
        [InlineData("inCloudBuild: undefined", /*originalValue*/null, /*expectedValue*/ null)]
        [InlineData("inCloudBuild: undefined", /*originalValue*/false, /*expectedValue*/ false)]

        [InlineData("", /*originalValue*/true, /*expectedValue*/ true)]
        [InlineData("", /*originalValue*/null, /*expectedValue*/ null)]
        [InlineData("", /*originalValue*/false, /*expectedValue*/ false)]
        public void TestBooleanProperty(string configurationLiteral, bool? originalValue, bool? expectedValue)
        {
            string code = string.Format(CultureInfo.InvariantCulture, @"
config({{
  {0}
}});", configurationLiteral);

            ICommandLineConfiguration commandLine = originalValue != null
                ? new CommandLineConfiguration(new CommandLineConfiguration() { InCloudBuild = originalValue.Value })
                : null;

            IConfiguration configuration = ParseConfigurationSuccessfully(code, commandLine);

            Assert.Equal(expectedValue, configuration.InCloudBuild);
        }

        [Fact]
        public void ConvertEnabledCustomRules()
        {
            string code = @"
config({
  frontEnd: {
    enabledPolicyRules: [""rule1"", ""rule2"", ""rule4""],
  }
});";

            var configuration = ParseConfigurationSuccessfully(code);

            Assert.Equal(new List<string> { "rule1", "rule2", "rule4" }, configuration.FrontEnd.EnabledPolicyRules);
        }

        [Fact]
        public void UnknownResolverKind()
        {
            string code = @"
config({
  resolvers: [
    {kind: 'unknown'}
  ]
});";

            var failure = ParseConfigurationWithFailure(code);

            Assert.Equal((int)LogEventId.ConfigurationParsingFailed, failure.ErrorCode);
            Assert.Contains("Types of property 'resolvers' are incompatible", failure.FullMessage);
        }

        [Fact]
        public void MergeArrayLiteral()
        {
            string code = @"
config({
  frontEnd: {
    enabledPolicyRules: [""rule1"", ""rule2"", ""rule4""],
  }
});";

            var configuration = ParseConfigurationSuccessfully(
                code,
                new CommandLineConfiguration() { FrontEnd = new FrontEndConfiguration() { EnabledPolicyRules = new List<string>() { "rule0" } } });

            Assert.Equal(new List<string> { "rule0", "rule1", "rule2", "rule4" }, configuration.FrontEnd.EnabledPolicyRules);
        }

        [Fact]
        public void MergeObjectLiteral()
        {
            string code = @"
config({
  frontEnd: {
    enabledPolicyRules: [""rule1"", ""rule2"", ""rule4""],
  }
});";

            var configuration = ParseConfigurationSuccessfully(
                code,
                new CommandLineConfiguration() { FrontEnd = new FrontEndConfiguration() { EnabledPolicyRules = new List<string>() { "rule0" }, DebugScript = true } });

            Assert.Equal(new List<string> { "rule0", "rule1", "rule2", "rule4" }, configuration.FrontEnd.EnabledPolicyRules);
            Assert.Equal(true, configuration.FrontEnd.DebugScript());
        }

        [Fact]
        public void UndefinedForEnabledPolicyRulesShouldNotCrash()
        {
            string code = @"
config({
  frontEnd: {
    enabledPolicyRules: undefined,
  }
});";

            var configuration = ParseConfigurationSuccessfully(
                code,
                new CommandLineConfiguration() { FrontEnd = new FrontEndConfiguration() { EnabledPolicyRules = new List<string>() { "rule0" } } });

            Assert.Equal(new List<string> { "rule0" }, configuration.FrontEnd.EnabledPolicyRules);
        }

        [Fact]
        public void UndefinedInListShouldNotOverrideOriginalConfiguration()
        {
            string code = @"
config({
  resolvers: undefined
});";

            var configuration = ParseConfigurationSuccessfully(
                code,
                new CommandLineConfiguration() { Resolvers = new List<IResolverSettings>() { new SourceResolverSettings() { Kind = "SourceResolver" } } });

            Assert.Equal(1, configuration.Resolvers.Count);
        }

        [Fact]
        public void ConvertConfigWithDisabledDefaultSourceResolver()
        {
            string code = @"
config({
  disableDefaultSourceResolver: true
});";

            var configuration = ParseConfigurationSuccessfully(code);

            Assert.True(configuration.DisableDefaultSourceResolver);
        }

        [Fact]
        public void ConvertEnums()
        {
            string code = @"
config({
  frontEnd: {
    nameResolutionSemantics: 1
  }
});";

            var configuration = ParseConfigurationSuccessfully(code);

            Assert.Equal(NameResolutionSemantics.ImplicitProjectReferences, configuration.FrontEnd.NameResolutionSemantics());
        }

        [Fact]
        public void ConvertFailsWithUnknownConfigureFormat()
        {
            // TODO:ST: need to check error message. Currently it carries low-level information.
            // Actual message should say something like: configuration is invalid - blah-blah-blah!
            string code = @"
config({
  unknownProperty: {
    typeCheck: false,
    enabledPolicyRules: [""rule1"", ""rule2"", ""rule4""],
  }
});";

            var failure = ParseConfigurationWithFailure(code);
            Assert.Equal((int)LogEventId.ConfigurationParsingFailed, failure.ErrorCode);
            Assert.Contains("Object literal may only specify known properties, and 'unknownProperty' does not exist in type 'Configuration'", failure.FullMessage);
        }

        private IConfiguration ParseConfigurationSuccessfully(string configuration, ICommandLineConfiguration commandLine = null)
        {
            var result = ParseConfiguration(configuration, commandLine);
            Assert.False(result.HasError);

            return result.Result;
        }

        private Diagnostic ParseConfigurationWithFailure(string configuration, ICommandLineConfiguration commandLine = null)
        {
            var result = ParseConfiguration(configuration, commandLine);
            Assert.True(result.HasError);

            return result.Errors.First();
        }

        /// <summary>
        /// Helper function that parses provided configuration.
        /// </summary>
        protected TestResult<IConfiguration> ParseConfiguration(string configuration, ICommandLineConfiguration template = null)
        {
            var testWriter = CreateTestWriter();
            AddPrelude(testWriter);

            var pathTable = FrontEndContext.PathTable;

            // Need to write configuration on disk
            testWriter.AddExtraFile(Names.ConfigDsc, configuration);
            testWriter.Write(TestRoot);

            var configFile = AbsolutePath.Create(pathTable, Path.Combine(TestRoot, Names.ConfigDsc));

            var config = template == null
                ? new CommandLineConfiguration()
                {
                    Startup =
                      {
                          ConfigFile = configFile,
                      },
                    Schedule =
                      {
                          MaxProcesses = DegreeOfParallelism,
                      },
                    FrontEnd =
                      {
                          MaxFrontEndConcurrency = DegreeOfParallelism,
                          EnableIncrementalFrontEnd = false,
                          LogStatistics = false,
                      },
                    Engine =
                    {
                        LogStatistics = false,
                    },
                    Cache =
                    {
                        CacheSpecs = SpecCachingOption.Disabled,
                    }
                }
                : new CommandLineConfiguration(template)
                {
                    Startup =
                      {
                          ConfigFile = configFile,
                      },
                    FrontEnd =
                    {
                        EnableIncrementalFrontEnd = false,
                    }
                };

            BuildXLEngine.PopulateLoggingAndLayoutConfiguration(config, pathTable, bxlExeLocation: null, inTestMode: true);

            var constants = new GlobalConstants(FrontEndContext.SymbolTable);
            var moduleRegistry = new ModuleRegistry(constants.Global);

            var workspaceFactory = CreateWorkspaceFactoryForTesting(constants, moduleRegistry, ParseAndEvaluateLogger);
            var frontEndFactory = CreateFrontEndFactoryForParsingConfig(constants, moduleRegistry, workspaceFactory, ParseAndEvaluateLogger);

            CreateFrontEndHost(config, frontEndFactory, workspaceFactory, moduleRegistry, configFile, out var finalConfig, out _);
            if (finalConfig != null)
            {
                return new TestResult<IConfiguration>(finalConfig);
            }

            return new TestResult<IConfiguration>(CapturedWarningsAndErrors);
        }
    }
}
