// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Text;

namespace BuildXL.Utilities
{
    /// <summary>
    /// Contains helper methods to get information on an object in Unix
    /// </summary>
    public class UnixObjectFileDumpUtils
    {
        /// <summary>
        /// Indicates whether binutils is installed so that objdump can be used by this class.
        /// </summary>
        public static Lazy<bool> IsObjDumpInstalled = new(() => OperatingSystemHelper.IsLinuxOS && File.Exists(ObjDumpPath));

        /// <summary>
        /// Cache of results from <see cref="IsBinaryStaticallyLinked(string)"/>.
        /// </summary>
        private readonly ConcurrentDictionary<string, bool> m_staticProcessCache = new();

        /// <summary>
        /// Path to objdump utility
        /// </summary>
        private const string ObjDumpPath = "/usr/bin/objdump";

        /// <summary>
        /// The output from the objdump utility that indicates that libc is dynamically linked
        /// </summary>
        private const string ObjDumpLibcOutput = "NEEDED               libc.so.";

        /// <nodoc />
        public static UnixObjectFileDumpUtils CreateObjDump() => IsObjDumpInstalled.Value ? new UnixObjectFileDumpUtils() : null;

        /// <summary>
        /// Returns true if the provided binary statically links libc
        /// </summary>
        /// <param name="binaryPath">Path for executable to be tested.</param>
        /// <returns>True if the binary is statically linked, false if not.</returns>
        public bool IsBinaryStaticallyLinked(string binaryPath)
        {
            if (!OperatingSystemHelper.IsLinuxOS || !IsObjDumpInstalled.Value || !File.Exists(binaryPath))
            {
                return false;
            }

            // It is possible that this path could have the same timestamp if deployed by cache
            // TODO [pgunasekara]: Revisit this if this case is happening in production queues and use a file hash for the key instead
            var pathKey = $"{File.GetLastWriteTime(binaryPath)}|{binaryPath}";

            if (m_staticProcessCache.TryGetValue(pathKey, out var result))
            {
                return result;
            }

            var processInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = ObjDumpPath,
                Arguments = $"-p {binaryPath}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden,
                UseShellExecute = false,
                ErrorDialog = false,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };

            using var process = System.Diagnostics.Process.Start(processInfo);
            if (process == null)
            {
                return false;
            }

            string stdout = string.Empty;

            try
            {
                stdout = process.StandardOutput.ReadToEnd();
#pragma warning disable AsyncFixer02 // WaitForExitAsync should be used instead
                if (!process.WaitForExit(2 * 1000))
                {
                    // Only waiting 2 seconds at the most, although it should never take this long
                    kill(process);
                    m_staticProcessCache.TryAdd(pathKey, false);
                }
                process.WaitForExit();
#pragma warning restore AsyncFixer02
            }
#pragma warning disable ERP022 // An exit point '}' swallows an unobserved exception.
            catch (Exception)
            {
                // Best effort
                m_staticProcessCache.TryAdd(pathKey, false);
            }
#pragma warning restore ERP022

            if (process.ExitCode == 0)
            {
                var isStaticallyLinked = !stdout.Contains(ObjDumpLibcOutput);
                m_staticProcessCache.TryAdd(pathKey, isStaticallyLinked);

                return isStaticallyLinked;
            }

            static void kill(System.Diagnostics.Process p)
            {
                if (p == null || p.HasExited)
                {
                    return;
                }

                try
                {
                    p.Kill();
                }
                catch (InvalidOperationException)
                {
                    // the process may have exited,
                    // in this case ignore the exception
                }
            }

            return false;
        }

        /// <summary>
        /// Gets a list of statically linked processes that were detected previously.
        /// </summary>
        public IEnumerable<string> GetDetectedStaticallyLinkedProcesses() => m_staticProcessCache.Where(p => p.Value).Select(p => p.Key);
    }
}
