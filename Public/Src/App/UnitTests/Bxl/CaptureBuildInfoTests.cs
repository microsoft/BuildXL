// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using BuildXL;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Configuration.Mutable;
using BuildXL.Utilities.Core;
using BuildXL.Utilities.Tracing;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using static Test.BuildXL.TestUtilities.Xunit.XunitBuildXLTest;

namespace Test.BuildXL
{
    public class CaptureBuildInfoTests
    {
        private static readonly string s_specFilePath = A("d", "src", "blahBlah.dsc");

        private const string EnvVarExpectedValue = "TestADO";

        private const string OrgURLNewFormatTestValue = "https://dev.azure.com/bxlTestCheck/check/newformat/URL//";

        private const string OrgURLFormatTestValue = "https://bxlTestCheck.visualstudio.com";

        private const string GitRemoteRepoUrlTestValue1 = "https://mseng.visualstudio.com/mseng/Domino/_git/BuildXL.Internal";

        private const string GitRemoteRepoUrlTestValue2 = "https://dev.azure.com/mseng/Domino/_git/BuildXL.Internal";

        /// <summary>
        /// This test is to check if the "infra" property is set to "ado" when "Build_DefinitionName" is present as an environment variable.
        /// </summary>
        [Fact]
        public static void TestInfraPropertyADO()
        {
            string[] envString = ComputeEnvBlockForTesting(null, CaptureBuildInfo.AdoEnvVariableForInfra, EnvVarExpectedValue);
            XAssert.IsTrue(AssertEnvStringContainsTelemetryEnvProperty("infra=ado", envString));
        }

        /// <summary>
        /// This test is to check if the "infra" property has been to set "cloudbuild" if the "InCloudBuild" cmd line argument is passed.
        /// </summary>
        [Fact]
        public void TestInfraPropertyCloudBuild()
        {
            ICommandLineConfiguration configuration = AddTraceInfoOrEnvironmentArguments("/inCloudBuild");
            string env1 = BuildXLApp.ComputeEnvironment(configuration);
            string[] envString = env1.Split(';');
            AssertNoDuplicates(envString);
            XAssert.IsTrue(AssertEnvStringContainsTelemetryEnvProperty("infra=cb", envString));
        }

        /// <summary>
        /// This test is to check the scenario when the user passes the "InCloudBuild" cmd line argument and the presence of the environment variable "Build_DefintionName".
        /// In this case the Infra property is set to cloudbuild env
        /// </summary>
        [Fact]
        public void TestInfraPropertyForBothADOCB()
        {
            string[] envString = ComputeEnvBlockForTesting("/inCloudBuild", CaptureBuildInfo.AdoEnvVariableForInfra, EnvVarExpectedValue);
            XAssert.IsTrue(AssertEnvStringContainsTelemetryEnvProperty("infra=cb", envString));
            XAssert.IsFalse(AssertEnvStringContainsTelemetryEnvProperty("infra=ado", envString));
        }

        /// <summary>
        /// This test is to ensure that the user passed build property value overrides the value being set by GetInfra().
        /// </summary>
        [Fact]
        public void TestInfraPropertyForDuplicates()
        {
            string traceInfoArgs = "/traceInfo:INFRA=test";
            string[] envString = ComputeEnvBlockForTesting(traceInfoArgs, CaptureBuildInfo.AdoEnvVariableForInfra, EnvVarExpectedValue);
            XAssert.IsTrue(AssertEnvStringContainsTelemetryEnvProperty("INFRA=test", envString));
            XAssert.IsFalse(AssertEnvStringContainsTelemetryEnvProperty("INFRA=ado", envString));
        }

        /// <summary>
        /// This test to check ensure there are no duplicates of different case sensitivity in the traceInfoFlags values
        /// </summary>
        [Fact]
        public void TestInfraPropertyForCaseSensitivity()
        {
            PathTable pt = new PathTable();
            var argsParser = new Args();
            ICommandLineConfiguration configuration = new CommandLineConfiguration();
            string args = "/traceInfo:Infra=test";
            string args1 = "/traceInfo:inFra=test2";
            argsParser.TryParse(new[] { @"/c:" + s_specFilePath, args, args1 }, pt, out configuration);
            string env1 = BuildXLApp.ComputeEnvironment(configuration);
            string[] envString = env1.Split(';');
            XAssert.IsFalse(AssertEnvStringContainsTelemetryEnvProperty("Infra=test", envString));
            XAssert.IsTrue(AssertEnvStringContainsTelemetryEnvProperty("Infra=test2", envString));
        }

