// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Reflection;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace Test.BuildXL.TestUtilities.XUnit.Extensions
{
    internal class BuildXLTestFrameworkExecutor : XunitTestFrameworkExecutor
    {
        private readonly Logger m_logger;

        public BuildXLTestFrameworkExecutor(
            Logger logger,
            AssemblyName assemblyName,
            ISourceInformationProvider sourceInformationProvider,
            IMessageSink diagnosticMessageSink)
            : base(assemblyName, sourceInformationProvider, diagnosticMessageSink)
        {
            m_logger = logger;
        }

        protected override void RunTestCases(IEnumerable<IXunitTestCase> testCases, IMessageSink executionMessageSink, ITestFrameworkExecutionOptions executionOptions)
        {
            using (var assemblyRunner = new BuildXLAssemblyRunner(m_logger, TestAssembly, testCases, DiagnosticMessageSink, executionMessageSink, executionOptions))
            {
                assemblyRunner.RunAsync().GetAwaiter().GetResult();
            }
        }
    }
}
