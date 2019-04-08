// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using BuildXL;
using BuildXL.Engine;
using BuildXL.Utilities;
using BuildXL.Utilities.Configuration;
using System.Collections.Generic;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;

using static Test.BuildXL.TestUtilities.Xunit.XunitBuildXLTest;

namespace Test.BuildXL
{
    public class ArgsTests
    {
        private readonly string m_specFilePath = A("d", "src", "blahBlah.dsc");

        /// <summary>
        /// Test to ensure that every unsafe option added in Args.cs (see m_handlers)
        /// has a logger function associated with it in Engine.cs (see CreateUnsafeOptionLoggers())
        /// </summary>
        [Fact]
        public void EnabledUnsafeOptionsLogWarnings()
        {
            ICommandLineConfiguration config;
            PathTable pt = new PathTable();
            var argsParser = new Args();

            argsParser.TryParse(new[] { "/c:" + m_specFilePath }, pt, out config);

            // Get a list of all possible options
            var options = argsParser.GetParsedOptionNames();

            // Compile unsafe options while removing duplicates
            var unsafeOptions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var opt in options)
            {
                if (opt.StartsWith("unsafe_", StringComparison.OrdinalIgnoreCase))
                {
                    unsafeOptions.Add(opt);
                }
            }

            // Get all the unsafe options that also associated loggers
            var unsafeOptionLoggers = new HashSet<string>(BuildXLEngine.CreateUnsafeOptionLoggers().Keys, StringComparer.OrdinalIgnoreCase);

            // After all unsafe options are removed, there should be no unsafe options left
            unsafeOptions.ExceptWith(unsafeOptionLoggers);
            XAssert.IsTrue(
                unsafeOptions.Count == 0,
                "The following unsafe options are not logging warnings to users when enabled: "
                + XAssert.SetToString(unsafeOptions)
                + Environment.NewLine
                + "Add an associated logger function in CreateUnsafeOptionLoggers()");
        }

        [Fact]
        public void BoolOption()
        {
            ICommandLineConfiguration config;
            PathTable pt = new PathTable();
            var argsParser = new Args();

            XAssert.IsTrue(argsParser.TryParse(new[] { @"/c:" + m_specFilePath, "/incrementalScheduling-" }, pt, out config));
            XAssert.AreEqual(false, config.Schedule.IncrementalScheduling);

            XAssert.IsTrue(argsParser.TryParse(new[] { @"/c:" + m_specFilePath, "/incrementalScheduling" }, pt, out config));
            XAssert.AreEqual(true, config.Schedule.IncrementalScheduling);

            XAssert.IsTrue(argsParser.TryParse(new[] { @"/c:" + m_specFilePath, "/incrementalScheduling+" }, pt, out config));
            XAssert.AreEqual(true, config.Schedule.IncrementalScheduling);

            // For bool options, passing an unnecessary ":" argument will just enable the option.
            XAssert.IsTrue(argsParser.TryParse(new[] { @"/c:" + m_specFilePath, "/incrementalScheduling:Gibberish" }, pt, out config));
            XAssert.AreEqual(true, config.Schedule.IncrementalScheduling);

            // Anything other than "+", "-", or ":" following an option is invalid.
            XAssert.IsFalse(argsParser.TryParse(new[] { @"/c:" + m_specFilePath, "/incrementalSchedulingGibberish" }, pt, out config));
        }

