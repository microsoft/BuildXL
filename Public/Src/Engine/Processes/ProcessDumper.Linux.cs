// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using BuildXL.Native.IO;
using BuildXL.Utilities;

namespace BuildXL.Processes
{
    /// <summary>
    /// Linux specific helpers to dump a process
    /// </summary>
    public static partial class ProcessDumper
    {
        /// <summary>
        /// Destination folder for core dumps (for most linux distributions)
        /// TODO: we could make this configurable so users are able to pass the location where dumps are created on their particular systems as a command line arg
        /// </summary>
        private static readonly string[] s_crashDumpFolders = new[] { "/var/crash/", "/var/lib/apport/coredump/" };

        /// <summary>
        /// Sends a signal to a processes identified by pid
        /// </summary>
        [DllImport("libc", SetLastError = true, EntryPoint = "kill")]
        private static extern unsafe int SendSignal(int pid, int signal);
        private const int SIG_ABRT = 6;

        private struct RLimit
        {
            /* The current (soft) limit.  */
            public long RLimCurr;

            /* The hard limit.  */
            public long RLimMax;
        };

        [DllImport("libc", SetLastError = true, EntryPoint = "setrlimit")]
        private static extern unsafe int setrlimit(int resource, ref RLimit rlimit);

        [DllImport("libc", SetLastError = true, EntryPoint = "getrlimit")]
        private static extern unsafe int getrlimit(int resource, out RLimit rlimit);

        [DllImport("libc", SetLastError = true, EntryPoint = "prlimit")]
        private static extern unsafe int prlimit(int pid, int resource, ref RLimit newLimit, ref RLimit oldLimit);

        [DllImport("libc", SetLastError = true, EntryPoint = "prlimit")]
        private static extern unsafe int prlimit(int pid, int resource, IntPtr newLimit, ref RLimit oldLimit);

        private const int RLIMIT_CORE = 4;

        /// <summary>
        /// Dumps the given processId into the designated location
        /// </summary>
        private static bool TryDumpLinuxProcess(int processId, string timeoutDumpLocation, out Exception dumpCreateException, Action<string> debugLogger = null)
        {
            Contract.Assert(OperatingSystemHelper.IsLinuxOS);

            // Let's try to make sure the (soft) core dump limit is non-zero. See https://man7.org/linux/man-pages/man5/core.5.html
            // This is on a best effort basis. In some linux configuration core dumps may be generated even if the core size limit is 0.
            // So we record any issues regarding setting the core limit, but we don't fail hard on it
            string error = SetSoftCoreLimit(processId);

            // Let's avoid concurrently generating dumps. Since the name of the core dump is not easy to determine beforehand, by serializing this
            // operation we have better chances at recognizing the proper core dump for a given process.
            // Consider as well we are trying to generate a core dump in a context of a pip timing out, so this is not on the critical path
            try
            {
                lock (s_dumpProcessLock)
                {
                    // The name of the core dump is not well defined across distributions (and it is user configurable as well). In order to increase
                    // robustness, we enumerate the crash dump folder before sending the SIGABRT signal so we can recognize any dumps that got generated
                    // due to aborting the process. This method is not bullet proof (e.g. in theory core dumps unrelated to bxl may be generated here in the same time window), but
                    // it should be good enough.
                    var existingDumps = EnumerateDumps().ToHashSet();

                    debugLogger?.Invoke($"Sending SIGABRT to {processId}");

                    // Send SIGABRT to the process so a core dump is produced
                    var result = SendSignal(processId, SIG_ABRT);

                    if (result != 0)
                    {
                        dumpCreateException = new NativeWin32Exception(Marshal.GetLastWin32Error(), $"Failed to send SIGABRT signal to process {processId}.");
                        return false;
                    }

                    debugLogger?.Invoke($"SIGABRT sent to {processId}");

                    // Give the process time to receive the signal and generate the core dump.
                    // A kill is typically what follows after a core dump, and in some cases the core dump is aborted if a kill is sent right after.
                    Thread.Sleep(500);

                    // Enumerate again in order to find the generated core dump(s)
                    var generatedDumps = EnumerateDumps().Where(coreDump => !existingDumps.Contains(coreDump)).ToList();

                    // Check if any core dumps were generated
                    if (!generatedDumps.Any())
                    {
                        // Give it another go, under high load the system sometimes needs more time for generating the core dump
                        Thread.Sleep(500);
                        generatedDumps = EnumerateDumps().Where(coreDump => !existingDumps.Contains(coreDump)).ToList();
                        
                        if (!generatedDumps.Any())
                        {
                            dumpCreateException = new BuildXLException($"No core dumps were produced. Please make sure core dumps are configured to be generated under any of '{string.Join(",", s_crashDumpFolders)}'. {error}");
                            return false;
                        }
                    }

                    debugLogger?.Invoke($"Found core dumps for {processId}: {string.Join(",", generatedDumps)}");

                    // Copy the produced core dump to the designated folder.
                    debugLogger?.Invoke($"Making sure target dump directory '{timeoutDumpLocation}' exists and it is empty.");
                    FileUtilities.CreateDirectory(Path.GetDirectoryName(timeoutDumpLocation));
                    FileUtilities.DeleteFile(timeoutDumpLocation);

                    debugLogger?.Invoke($"Copying '{generatedDumps[0]}' to {timeoutDumpLocation}.");
                    CopyFileWithRetries(generatedDumps[0], timeoutDumpLocation);

                    // In theory we should only have one generated dump. But in order to be conservative, copy over everything we find
                    for (int i = 1; i < generatedDumps.Count; i++)
                    {
                        debugLogger?.Invoke($"Copying additional core dump '{generatedDumps[i]}' to {timeoutDumpLocation}.{i}");
                        CopyFileWithRetries(generatedDumps[i], $"{timeoutDumpLocation}.{i}");
                    }

                    dumpCreateException = null;
                    
                    debugLogger?.Invoke($"Done capture core dump for {processId}.");

                    return true;
                }
            }
            catch (Exception e) 
            {
                debugLogger?.Invoke($"Exception during core dump capturing:{e.GetLogEventMessage()}");
                dumpCreateException = e;
                return false;
            }
        }

