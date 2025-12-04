// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AdoBuildRunner.Configuration.Mutable;
using AdoBuildRunner.Utilties;
using BuildXL.AdoBuildRunner;
using BuildXL.AdoBuildRunner.Utilties.Mocks;
using Xunit;

namespace Test.Tool.AdoBuildRunner
{
    /// <nodoc />
    public class BuildOverridesTests
    {
        private readonly IAdoEnvironment m_testAdoEnvironment = new MockAdoEnvironment();

        [Fact]
        public async Task DeserializationTestOverrides()
        {
            // Write testConfig for OverridesConfiguration

            var rulesJson = @$"
{{
  ""rules"": [
    {{
      ""comment"": ""build runner + cmd line"",
      ""repository"": ""repo1"",
      ""pipelineIds"": [101, 202],
      ""invocationKeys"": [""InvocationKey1"", ""InvocationKey2""],
      ""overrides"": {{
        ""additionalBuildRunnerArguments"": ""--some-arg"",
        ""additionalCommandLineArguments"": ""/p:Configuration=Release /m""
      }}
    }},
    {{
      ""comment"": ""cmd line"",
      ""repository"": ""repo2"",
      ""pipelineIds"": [101],
      ""overrides"": {{
        ""additionalCommandLineArguments"": ""/p:Configuration=Release /m""
      }}
    }},
    {{
      ""comment"": ""no cmd line, no ids"",
      ""repository"": ""repo3"",
      ""overrides"": {{
        ""additionalBuildRunnerArguments"": ""--some-other-arg"",
      }}
     }}
  ]
}}";

            var tempFile = Path.GetTempFileName();

            try
            {
                await File.WriteAllTextAsync(tempFile, rulesJson);

                var deserializedOverrides = await BuildOverridesHelper.DeserializeOverrides(tempFile, new MockLogger());
                Assert.NotNull(deserializedOverrides);
                Assert.Equal(3, deserializedOverrides.Rules.Count);
                Assert.Equal("build runner + cmd line", deserializedOverrides.Rules[0].Comment);
                Assert.Equal("repo2", deserializedOverrides.Rules[1].Repository);
                Assert.NotNull(deserializedOverrides.Rules[0].PipelineIds);
                Assert.Equal(2, deserializedOverrides.Rules[0].PipelineIds!.Count);
                Assert.NotNull(deserializedOverrides.Rules[0].Overrides.AdditionalBuildRunnerArguments);
                Assert.NotNull(deserializedOverrides.Rules[0].Overrides.AdditionalCommandLineArguments);
            }
            finally
            {
                // Clean up temp file
                if (File.Exists(tempFile))
                {
                    File.Delete(tempFile);
                }
            }
        }

        [Theory]
        [InlineData(false, false, false)]
        [InlineData(true, false, false)]
        [InlineData(false, true, true)]
        [InlineData(true, true, true)]
        [InlineData(false, true, true)]
        [InlineData(true, true, true)]
        public void Overrides(bool repoMatches, bool pipelineSpecified, bool pipelineMatches)
        {
            var repoName = m_testAdoEnvironment.RepositoryName;
            var repoPipelineId = m_testAdoEnvironment.DefinitionId;

            var overrideConfig = new OverridesConfiguration()
            {
                Rules = [
                    // A non-matching rule that should be skipped
                    // Note that when (repoMatches && pipelineSpecified) is true,
                    // we make the repo match but not the pipeline, so we check that the pipeline id matching is working correctly
                    new BuildOverridesRule() {
                        Repository = (repoMatches && pipelineSpecified) ? repoName : "not_ " + repoName,
                        PipelineIds = pipelineSpecified ? [ repoPipelineId + 1 ] : null,
                        Overrides = new BuildOverrides()
                        {
                            AdditionalCommandLineArguments = "/foo /bar"
                        }
                    },

                    // A matching rule
                    new BuildOverridesRule() {
                        Repository = repoMatches ? repoName : "not_ " + repoName,
                        PipelineIds = pipelineSpecified ? [ repoPipelineId ] : null,
                        Overrides = new BuildOverrides()
                        {
                            AdditionalCommandLineArguments = "/logObservedFileAccesses+ /logProcesses+"
                        }
                    }
                ]
            };

            var invocationKey = "InvocationKey";
            var matchingOverride = BuildOverridesHelper.TryGetMatchingOverride(invocationKey, overrideConfig, m_testAdoEnvironment, new MockLogger());

            // Matching rule when the repository name is a match,
            // and either when no pipeline is specified (so any pipeline matches), or the pipeline id is a match
            if (repoMatches && (!pipelineSpecified || pipelineMatches))
            {
                Assert.NotNull(matchingOverride);

                // The second rule should be the matching rule
                Assert.Equal("/logObservedFileAccesses+ /logProcesses+", matchingOverride.AdditionalCommandLineArguments);
            }
            else
            {
                Assert.Null(matchingOverride);
            }
        }

        [Fact]
        public void OverridesViaInvocationKey()
        {
            var repoName = m_testAdoEnvironment.RepositoryName;
            var repoPipelineId = m_testAdoEnvironment.DefinitionId;
            var invocationKey = "SpecialInvocationKey";

            var overrideConfig = new OverridesConfiguration()
            {
                Rules = [
                    // A non-matching rule that should be skipped
                    new BuildOverridesRule() {
                        Repository = repoName,
                        PipelineIds = [repoPipelineId],
                        InvocationKeys = [ "NotTheInvocationKey" ],
                        Overrides = new BuildOverrides()
                        {
                            AdditionalCommandLineArguments = "/foo /bar"
                        }
                    },

                    // A matching rule
                    new BuildOverridesRule() {
                        Repository = repoName,
                        PipelineIds = [repoPipelineId],
                        InvocationKeys = [ invocationKey ],
                        Overrides = new BuildOverrides()
                        {
                            AdditionalCommandLineArguments = "/logObservedFileAccesses+ /logProcesses+"
                        }
                    }
                ]
            };

            var matchingOverride = BuildOverridesHelper.TryGetMatchingOverride(invocationKey, overrideConfig, m_testAdoEnvironment, new MockLogger());

            // The second rule should be the matching rule
            Assert.NotNull(matchingOverride);
            Assert.Equal("/logObservedFileAccesses+ /logProcesses+", matchingOverride.AdditionalCommandLineArguments);
        }
    }
}