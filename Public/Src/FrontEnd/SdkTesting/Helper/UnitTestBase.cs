// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.IO;
using Xunit;

namespace BuildXL.FrontEnd.Script.Testing.Helper
{
    /// <summary>
    /// Base class for DScript UnitTests
    /// </summary>
    public abstract class UnitTestBase
    {
        private readonly ITestOutputHelper m_output;

        /// <nodoc />
        protected UnitTestBase(ITestOutputHelper output)
        {
            m_output = output;
        }

        /// <summary>
        /// The DScript file this test should run on
        /// </summary>
        protected abstract string FileUnderTest { get; }

        /// <summary>
        /// The Sdk folders this test covers
        /// </summary>
        [SuppressMessage("Microsoft.Performance", "CA1819:PropertiesShouldNotReturnArrays")]
        protected abstract string[] SdkFoldersUnderTest { get; }

        /// <summary>
        /// Runs a spec test
        /// </summary>
        protected void RunSpecTest(string identifier, string shortName, string lkgFile = null)
        {
            Contract.Requires(!string.IsNullOrEmpty(identifier));

            var testFolder = GetAndCleanTestFolder(shortName);

            var diagnosticHandler = new XUnitDiagnosticHandler(m_output);
            var updateFailedTests = Environment.GetEnvironmentVariable("AutoFixLkgs") == "1";
            var testRunner = new TestRunner(diagnosticHandler.HandleDiagnostic, updateFailedTests);

            var result = testRunner.Run(testFolder, FileUnderTest, identifier, shortName, lkgFile, SdkFoldersUnderTest);
            Assert.True(result, diagnosticHandler.AllErrors);
        }

        /// <summary>
        /// Gets a unique test folder for the test.
        /// </summary>
        protected string GetAndCleanTestFolder(string testMethodName)
        {
            var testRoot = Environment.GetEnvironmentVariable("TestOutputDir") ??
                           Environment.GetEnvironmentVariable("TEMP") ??
                           Path.Combine(Path.GetDirectoryName(BuildXL.Utilities.Core.AssemblyHelper.GetAssemblyLocation(GetType().Assembly)), "TestGen");

            var testClass = GetType().FullName;
            var testFolder = Path.Combine(testRoot, testClass, testMethodName);

            if (Directory.Exists(testFolder))
            {
                Directory.Delete(testFolder, true);
            }

            Directory.CreateDirectory(testFolder);
            return testFolder;
        }
    }
}