        /// <summary>
        /// This test is to check if the "org" property is set to the organization name extracted from the URL for both ADO and GitRemoteRepoUrl.
        /// </summary>
        [Theory]
        [InlineData(CaptureBuildInfo.EnvVariableForOrg, OrgURLNewFormatTestValue, "bxlTestCheck", GitRemoteRepoUrlTestValue1)] // Case 1: The "org" property is set to ADO predefined var value in ADO if it is available, the URL is in the new format and if GitRemoteRepoUrl is available.
        [InlineData(CaptureBuildInfo.EnvVariableForOrg, OrgURLFormatTestValue, "bxlTestCheck", GitRemoteRepoUrlTestValue1)] //Case 2: The "org" property is set to ADO predefined var value in ADO if it is available, the URL is in the old format and if GitRemoteRepoUrl is available.
        [InlineData(CaptureBuildInfo.EnvVariableForOrg, null, "mseng", GitRemoteRepoUrlTestValue1)] // Case 3: The "org" value is set to the GitRemoteRepoUrl old format, if it is present, when the ADO predefined variable is unavailable.
        [InlineData(CaptureBuildInfo.EnvVariableForOrg, null, "mseng", GitRemoteRepoUrlTestValue2)] // Case 4: The "org" value is set to the GitRemoteRepoUrl new format, if it is present, when the ADO predefined variable is unavailable.        
        [InlineData(CaptureBuildInfo.EnvVariableForOrg, "https://dev.azure.com123/bxlTestCheck/check/newformat/URL//", "mseng", GitRemoteRepoUrlTestValue2)] // Case 5: The "org" value is set to the GitRemoteRepoUrl format, if it is present, when the "org" value extracted from the ADO predefined variable is invalid.        
        [InlineData(CaptureBuildInfo.EnvVariableForOrg, "https://dev.azure.com123/bxlTestCheck/check/newformat/URL//", null, null)] // Case 6: The "org" value is unset, when the "org" value extracted from the ADO predefined variable is invalid and GitRemoteRepoUrl is absent.        
        [InlineData("NotAnEnvVariable", null, null, null)] // Case 7: The "org" value remains unset if both environment variable and GitRemoteRepoUrl are absent.
        public static void TestOrgProperty(string adoPreDefinedEnvVar, string adoPreDefinedEnvVarTestValue, string expectedValueInEnvString, string gitRemoteRepoUrl)
        {
            string[] envString = ComputeEnvBlockForTesting(null, adoPreDefinedEnvVar, adoPreDefinedEnvVarTestValue, gitRemoteRepoUrl);
            if (expectedValueInEnvString != null)
            {
                XAssert.IsTrue(AssertEnvStringContainsTelemetryEnvProperty("org=" + expectedValueInEnvString, envString));
            }
            else
            {
                XAssert.IsFalse(AssertEnvStringContainsTelemetryEnvProperty("org=", envString));
            }
        }

        /// <summary>
        /// This test is to ensure that the user passed build property value overrides the value being set by GetOrg().
        /// </summary>
        [Fact]
        public void TestOrgPropertyForTraceInfoValue()
        {
            string traceInfoArgs = "/traceInfo:org=test";
            string[] envString = ComputeEnvBlockForTesting(traceInfoArgs, CaptureBuildInfo.EnvVariableForOrg, OrgURLNewFormatTestValue, GitRemoteRepoUrlTestValue1);
            XAssert.IsTrue(AssertEnvStringContainsTelemetryEnvProperty("org=test", envString));
            XAssert.IsFalse(AssertEnvStringContainsTelemetryEnvProperty("org=bxlTestCheck", envString));
            XAssert.IsFalse(AssertEnvStringContainsTelemetryEnvProperty("org=BuildXL.Internal", envString));
        }

