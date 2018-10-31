// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Reflection;
using BuildXL.Utilities;

namespace Test.BuildXL.Storage
{
    /// <summary>
    /// Wrapper for the DummyWaiter.exe
    /// </summary>
    public sealed class DummyWaiter : IDisposable
    {
        private readonly string m_executablePath;
        private readonly Process m_process;

        private const string ExecutableName = "Test.BuildXL.Executables.DummyWaiter.exe";

        private DummyWaiter(string exePath, Process process)
        {
            Contract.Requires(process != null);
            Contract.Requires(exePath != null);
            m_process = process;
            m_executablePath = exePath;
        }

        public static DummyWaiter RunAndWait()
        {
            string exePath = GetDummyWaiterExeLocation();
            if (!File.Exists(exePath))
            {
                throw new BuildXLException("Expected to find DummyWaiter.exe at " + exePath);
            }

            var startInfo = new ProcessStartInfo(exePath)
                            {
                                CreateNoWindow = true,
                                RedirectStandardInput = true,
                                UseShellExecute = false
                            };
            Process process = Process.Start(startInfo);
            return new DummyWaiter(exePath, process);
        }

        /// <summary>
        /// Creates and returns the location of a COPY of the exe deployed with <see cref="ExecutableName"/>.
        /// The copy can be mutated or have its file permissions changed, unlike the actual exe which will be a hardlink
        /// into the CAS.
        /// </summary>
        public static string GetDummyWaiterExeLocation()
        {
            string currentCodeFolder = Path.GetDirectoryName(AssemblyHelper.GetAssemblyLocation(typeof(DummyWaiter).GetTypeInfo().Assembly));
            Contract.Assume(currentCodeFolder != null);

            string dummyWaiterExecutableCopy = Path.Combine(Environment.GetEnvironmentVariable("TEMP"), "Copied" + ExecutableName);

            if (!File.Exists(dummyWaiterExecutableCopy))
            {
                string dummyWaiterExeLocation = Path.GetFullPath(Path.Combine(currentCodeFolder, ExecutableName));
                File.Copy(dummyWaiterExeLocation, dummyWaiterExecutableCopy);
            }

            return dummyWaiterExecutableCopy;
        }

        public void Dispose()
        {
            if (m_process.HasExited)
            {
                throw new BuildXLException("DummyWaiter.exe exited before Dispose(); did it fail to start?");
            }

            m_process.StandardInput.Write('!');
            m_process.StandardInput.Dispose();

            if (!m_process.WaitForExit(60 * 1000))
            {
                m_process.Kill();
                throw new BuildXLException("DummyWaiter.exe did not exit when expected.");
            }
        }
    }
}
