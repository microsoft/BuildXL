// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Linq;
using BuildXL;
using BuildXL.Utilities;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Configuration.Mutable;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using static Test.BuildXL.TestUtilities.Xunit.XunitBuildXLTest;


namespace Test.BuildXL.Utilities.Configuration
{
    public class CaptureBuildInfoTests
    {

        private readonly string m_specFilePath = A("d", "src", "blahBlah.dsc");

        /// <summary>
        /// This test is to check if the "infra" property is set to "ado" when "Build_DefinitionName" is present as an environment variable.
        /// </summary>
        [Fact]
        public static void TestInfraPropertyADO()
        {
            ICommandLineConfiguration configuration = new CommandLineConfiguration();
            string[] envString = ComputeEnvBlockForTesting(configuration);
            XAssert.IsTrue(CheckIfEnvStringContainsInfraProperty("infra=ado", envString));
        }
        /// <summary>
        /// This test is to check if the "infra" property has been to set "cloudbuild" if the "InCloudBuild" cmd line argument is passed.
        /// </summary>

        [Fact]
        public void TestInfraPropertyCloudBuild()
        {
            ICommandLineConfiguration configuration = new CommandLineConfiguration(new CommandLineConfiguration() { InCloudBuild = true });
            string env1 = BuildXLApp.ComputeEnvironment(configuration);
            string[] envString = env1.Split(';');
            CheckForDuplicates(envString);
            XAssert.IsTrue(CheckIfEnvStringContainsInfraProperty("infra=cloudbuild", envString));
        }

        /// <summary>
        /// This test is to check the scenario when the user passes the "InCloudBuild" cmd line argument and the presence of the environment variable "Build_DefintionName".
        /// In this case the Infra property is set to cloudbuild env
        /// </summary>
        [Fact]
        public void TestInfraPropertyForBothADOCB()
        {
            ICommandLineConfiguration configuration = new CommandLineConfiguration(new CommandLineConfiguration() { InCloudBuild = true });
            string[] envString = ComputeEnvBlockForTesting(configuration);
            XAssert.IsTrue(CheckIfEnvStringContainsInfraProperty("infra=cloudbuild", envString));
            XAssert.IsFalse(CheckIfEnvStringContainsInfraProperty("infra=ado", envString));
        }

        /// <summary>
        /// This test is to ensure that the user passed build property value overrides the value being set by GetInfra().
        /// </summary>
                [Fact]
        public void TestInfraPropertyForDuplicates()
        {
            PathTable pt = new PathTable();
            var argsParser = new Args();
            ICommandLineConfiguration configuration = new CommandLineConfiguration();
            string args = "/traceInfo:INFRA=test";
            argsParser.TryParse(new[] { @"/c:" + m_specFilePath, args}, pt, out configuration);
            string[] envString = ComputeEnvBlockForTesting(configuration);            
            XAssert.IsTrue(CheckIfEnvStringContainsInfraProperty("INFRA=test", envString));
            XAssert.IsFalse(CheckIfEnvStringContainsInfraProperty("INFRA=ado", envString));
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
            argsParser.TryParse(new[] { @"/c:" + m_specFilePath, args, args1 }, pt, out configuration);
            string env1 = BuildXLApp.ComputeEnvironment(configuration);
            string[] envString = env1.Split(';');
            XAssert.IsFalse(CheckIfEnvStringContainsInfraProperty("Infra=test", envString));
            XAssert.IsTrue(CheckIfEnvStringContainsInfraProperty("Infra=test2", envString));

        }

        /// <summary>
        /// This is a helper method to set a default environment variable "Build_DefintionName"
        /// Check if there any duplicates in the environment string.
        /// Reset the Build_DefinitionName variable to its original value
        /// </summary>
        /// <param name="configuration">
        /// CommandLine configuration object
        /// </param>
        /// <param name="buildProperty">
        /// The buildProperty value set
        /// </param>
        /// <returns></returns>

        public static string[] ComputeEnvBlockForTesting(ICommandLineConfiguration configuration)
        {
            string buildDefOriginalValue = Environment.GetEnvironmentVariable("BUILD_DEFINTIONNAME");
            try
            {
                Environment.SetEnvironmentVariable("BUILD_DEFINITIONNAME", "TestADO");
                string env = BuildXLApp.ComputeEnvironment(configuration);
                string[] envString = env.Split(';');
                // Adding this test condition to make sure that there are no duplicates.
                CheckForDuplicates(envString);
                return envString;                
            }
            finally
            {
                Environment.SetEnvironmentVariable("BUILD_DEFINITIONNAME", buildDefOriginalValue);
            }
        }

        /// <summary>
        /// Helper method to check if the envString contains the required buildProperty or not.
        /// </summary>
        /// <param name="buildPropertyValue">
        /// Build property to be tested ex:- infra=ado</param>
        /// <param name="envString">
        /// environment string array which contains traceinfo and buildproperties</param>
        /// <returns></returns>
        private static bool CheckIfEnvStringContainsInfraProperty(string buildPropertyValue, string[] envString)
        {       
            return envString.Contains(buildPropertyValue, StringComparer.InvariantCultureIgnoreCase);
        }

        private static void CheckForDuplicates(string[] envString)
        {
            XAssert.IsTrue(envString.Count(x => x.StartsWith("infra=", StringComparison.InvariantCultureIgnoreCase)) <= 1);

        }
    }
}
