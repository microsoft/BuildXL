// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Threading.Tasks;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace Test.BuildXL.TestUtilities.XUnit.Extensions
{
    /// <summary>
    /// Custom Xunit test framework that calls <see cref="TestFrameworkExecutor"/>.
    /// </summary>
    [SuppressMessage("Microsoft.Performance", "CA1812:AvoidUninstantiatedInternalClasses", Justification = "Used by Xunit")]
    internal class TestFramework : XunitTestFramework
    {
        private readonly Logger m_logger;

        /// <nodoc />
        public TestFramework(IMessageSink messageSink)
            : base(messageSink)
        {
            m_logger = new Logger();

            TaskScheduler.UnobservedTaskException += (sender, args) => m_logger.LogError("Task unobserved exception: " + args.Exception);

            // Unfortunately, the following line won't be executed, because Xunit console runner calls Environment.Exit in a similar handler.
            // But this handler still make sense because xunit does that only for NET 4.5.1.
            AppDomain.CurrentDomain.UnhandledException += (sender, args) => m_logger.LogError("Domain unhandled error: " + (args.ExceptionObject as Exception)?.ToString());
            
        }

        /// <inheritdoc />
        protected override ITestFrameworkExecutor CreateExecutor(AssemblyName assemblyName)
            => new TestFrameworkExecutor(m_logger, assemblyName, SourceInformationProvider, DiagnosticMessageSink);
    }
}
