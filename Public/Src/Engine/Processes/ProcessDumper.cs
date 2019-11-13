// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.ContractsLight;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Management;
using System.Net;
using System.Runtime.InteropServices;
using BuildXL.Native.IO;
using BuildXL.Native.Processes.Windows;
using BuildXL.Utilities;

namespace BuildXL.Processes
{
    /// <summary>
    /// Dumps processes
    /// </summary>
    public static class ProcessDumper
    {
        /// <summary>
        /// Protects calling <see cref="ProcessUtilitiesWin.MiniDumpWriteDump(IntPtr, uint, SafeHandle, uint, IntPtr, IntPtr, IntPtr)"/>, since all Windows DbgHelp functions are single threaded.
        /// </summary>
        private static readonly object s_dumpProcessLock = new object();

        private static readonly HashSet<string> s_skipProcesses = new HashSet<string>() {
            "conhost", // Conhost dump causes native error 0x8007012b (Only part of a ReadProcessMemory or WriteProcessMemory request was completed) - Build 1809
        };

        /// <summary>
        /// Attempts to create a process memory dump at the requested location. Any file already existing at that location will be overwritten
        /// </summary>
        public static bool TryDumpProcess(Process process, string dumpPath, out Exception dumpCreationException, bool compress = false)
        {
            if (OperatingSystemHelper.IsUnixOS)
            {
                dumpCreationException = new PlatformNotSupportedException();
                return false;
            }

            string processName = "Exited";
            try
            {
                processName = process.ProcessName;
                bool dumpResult = TryDumpProcess(process.Handle, process.Id, dumpPath, out dumpCreationException, compress);
                if (!dumpResult)
                {
                    Contract.Assume(dumpCreationException != null, "Exception was null on failure.");
                }

                return dumpResult;
            }
            catch (Win32Exception ex)
            {
                dumpCreationException = new BuildXLException("Failed to get process handle to create a process dump for: " + processName, ex);
                return false;
            }
            catch (InvalidOperationException ex)
            {
                dumpCreationException = new BuildXLException("Failed to get process handle to create a process dump for: " + processName, ex);
                return false;
            }
            catch (NotSupportedException ex)
            {
                dumpCreationException = new BuildXLException("Failed to get process handle to create a process dump for: " + processName, ex);
                return false;
            }
        }

        /// <summary>
        /// Attempts to create a process memory dump at the requested location. Any file already existing at that location will be overwritten
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA2202:DoNotDisposeObjectsMultipleTimes")]
        public static bool TryDumpProcess(IntPtr processHandle, int processId, string dumpPath, out Exception dumpCreationException, bool compress = false)
        {
            if (OperatingSystemHelper.IsUnixOS)
            {
                dumpCreationException = new PlatformNotSupportedException();
                return false;
            }

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(dumpPath));

                FileUtilities.DeleteFile(dumpPath);
                var uncompressedDumpPath = dumpPath;

                if (compress)
                {
                    uncompressedDumpPath = dumpPath + ".dmp.tmp";
                    Analysis.IgnoreResult(FileUtilities.TryDeleteFile(uncompressedDumpPath));
                }

                using (FileStream fs = new FileStream(uncompressedDumpPath, FileMode.Create))
                {
                    lock (s_dumpProcessLock)
                    {
                        bool dumpSuccess = ProcessUtilitiesWin.MiniDumpWriteDump(
                            hProcess: processHandle,
                            processId: (uint)processId,
                            hFile: fs.SafeFileHandle,
                            dumpType: (uint)ProcessUtilitiesWin.MINIDUMP_TYPE.MiniDumpWithFullMemory,
                            expParam: IntPtr.Zero,
                            userStreamParam: IntPtr.Zero,
                            callbackParam: IntPtr.Zero);

                        if (!dumpSuccess)
                        {
                            var code = Marshal.GetLastWin32Error();
                            var message = new Win32Exception(code).Message;

                            throw new BuildXLException($"Failed to create process dump. Native error: ({code:x}) {message}, dump-path={dumpPath}");
                        }
                    }
                }

                if (compress)
                {
                    using (FileStream compressedDumpStream = new FileStream(dumpPath, FileMode.Create))
                    using (var archive = new ZipArchive(compressedDumpStream, ZipArchiveMode.Create))
                    {
                        var entry = archive.CreateEntry(Path.GetFileNameWithoutExtension(dumpPath) + ".dmp", CompressionLevel.Fastest);

                        using (FileStream uncompressedDumpStream = File.Open(uncompressedDumpPath, FileMode.Open))
                        using (var entryStream = entry.Open())
                        {
                            uncompressedDumpStream.CopyTo(entryStream);
                        }
                    }

                    FileUtilities.DeleteFile(uncompressedDumpPath);
                }