        private static IEnumerable<string> EnumerateDumps() => s_crashDumpFolders.SelectMany(crashFolder => Directory.EnumerateFiles(crashFolder));

        private static void CopyFileWithRetries(string source, string destination)
        {
            int retries = 0;
            while (true)
            {
                try
                {
                    File.Copy(source, destination, true);
                    return;
                }
                catch (UnauthorizedAccessException)
                {
                    // When copying over core dumps, the dump file might be still being written into. This manifests in
                    // an unauthorized exception. Let's retry a couple times to give the dump writing process time to be done with it
                    retries++;

                    if (retries == 3)
                    {
                        throw;
                    }

                    Thread.Sleep(100);
                }
            }
        }

        private static string SetSoftCoreLimit(int processId)
        {
            var error = string.Empty;
            
            // Retrieve the actual core limit
            RLimit rlimit = new RLimit();
            var getRlimitResult = prlimit(processId, RLIMIT_CORE, IntPtr.Zero, ref rlimit);
            if (getRlimitResult != 0)
            {
                error = $"Failed to get core dump limit. Error code: {Marshal.GetLastWin32Error()}. Assuming it is a non-zero value. {Environment.NewLine}";
            }

            // If the soft limit is zero, let's try to change it to a non-zero value, otherwise core files may not be produced. This is a best effort basis since
            // depending on the system configuration we may need a root user to be able to change that
            if (getRlimitResult == 0 && rlimit.RLimCurr == 0)
            {
                // If the maximum hard limit is greater than zero, set the soft limit to that
                long newSoftLimit;
                long newHardLimit;
                if (rlimit.RLimMax > 0)
                {
                    newSoftLimit = rlimit.RLimMax;
                    newHardLimit = rlimit.RLimMax;
                }
                else
                {
                    // Otherwise, we need to change the hard limit to a non-zero value. This can only be done by a root user. So let's try to change both
                    // to unlimited. If we fail, then dumps can't be actually captured
                    newSoftLimit = -1;
                    newHardLimit = -1; 
                }

                RLimit newLimit = new() { RLimCurr = newSoftLimit, RLimMax = newHardLimit };
                if (prlimit(processId, RLIMIT_CORE, ref newLimit, ref rlimit) != 0)
                {
                    var errno = Marshal.GetLastWin32Error();
                    error = $"Failed to set soft core dump limit to 'unlimited'. Error code: {errno}.{Environment.NewLine}";
                }
            }

            return error;
        }

        private static List<KeyValuePair<string, int>> GetChildLinuxProcessTreeIds(int processId, int maxTreeDepth, bool isRootQuery)
        {
            List<KeyValuePair<string, int>> result = new List<KeyValuePair<string, int>>();

            try 
            {
                if (isRootQuery || maxTreeDepth > 0)
                {
                    var childProcesses = isRootQuery? new[] { processId } : Interop.Unix.Process.GetChildProcesses(processId);
                    var counter = 0;

                    // Sending SIGABRT to a parent process may or may not propagate the signal to child processes. That depends on how the child process
                    // was created via fork/clone. This means that by aborting the parent process we may also be aborting the children.
                    // Return process tree with leaves first as a way to maximize the chances of getting all dumps (processes are aborted from first to last
                    // element on this list)
                    foreach (var childProcess in childProcesses)
                    {
                        counter++;
                        result.Insert(0, GetCoreDumpName(counter, childProcess, System.Diagnostics.Process.GetProcessById(childProcess).ProcessName));

                        foreach (var child in GetChildLinuxProcessTreeIds(childProcess, maxTreeDepth - 1, false))
                        {
                            result.Insert(0, GetCoreDumpName(counter, child.Value, child.Key));
                        }
                    }
                }

                if (isRootQuery && result.Count == 0)
                {
                    throw new ArgumentException($"Process with an Id of {processId} is inaccessible or not running.");
                }

                return result;
            }
            catch (Exception ex)
            {
                throw new BuildXLException("Failed to enumerate child processes", ex);
            }
        }
    }
}
