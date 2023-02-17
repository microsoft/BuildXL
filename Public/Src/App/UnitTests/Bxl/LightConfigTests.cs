// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.IO;
using BuildXL;
using BuildXL.Utilities.Core;
using BuildXL.Utilities.Configuration;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;
using static Test.BuildXL.TestUtilities.Xunit.XunitBuildXLTest;

namespace Test.BuildXL
{
    public class LightConfigTests
    {
        private readonly string m_specFilePath = A("d", "src", "blahBlah.dsc");
        private readonly string m_serverDirPath = A("d", "src", "serverDir");
        private readonly string m_srcPath = A("d", "src", "src");
        private readonly string m_targetPath = A("d", "src", "target");

        [Fact]
        public void DefaultsMatch()
        {
            // Need to always provide a config file otherwise the arg parser fails
            RunCongruentTest(new[] { @"/c:" + m_specFilePath });
        }

        [Fact]
        public void AllSupportedItemsMatch()
        {
            RunCongruentTest(new[]
            {
                "/c:" + m_specFilePath,
                "/server-",
                "/serverdeploymentdir:" + m_serverDirPath,
                "/nologo",
                "/fancyConsole-",
                "/color-",
                "/substsource:" + m_srcPath,
                "/substtarget:" + m_targetPath,
                "/serverMaxIdleTimeInMinutes:60",
                "/runInSubst:"
            });
        }

        private void RunCongruentTest(string[] args, bool passParse = true)
        {
            ICommandLineConfiguration config;
            PathTable pt = new PathTable();
            bool fullConfigSuccess = Args.TryParseArguments(args, pt, null, out config);

            LightConfig lightConfig;
            bool lightConfigSuccess = LightConfig.TryParse(args, out lightConfig);

            XAssert.AreEqual(passParse, fullConfigSuccess);
            XAssert.AreEqual(passParse, lightConfigSuccess);

            if (passParse)
            {
                AssertCongruent(pt, config, lightConfig);
            }
        }

        private void AssertCongruent(PathTable pathTable, ICommandLineConfiguration commandLineConfig, LightConfig lightConfig)
        {
            XAssert.AreEqual(lightConfig.AnimateTaskbar, commandLineConfig.Logging.AnimateTaskbar);
            XAssert.AreEqual(lightConfig.Color, commandLineConfig.Logging.Color);
            AssertPathCongruent(pathTable, lightConfig.Config, commandLineConfig.Startup.ConfigFile);
            XAssert.AreEqual(lightConfig.FancyConsole, commandLineConfig.Logging.FancyConsole);
            XAssert.AreEqual(lightConfig.Help, commandLineConfig.Help);
            XAssert.AreEqual(lightConfig.NoLogo, commandLineConfig.NoLogo);
            XAssert.AreEqual(lightConfig.Server, commandLineConfig.Server);
            XAssert.AreEqual(lightConfig.DisablePathTranslation, commandLineConfig.Logging.DisableLoggedPathTranslation);
            AssertPathCongruent(pathTable, lightConfig.ServerDeploymentDirectory, commandLineConfig.ServerDeploymentDirectory);
            AssertPathCongruent(pathTable, lightConfig.SubstSource, commandLineConfig.Logging.SubstSource);
            AssertPathCongruent(pathTable, lightConfig.SubstTarget, commandLineConfig.Logging.SubstTarget);
            XAssert.AreEqual(lightConfig.RunInSubst, commandLineConfig.RunInSubst);
        }

        private void AssertPathCongruent(PathTable pathTable, string s, AbsolutePath p)
        {
            if (!p.IsValid)
            {
                XAssert.IsNull(s);
            }
            else
            {
                XAssert.AreEqual(s, p.ToString(pathTable));
            }
        }

        /// <summary>
        /// This isn't part of the standard congruency test above because the full config parser doesn't extract hashtype
        /// as data to the config object. Instead it sets it for the running application. That needs to be refactored
        /// in order to run a proper unit test to make sure the two configurations objects align.
        /// </summary>
        [Fact]
        public void VerifyHashType()
        {
            LightConfig lightConfig;
            XAssert.IsTrue(LightConfig.TryParse(new[] { $"/c:fake", "/hashtype:murmur" }, out lightConfig));
            XAssert.AreEqual("murmur", lightConfig.HashType);
        }
    }
}