                dumpCreationException = null;
                return true;
            }
            catch (Exception ex)
            {
                dumpCreationException = ex;
                return false;
            }
        }

        /// <summary>
        /// Attempts to dump all processes in a process tree. Any files existing in the dump directory will be deleted.
        /// </summary>
        public static bool TryDumpProcessAndChildren(int parentProcessId, string dumpDirectory, out Exception primaryDumpCreationException, int maxTreeDepth = 10)
        {
            if (OperatingSystemHelper.IsUnixOS)
            {
                primaryDumpCreationException = new PlatformNotSupportedException();
                return false;
            }

            DateTime treeDumpInitiateTime = DateTime.Now;

            try
            {
                FileUtilities.DeleteDirectoryContents(dumpDirectory);
            }
            catch (BuildXLException ex)
            {
                primaryDumpCreationException = ex;
                return false;
            }

            List<KeyValuePair<string, int>> processesToDump;

            try
            {
                processesToDump = GetProcessTreeIds(parentProcessId, maxTreeDepth);
            }
            catch (BuildXLException ex)
            {
                // We couldn't enumerate the child process tree. Fail the entire operation
                primaryDumpCreationException = ex;
                return false;
            }

            // When dumping individual processes, allow any one dump to fail and keep on processing to collect
            // as many dumps as possible
            bool success = true;
            primaryDumpCreationException = null;
            foreach (var process in processesToDump)
            {
                var maybeProcess = TryGetProcesById(process.Value);
                if (!maybeProcess.Succeeded)
                {
                    primaryDumpCreationException = maybeProcess.Failure.CreateException();
                    success = false;
                    continue;
                }

                Process p = maybeProcess.Result;

                // In NetCore the process name cannot be accessed after the process exits. Noop if the process has exited
                string processName = null;
                try
                {
                    processName = p.ProcessName;
                }
                catch (InvalidOperationException ex)
                {
                    if (!p.HasExited)
                    {
                        primaryDumpCreationException = ex;
                        return false;
                    }
                }

                if (processName == null || s_skipProcesses.Contains(processName, StringComparer.OrdinalIgnoreCase) || p.StartTime > treeDumpInitiateTime)
                {
                    // Ignore processes explicitly configured to be skipped or 
                    // that were created after the tree dump was initiated in case of the likely rare
                    // possibility that a pid got immediately reused.
                    // TODO: this would be quite a bit more robust if it checked the job object to get parent rather than
                    // relying on querying for pid and start times.
                    continue;
                }
                
                var dumpPath = Path.Combine(dumpDirectory, process.Key + ".dmp");
                if (!TryDumpProcess(p, dumpPath, out var e))
                {
                    if (e != null) {
                        Contract.Assume(e != null, $"Exception should not be null on failure. Dump-path: {dumpPath}");
                    }
                    primaryDumpCreationException = e;
                    success = false;
                }
            }

            return success;
        }

        /// <summary>
        /// Gets the identifiers and process ids of all active processes in a process tree
        /// </summary>
        /// <remarks>
        /// The identifier is made up of a chain of numbers to encode the process tree hierarchy and the process name.
        /// The hierarchy is a number representing the nth chid process of the parent at each level. For example:
        ///
        /// 1_dog
        /// 1_1_cat
        /// 1_2_elephant
        /// 1_3_fish
        /// 1_2_1_donkey
        /// 1_2_2_mule
        ///
        /// represents a hierarchy like this:
        ///                dog
        ///                 |
        ///      ---------------------
        ///      |          |        |
        ///     cat      elephant   fish
        ///                 |
        ///           -------------
        ///          |            |
        ///        donkey        mule
        /// </remarks>
        /// <param name="parentProcessId">ID of parent process</param>
        /// <param name="maxTreeDepth">Maximum depth of process tree to continue dumping child processes for</param>
        /// <returns>Collection of {identifier, pid}</returns>
        /// <exception cref="BuildXLException">May throw a BuildXLException on failure</exception>
        internal static List<KeyValuePair<string, int>> GetProcessTreeIds(int parentProcessId, int maxTreeDepth)
        {
            return GetChildProcessTreeIds(parentProcessId, maxTreeDepth, isRootQuery: true);
        }

        private static List<KeyValuePair<string, int>> GetChildProcessTreeIds(int idToQuery, int maxTreeDepth, bool isRootQuery)
        {
            List<KeyValuePair<string, int>> result = new List<KeyValuePair<string, int>>();

            // If we don't have access to get the process tree, we'll still at least get the root process. 
            try
            {
                if (isRootQuery || maxTreeDepth > 0)
                {
                    string queryBase = isRootQuery ? "select * from win32_process where ProcessId=" :
                        "select * from win32_process where ParentProcessId=";
                    using (var searcher = new ManagementObjectSearcher(queryBase + idToQuery))
                    {
                        var processes = searcher.Get();

                        int counter = 0;
                        foreach (ManagementObject item in processes)
                        {
                            counter++;
                            int processId = Convert.ToInt32(item["ProcessId"].ToString(), CultureInfo.InvariantCulture);
                            string processName = item["Name"].ToString();

                            // Skip any processes that aren't being run by the current username
                            ManagementBaseObject getOwner = item.InvokeMethod("GetOwner", null, null);
                            object user = getOwner["User"];
                            if (user == null || user.ToString() != Environment.UserName)
                            {
                                continue;
                            }

                            result.Add(new KeyValuePair<string, int>(counter.ToString(CultureInfo.InvariantCulture) + "_" + processName, processId));

                            foreach (var child in GetChildProcessTreeIds(processId, maxTreeDepth - 1, false))
                            {
                                result.Add(new KeyValuePair<string, int>(counter.ToString(CultureInfo.InvariantCulture) + "_" + child.Key, child.Value));
                            }
                        }
                    }
                }

                if (isRootQuery && result.Count == 0)
                {
                    throw new ArgumentException($"Process with an Id of {idToQuery} is inaccessible or not running.");
                }

                return result;
            }
            catch (Exception ex)
            {
                // Catching all exception is OK because there are tons of exceptions that can happen, from
                // creating ManagementOBjectSearcher to enumerating the processes in ManagementOBjectSearcher instance.
                // Moreover, we don't care about those exceptions.
                throw new BuildXLException("Failed to enumerate child processes", ex);
            }
        }

        private static Possible<Process> TryGetProcesById(int processId)
        {
            try
            {
                return Process.GetProcessById(processId);
            }
            catch (InvalidOperationException ex)
            {
                return new Failure<string>("Could not get process by id: " + ex);
            }
            catch (ArgumentException ex)
            {
                return new Failure<string>("Could not get process by id: " + ex);
            }
        }
    }
}
