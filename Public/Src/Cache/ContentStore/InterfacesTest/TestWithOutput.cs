// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using Xunit.Abstractions;

namespace BuildXL.Cache.ContentStore.InterfacesTest
{
    /// <summary>
    /// Helper class for redirecting console output into xunit output.
    /// </summary>
    public class XUnitTestOutputTextWriter : TextWriter
    {
        private readonly ITestOutputHelper _output;
        private readonly Stopwatch _durationStopwatch;

        public XUnitTestOutputTextWriter(ITestOutputHelper output)
        {
            _output = output;
            _durationStopwatch = Stopwatch.StartNew();
        }

        public override void WriteLine(string data)
        {
            try
            {
                _output.WriteLine($"{_durationStopwatch.Elapsed:g}: {data}");
            }
            catch (InvalidOperationException)
            {
                // In some cases the tracer may be used when the operation is already finished.
                //Debug.Assert(false, data);
            }
        }

        /// <inheritdoc />
        public override Encoding Encoding => Encoding.UTF8;
    }

    /// <summary>
    /// Abstract base class that takes <see cref="ITestOutputHelper"/> and redirects <see cref="System.Console.Out"/> into the helper to make logs part of the test output.
    /// </summary>
    public abstract class TestWithOutput : IDisposable
    {
        private readonly TextWriter _oldConsoleOutput;

        /// <summary>
        /// XUnit output helper for tracing test execution.
        /// </summary>
        protected ITestOutputHelper Output { get; }

        /// <inheritdoc />
        protected TestWithOutput(ITestOutputHelper output)
        {
            Output = output;
            if (output != null)
            {
                _oldConsoleOutput = Console.Out;
                Console.SetOut(new XUnitTestOutputTextWriter(output));
            }
        }

        ~TestWithOutput()
        {
            Debug.Assert(false, $"Instance of type {GetType()} was not disposed!");
        }

        /// <inheritodc />
        public virtual void Dispose()
        {
            if (Output != null)
            {
                // Need to restore an old console output to prevent invalid operation exceptions when tracing is happening after all the tests are finished.
                Console.SetOut(_oldConsoleOutput);
            }

            GC.SuppressFinalize(this);
        }
    }
}
