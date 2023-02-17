// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;
using Xunit.Abstractions;

namespace Test.BuildXL.FrontEnd.Script.Testing.TestGenerator
{
    /// <summary>
    /// Shared helpers for test generator tests
    /// </summary>
    public class BaseTest
    {
        private readonly ITestOutputHelper m_output;

        /// <nodoc />
        public BaseTest(ITestOutputHelper output)
        {
            m_output = output;
            Logger = new TestLogger(output);
        }

        /// <summary>
        /// TestLogger to use
        /// </summary>
        protected TestLogger Logger { get; }

        /// <summary>
        /// Gets a unique test folder for the test.
        /// </summary>
        protected string GetAndCleanTestFolder(string testClass, string testName)
        {
            var testRoot = Path.Combine(Environment.GetEnvironmentVariable("TEMP"), "TestGen");

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