        /// <summary>
        /// This test is to check if the "codebase" property has been to set the branch name passed via traceInfo in the CB environment.
        /// This test also tests the scenario when the codebase property has been passed via traceInfo in the "CloudBuild" environment,
        /// the presence of the environment variable "BUILD_REPOSITORY_NAME" and GitRemoteRepoUrl.
        /// In this case the codebase property value obtained from the traceInfo argument should be set in the envString for codebase.
        /// </summary>
        [Fact]
        public void TestCodebasePropertyCloudBuild()
        {
            string traceInfoArgs = "/traceInfo:codebase=TestCB";
            string[] envString = ComputeEnvBlockForTesting(traceInfoArgs, CaptureBuildInfo.AdoPreDefinedVariableForCodebase, EnvVarExpectedValue, GitRemoteRepoUrlTestValue1);
            XAssert.IsTrue(AssertEnvStringContainsTelemetryEnvProperty("codebase=TestCB", envString));
            XAssert.IsFalse(AssertEnvStringContainsTelemetryEnvProperty("codebase=TestADO", envString));
            XAssert.IsFalse(AssertEnvStringContainsTelemetryEnvProperty("codebase=BuildXL.Internal", envString));
        }

        /// <summary>
        /// This test validates the following cases:
        /// Case 1: The "codebase" property is set to the repository name when "BUILD_REPOSITORY_NAME" is present as an environment variable in ADO.
        /// Case 2: When the codebase value cannot be obtained from the env var.
        ///         The test checks that the codebase value is correctly extracted from the GitRemoteRepoUrl if it is present.
        /// Case 3: If the environment variable is not present and the GitRemoteRepoUrl is not valid (e.g., ends with a slash or is null/empty), the codebase value remains unset.
        /// Case 4: If both the environment variable and GitRemoteRepoUrl are not present, the codebase value remains unset.
        [Theory]
        [InlineData(GitRemoteRepoUrlTestValue1, EnvVarExpectedValue, EnvVarExpectedValue)]
        [InlineData(GitRemoteRepoUrlTestValue1, null, "BuildXL.Internal")]
        [InlineData("https://mseng.visualstudio.com/DefaultCollection/Domino/_git/BuildXL.Internal//", null, null)]
        [InlineData(null, null, null)]
        [InlineData("https://DEV.AZURE.COM/mseng/Domino/_git/BuildXL.Internal", null, "BuildXL.Internal")]
        public void ValidateExtractCodebasePropertyFromGitRemoteRepoUrl(string gitRemoteRepoUrl, string adoPredefinedValueForCodebase, string expectedRepoName)
        {
            string[] envString = ComputeEnvBlockForTesting(null, CaptureBuildInfo.AdoPreDefinedVariableForCodebase, adoPredefinedValueForCodebase, gitRemoteRepoUrl);
            if (expectedRepoName != null)
            {
                XAssert.IsTrue(AssertEnvStringContainsTelemetryEnvProperty("codebase=" + expectedRepoName, envString));
            }
            else
            {
                XAssert.IsFalse(AssertEnvStringContainsTelemetryEnvProperty("codebase=", envString));
            }
        }

        /// <summary>
        /// This test is to check if the "pipelineID" property is set to the pipeline id in an ADO env when "SYSTEM_DEFINITIONID" is present as an environment variable.
        /// </summary>
        [Fact]
        public static void TestPipelineIdPropertyADO()
        {
            string[] envString = ComputeEnvBlockForTesting(null, CaptureBuildInfo.AdoPreDefinedVariableForPipelineId, EnvVarExpectedValue);
            XAssert.IsTrue(AssertEnvStringContainsTelemetryEnvProperty("pipelineid=TestADO", envString));
        }


        /// <summary>
        /// This test is to check if the "adobuildid" property is set to the pipeline id in an ADO env when "BUILD_BUILID" is present as an environment variable.
        /// </summary>
        [Fact]
        public static void TestBuildIdPropertyADO()
        {
            string[] envString = ComputeEnvBlockForTesting(null, CaptureBuildInfo.AdoPreDefinedVariableForBuildId, EnvVarExpectedValue);
            XAssert.IsTrue(AssertEnvStringContainsTelemetryEnvProperty($"{CaptureBuildProperties.AdoBuildIdKey}={EnvVarExpectedValue}", envString));
        }