        [Fact]
        public void BoolEnumOption()
        {
            ICommandLineConfiguration config;
            PathTable pt = new PathTable();
            var argsParser = new Args();

            XAssert.IsTrue(argsParser.TryParse(new[] { @"/c:" + m_specFilePath, "/unsafe_PreserveOutputs-" }, pt, out config));
            XAssert.AreEqual(PreserveOutputsMode.Disabled, config.Sandbox.UnsafeSandboxConfiguration.PreserveOutputs);

            XAssert.IsTrue(argsParser.TryParse(new[] { @"/c:" + m_specFilePath, "/unsafe_PreserveOutputs" }, pt, out config));
            XAssert.AreEqual(PreserveOutputsMode.Enabled, config.Sandbox.UnsafeSandboxConfiguration.PreserveOutputs);

            XAssert.IsTrue(argsParser.TryParse(new[] { @"/c:" + m_specFilePath, "/unsafe_PreserveOutputs+" }, pt, out config));
            XAssert.AreEqual(PreserveOutputsMode.Enabled, config.Sandbox.UnsafeSandboxConfiguration.PreserveOutputs);

            XAssert.IsTrue(argsParser.TryParse(new[] { @"/c:" + m_specFilePath, "/unsafe_PreserveOutputs:Enabled" }, pt, out config));
            XAssert.AreEqual(PreserveOutputsMode.Enabled, config.Sandbox.UnsafeSandboxConfiguration.PreserveOutputs);

            XAssert.IsTrue(argsParser.TryParse(new[] { @"/c:" + m_specFilePath, "/unsafe_PreserveOutputs:Disabled" }, pt, out config));
            XAssert.AreEqual(PreserveOutputsMode.Disabled, config.Sandbox.UnsafeSandboxConfiguration.PreserveOutputs);

            XAssert.IsTrue(argsParser.TryParse(new[] { @"/c:" + m_specFilePath, "/unsafe_PreserveOutputs:Reset" }, pt, out config));
            XAssert.AreEqual(PreserveOutputsMode.Reset, config.Sandbox.UnsafeSandboxConfiguration.PreserveOutputs);

            // Arguments that don't correspond with an enum type fail
            XAssert.IsFalse(argsParser.TryParse(new[] { @"/c:" + m_specFilePath, "/unsafe_PreserveOutputs:Gibberish" }, pt, out config));
        }

        [Fact]
        public void FingerprintStoreModeOption()
        {
            ICommandLineConfiguration config;
            PathTable pt = new PathTable();
            var argsParser = new Args();

            XAssert.IsTrue(argsParser.TryParse(new[] { @"/c:" + m_specFilePath, "/storeFingerprints-" }, pt, out config));
            XAssert.AreEqual(false, config.Logging.StoreFingerprints);

            XAssert.IsTrue(argsParser.TryParse(new[] { @"/c:" + m_specFilePath, "/storeFingerprints+" }, pt, out config));
            XAssert.AreEqual(true, config.Logging.StoreFingerprints);
            XAssert.AreEqual(FingerprintStoreMode.Default, config.Logging.FingerprintStoreMode);

            XAssert.IsTrue(argsParser.TryParse(new[] { @"/c:" + m_specFilePath, "/storeFingerprints:Default" }, pt, out config));
            XAssert.AreEqual(true, config.Logging.StoreFingerprints);
            XAssert.AreEqual(FingerprintStoreMode.Default, config.Logging.FingerprintStoreMode);

            XAssert.IsTrue(argsParser.TryParse(new[] { @"/c:" + m_specFilePath, "/storeFingerprints:IgnoreExistingEntries" }, pt, out config));
            XAssert.AreEqual(true, config.Logging.StoreFingerprints);
            XAssert.AreEqual(FingerprintStoreMode.IgnoreExistingEntries, config.Logging.FingerprintStoreMode);

            // Arguments that don't correspond with an enum type fail
            XAssert.IsFalse(argsParser.TryParse(new[] { @"/c:" + m_specFilePath, "/storeFingerprints:Gibberish" }, pt, out config));
        }

        /// <summary>
        /// Primarily makes sure help text format strings do not have any crashes
        /// </summary>
        [Fact]
        public void DisplayHelpText()
        {
            HelpText.DisplayHelp(global::BuildXL.ToolSupport.HelpLevel.Standard);
            HelpText.DisplayHelp(global::BuildXL.ToolSupport.HelpLevel.Verbose);
        }
    }
}
