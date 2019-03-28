// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.IO;
using System.Reflection;
using BuildXL.Utilities;
using Xunit.Abstractions;
using Xunit.Sdk;
using AssemblyHelper = BuildXL.Utilities.AssemblyHelper;

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
