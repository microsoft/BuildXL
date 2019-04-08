// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Reflection;
using BuildXL.Utilities;
using Xunit;
using Xunit.Abstractions;
using Xunit.Sdk;

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

            var testFolder = GetAndCleanTestFolder();

            var diagnosticHandler = new XUnitDiagnosticHandler(m_output);
            var updateFailedTests = Environment.GetEnvironmentVariable("AutoFixLkgs") == "1";
            var testRunner = new TestRunner(diagnosticHandler.HandleDiagnostic, updateFailedTests);

            var result = testRunner.Run(testFolder, FileUnderTest, identifier, shortName, lkgFile, SdkFoldersUnderTest);
            Assert.True(result, diagnosticHandler.AllErrors);
        }

        /// <summary>
        /// Gets a unique test folder for the test.
        /// </summary>
        protected string GetAndCleanTestFolder()
        {
            var testRoot = Environment.GetEnvironmentVariable("TestOutputDir") ??
                           Environment.GetEnvironmentVariable("TEMP") ??
                           Path.Combine(Path.GetDirectoryName(BuildXL.Utilities.AssemblyHelper.GetAssemblyLocation(GetType().Assembly)), "TestGen");

            var testOutputHelper = (TestOutputHelper)m_output;
            var iTestObject = testOutputHelper
                .GetType()
                .GetField("test", BindingFlags.NonPublic | BindingFlags.Instance)
                .GetValue(testOutputHelper);
            var test = (ITest)iTestObject;

            var testClass = test.TestCase.TestMethod.TestClass.Class.Name;
            var testName = test.TestCase.TestMethod.Method.Name;

            var testFolder = Path.Combine(testRoot, testClass, testName);

            if (Directory.Exists(testFolder))
            {
                Directory.Delete(testFolder, true);
            }

            Directory.CreateDirectory(testFolder);
            return testFolder;
        }
    }
}
