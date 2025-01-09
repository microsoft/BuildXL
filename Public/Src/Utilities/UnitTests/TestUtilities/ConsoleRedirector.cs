// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;
using System.Text;

namespace Test.BuildXL.TestUtilities
{
    /// <summary>
    /// Temporarily redirects console output to strings while the 
    /// object is in scope. Console output returns to previous location on dispose.
    /// </summary>
    public class ConsoleRedirector : IDisposable
    {
        private TextWriter m_originalConsoleOut;
        private StringBuilder m_standardOutput = new StringBuilder();
        private string m_outStringTarget;

        /// <summary>
        /// Start redirecting console output.
        /// </summary>
        public ConsoleRedirector(ref string outString)
        {
            m_originalConsoleOut = Console.Out;
            m_outStringTarget = outString;
            Console.SetOut(new StringWriter(m_standardOutput));
        }

        /// <summary>
        /// Return Standard output value emitted.
        /// </summary>
        public string GetOutput()
        {
            return m_standardOutput.ToString();
        }

        /// <summary>
        /// Return console output to previous locations.
        /// </summary>
        public void Dispose()
        {
            m_outStringTarget = m_standardOutput.ToString();
            Console.SetOut(m_originalConsoleOut);
        }
    }
}