        /// <summary>
        /// This test is to check if the "pipelineid" property has been set to the pipeline id passed via traceInfo.
        /// This test also tests the scenario when the pipelineid property has been passed via traceInfo and the presence of the environment variable "SYSTEM_DEFINITIONID".
        /// In this case the pipelineid property value obtained from the traceInfo argument should be set in the envString for pipelineid.
        /// </summary>
        [Fact]
        public void TestPipelineIdTraceInfoPropertyADO()
        {
            string traceInfoArgs = "/traceInfo:pipelineid=TestADOTraceInfo";
            string[] envString = ComputeEnvBlockForTesting(traceInfoArgs, CaptureBuildInfo.AdoPreDefinedVariableForCodebase, EnvVarExpectedValue);
            XAssert.IsTrue(AssertEnvStringContainsTelemetryEnvProperty("pipelineid=TestADOTraceInfo", envString));
            XAssert.IsFalse(AssertEnvStringContainsTelemetryEnvProperty("pipelineid=TestADO", envString));
        }

        /// <summary>
        /// This test is to check if the "cloudBuildQueue" property has been to set the build queue passed via traceInfo in the CB environment.
        /// This test also tests the scenario when the cloudBuildQueue property has been passed via traceInfo in the "CloudBuild" environment and the presence of the environment variable "SYSTEM_DEFINITIONID".
        /// In this case the env string should contain both the cloudBuildQueue and the pipelineid property.
        /// </summary>
        [Fact]
        public void TestBuildQueuePropertyCloudBuild()
        {
            string traceInfoArgs = "/traceInfo:cloudBuildQueue=TestCB";
            string[] envString = ComputeEnvBlockForTesting(traceInfoArgs, CaptureBuildInfo.AdoPreDefinedVariableForPipelineId, EnvVarExpectedValue);
            XAssert.IsTrue(AssertEnvStringContainsTelemetryEnvProperty("cloudBuildQueue=TestCB", envString));
            XAssert.IsTrue(AssertEnvStringContainsTelemetryEnvProperty("pipelineid=TestADO", envString));
        }

        /// <summary>
        /// This test is to ensure that the traceInfo value for the stageid property is set accordingly in the Env string
        /// </summary>
        [Fact]
        public void TestStageIdPropertyForTraceInfoValue()
        {
            string traceInfoArgs = "/traceInfo:stageid=TestCB";
            ICommandLineConfiguration configuration = AddTraceInfoOrEnvironmentArguments(traceInfoArgs);
            string env1 = BuildXLApp.ComputeEnvironment(configuration);
            string[] envString = env1.Split(';');
            AssertNoDuplicates(envString);
            XAssert.IsTrue(AssertEnvStringContainsTelemetryEnvProperty("stageid=TestCB", envString));
            XAssert.IsFalse(AssertEnvStringContainsTelemetryEnvProperty("stageid=TestADO", envString));
        }

        /// <summary>
        /// This test is to ensure that the stageid property is set accordingly for Office builds in the Env string
        /// </summary>
        [Theory]
        [InlineData("/environment:OfficeEnlistmentBuildLab", "enlist")]
        [InlineData("/environment:OfficeMetaBuildLab", "meta")]
        [InlineData("/environment:OfficeProductBuildLab", "product")]
        [InlineData("/environment:OfficeProductBuildDev", "product")]
        [InlineData("/environment:OfficeMetaBuildDev", "meta")]
        [InlineData("/environment:OfficeEnlistmentBuildDev", "enlist")]
        [InlineData("/environment:Unset", null)]
        [InlineData("/environment:OsgLab", null)]
        [InlineData("/environment:SelfHostPrivateBuild", null)]
        [InlineData("/environment:SelfHostLKG", null)]
        public void TestStageIdPropertyForOfficeBuilds(string environmentValue, string expectedValue)
        {
            string cmdArgs = environmentValue;
            ICommandLineConfiguration configuration = AddTraceInfoOrEnvironmentArguments(cmdArgs);
            string env1 = BuildXLApp.ComputeEnvironment(configuration);
            string[] envString = env1.Split(';');
            AssertNoDuplicates(envString);
            if (expectedValue != null)
            {
                XAssert.IsTrue(AssertEnvStringContainsTelemetryEnvProperty("stageid=" + expectedValue, envString));
            }
            else
            {
                XAssert.IsFalse(AssertEnvStringContainsTelemetryEnvProperty("stageId=", envString));
            }

        }

        /// <summary>
        /// This test is used to check the scenario when it is a Office build and the user sends a specific value for the property using traceInfo flag.
        /// In this case the user defined value should override the stageid property value set for Officebuilds.
        /// </summary>
        [Fact]
        public void TestStageIdPropertyForTraceInfoPrecedence()
        {
            string traceInfoArgs = "/traceInfo:stageid=TestCB";
            string environmentValue = "/environment:OfficeEnlistmentBuildLab";
            PathTable pt = new PathTable();
            var argsParser = new Args();
            ICommandLineConfiguration configuration = new CommandLineConfiguration();
            argsParser.TryParse(new[] { @"/c:" + s_specFilePath, traceInfoArgs, environmentValue }, pt, out configuration);
            string env1 = BuildXLApp.ComputeEnvironment(configuration);
            string[] envString = env1.Split(';');
            AssertNoDuplicates(envString);
            XAssert.IsTrue(AssertEnvStringContainsTelemetryEnvProperty("stageid=TestCB", envString));
            XAssert.IsFalse(AssertEnvStringContainsTelemetryEnvProperty("stageid=Enlist", envString));
        }

        /// <summary>
        /// This is a helper method to avoid memory leaks with respect to the environment variables that are tested
        /// Check if there any duplicates are present in the environment string.
        /// </summary>
        /// <param name="argument">
        /// Optional command line argument (i.e., a single argument or null).
        /// </param>
        /// <param name="envProperty">
        /// The environment property which is used to add the appropriate properties of build.
        /// Ex: The presence of envProperty "Build_DefinitionName" adds a property called "infra=ado" to the envString.
        /// </param>
        public static string[] ComputeEnvBlockForTesting(string argument, string envProperty, string envPropertyTestValue, string gitRemoteRepoUrl = null)
        {
            string envPropertyOriginalValue = Environment.GetEnvironmentVariable(envProperty);
            try
            {
                Environment.SetEnvironmentVariable(envProperty, envPropertyTestValue);
                // We determine infra during the command line args parsing, so the config object should be constructed
                // after we set an env var that we are testing.
                var configuration = AddTraceInfoOrEnvironmentArguments(argument);
                string env = BuildXLApp.ComputeEnvironment(configuration, gitRemoteRepoUrl);
                string[] envString = env.Split(';');
                // Adding this test condition to make sure that there are no duplicates.
                AssertNoDuplicates(envString);
                return envString;
            }
            finally
            {
                Environment.SetEnvironmentVariable(envProperty, envPropertyOriginalValue);
            }
        }

        /// <summary>
        /// Helper method to pass additional arguments.
        /// </summary>
        private static ICommandLineConfiguration AddTraceInfoOrEnvironmentArguments(string traceInfoArgs)
        {
            PathTable pt = new PathTable();
            var argsParser = new Args();
            ICommandLineConfiguration configuration = new CommandLineConfiguration();
            argsParser.TryParse(
                // If there are no args to add, just create an empty config object.
                traceInfoArgs == null 
                    ? new[] { @"/c:" + s_specFilePath }
                    : new[] { @"/c:" + s_specFilePath, traceInfoArgs }, 
                pt, out configuration);
            return configuration;
        }

        /// <summary>
        /// Helper method to check if the envString contains the required buildProperty or not.
        /// </summary>
        /// <param name="envPropertyValue">
        /// Build property to be tested ex:- infra=ado</param>
        /// <param name="envString">environment string array which contains traceinfo and buildproperties </param>
        private static bool AssertEnvStringContainsTelemetryEnvProperty(string envPropertyValue, string[] envString)
        {
            return envString.Contains(envPropertyValue, StringComparer.InvariantCultureIgnoreCase);
        }

        /// <summary>
        /// Helper method to detect duplicates in the environment string
        /// </summary>
        /// <param name="envString">Environment string which contains traceInfo and build properties</param>
        private static void AssertNoDuplicates(string[] envString)
        {
            HashSet<string> envKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string envStringItem in envString)
            {
                string[] envProperties = envStringItem.Split('=');
                envKeys.Add(envProperties[0]);
            }
            XAssert.AreEqual(envKeys.Count(), envString.Length, "Duplicate properties found in the environment string");
        }
    }
}
