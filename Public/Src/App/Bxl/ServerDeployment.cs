// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using BuildXL.App.Tracing;
using BuildXL.Engine;
using BuildXL.Native.IO;
using BuildXL.Utilities;
using BuildXL.Utilities.Tasks;
using static BuildXL.Utilities.FormattableStringEx;

namespace BuildXL
{
    /// <summary>
    /// Represents a cached deployment used for BuildXL server mode
    /// </summary>
    internal sealed class ServerDeployment
    {
        public readonly string DeploymentPath;

        public readonly ServerDeploymentCacheCreated? CacheCreationInformation;

        // Folder name for the deployment cache, a subfolder of the running client app folder
        private const string ServerDeploymentDirectoryCache = "BuildXLServerDeploymentCache";

        private const string KillBuildXLServerCommandLine = "wmic";
        private const string KillBuildXLServerCommandLineArgs = @"process where ""ExecutablePath like '%{0}%'"" delete";

        private ServerDeployment(string baseDirectory, ServerDeploymentCacheCreated? cacheCreationInformation)
        {
            DeploymentPath = baseDirectory;
            CacheCreationInformation = cacheCreationInformation;
        }

        /// <summary>
        /// If the deployment hash of the client is not the same as the deployment hash of the server cache, creates a new deployment cache. Otherwise, does nothing.
        /// </summary>
        /// <exception cref="IOException">
        /// Throws if the copy fails</exception>
        public static ServerDeployment GetOrCreateServerDeploymentCache(string serverDeploymentRoot, AppDeployment clientApp)
        {
            ServerDeploymentCacheCreated? cacheCreated = null;
            if (IsServerDeploymentOutOfSync(serverDeploymentRoot, clientApp, out var deploymentDir))
            {
                cacheCreated = CreateServerDeployment(deploymentDir, clientApp);
            }

            return new ServerDeployment(deploymentDir, cacheCreated);
        }

        [SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope",
            Justification = "StreamReader/StreamWriter takes ownership for disposal.")]
        private static ServerDeploymentCacheCreated CreateServerDeployment(string destDir, AppDeployment clientApp)
        {
            Stopwatch st = Stopwatch.StartNew();

            // Check if the main server process is in use before attempting to delete the deployment, this way we avoid partially deleting files
            // due to access permission issues. This is not completely bullet proof (there can be a race) but it is highly unlikely the
            // process starts to be in use right after this check
            KillServer(destDir);

            // Deletes the existing cache directory if it exists, so we avoid accumulating garbage.
            if (Directory.Exists(destDir))
            {
                // Remove all files regardless of files being readonly
                FileUtilities.DeleteDirectoryContents(destDir, true);
            }

            // Perform the deployment
            AppDeployment serverDeployment = AppDeployment.ReadDeploymentManifest(clientApp.BaseDirectory, AppDeployment.ServerDeploymentManifestFileName);
            HashSet<string> directories = new HashSet<string>();
            List<KeyValuePair<string, string>> filesToCopy = new List<KeyValuePair<string, string>>();

            foreach (string path in serverDeployment.GetRelevantRelativePaths(forServerDeployment: true).Concat(new string[] { AppDeployment.ServerDeploymentManifestFileName }))
            {
                string targetPath = Path.Combine(destDir, path);
                string sourcePath = Path.Combine(clientApp.BaseDirectory, path);
                string directory = Path.GetDirectoryName(targetPath);
                if (directories.Add(directory))
                {
                    FileUtilities.CreateDirectory(directory);
                }

                filesToCopy.Add(new KeyValuePair<string, string>(sourcePath, targetPath));
            }

            // Because some deployments use virtualized vpak, using a very parallelized copy is beneficial
            Parallel.ForEach(
                filesToCopy,
                new ParallelOptions()
                {
                    MaxDegreeOfParallelism = 50,
                },
                (fileToCopy) =>
                {
                    if (File.Exists(fileToCopy.Key))
                    {
                        File.Copy(fileToCopy.Key, fileToCopy.Value);
                    }
                });

#if NET_FRAMEWORK
            var ngenExe = Path.Combine(System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory(), @"ngen.exe");
            var destExe = Path.Combine(destDir, System.AppDomain.CurrentDomain.FriendlyName);

            // queue:1 means it runs in the background
            if (File.Exists(ngenExe))
            {
                var ngenArgs = "install " + destExe + " /queue:1";
                ProcessStartInfo startInfo = new ProcessStartInfo(ngenExe, ngenArgs);
                startInfo.UseShellExecute = false;
                startInfo.CreateNoWindow = true;
                Process.Start(startInfo);
            }
#endif

            ServerDeploymentCacheCreated cacheCreated = default(ServerDeploymentCacheCreated);
            cacheCreated.TimeToCreateServerCacheMilliseconds = st.ElapsedMilliseconds;

            return cacheCreated;
        }

        public static string ComputeDeploymentDir(string serverDeploymentRoot)
        {
            // Note that this always creates a subdirectory even if there is an externally configured serverDeplymentRoot.
            // This is for protection in case the config provided path is a directory that already contains other files since
            // the server deployment will delete any files already existing in that directory.
            return Path.Combine(
                !string.IsNullOrWhiteSpace(serverDeploymentRoot) ? serverDeploymentRoot : Path.GetFullPath(AppDomain.CurrentDomain.BaseDirectory),
                ServerDeploymentDirectoryCache);
        }

        /// <summary>
        /// Kills the server mode BuildXL associated with this build instance
        /// </summary>
        internal static void KillServer(string serverDeploymentRoot)
        {
            // Check if the main root process (likely bxl.exe) is in use before attempting to delete, so we avoid partially deleting files
            // Not completely bullet proof (there can be a race) but it is highly unlikely the process starts to be in use right after this check
            Assembly rootAssembly = Assembly.GetEntryAssembly();
            Contract.Assert(rootAssembly != null, "Could not look up entry assembly");

            string assemblyFullPath = Path.Combine(serverDeploymentRoot, new FileInfo(AssemblyHelper.GetThisProgramExeLocation()).Name);

            // Try kill process using Process.Kill.
            var killProcessResult = TryKillProcess(assemblyFullPath);

            if (!killProcessResult.Succeeded)
            {
                // Try kill process using wmci. Note that wmci is going to be deprecated, but it's been used here for a long time.
                var killProcessWithWMICResult = TryKillProcessWithWMIC(assemblyFullPath);

                if (!killProcessWithWMICResult.Succeeded)
                {
                    throw killProcessWithWMICResult.Failure.Annotate(killProcessResult.Failure.DescribeIncludingInnerFailures()).Throw();
                }
            }
        }

        private static Possible<Unit> TryKillProcessWithWMIC(string assemblyFullPath)
        {
            // We make sure there is no server process running. Observe that if there was one, it can't
            // be doing a build since in this case the client binaries were overridden, which means they are not locked
            // So here we should be killing a server process that is about to timeout and die anyway
            string args = string.Format(CultureInfo.InvariantCulture, KillBuildXLServerCommandLineArgs, assemblyFullPath);

            // wmic needs escaped backslashes
            args = args.Replace("\\", "\\\\");

            var killServer = new ProcessStartInfo(KillBuildXLServerCommandLine, args);
            killServer.WindowStyle = ProcessWindowStyle.Hidden;

            Process process = null;

            try
            {
                process = Process.Start(killServer);
                process.WaitForExit();
                return Unit.Void;
            }
            catch (Exception e)
            {
                return new Failure<string>(I($"Failed to kill process with path '{assemblyFullPath}'"), new Failure<Exception>(e));
            }
            finally
            {
                if (process != null)
                {
                    process.Dispose();
                }
            }
        }

        private static Possible<Unit> TryKillProcess(string assemblyFullPath)
        {
            string processName = Path.GetFileNameWithoutExtension(assemblyFullPath);

            foreach (var processToKill in Process.GetProcessesByName(processName).Where(p => string.Equals(assemblyFullPath, p.MainModule.FileName, StringComparison.OrdinalIgnoreCase)))
            {
                try
                {
                    if (!processToKill.HasExited)
                    {
                        processToKill.Kill();
                        processToKill.WaitForExit(3000);
                    }
                }
                catch (Exception e) when (
                    e is System.ComponentModel.Win32Exception
                    || e is NotSupportedException
                    || e is InvalidOperationException
                    || e is SystemException)
                {
                    return new Failure<string>(I($"Failed to kill process with name '{processName}' (process id: {processToKill.Id}) and path '{assemblyFullPath}'"), new Failure<Exception>(e));
                }
            }

            return Unit.Void;
        }

        /// <summary>
        /// Calculates a hash of the contents of the BuildXL binaries from the server deployment directory.
        /// </summary>
        public static string GetDeploymentCacheHash(string deploymentDir)
        {
            AppDeployment serverDeployment = AppDeployment.ReadDeploymentManifest(deploymentDir, AppDeployment.ServerDeploymentManifestFileName);
            return serverDeployment.TimestampBasedHash.ToHex();
        }

        /// <summary>
        /// Checks if the deployed server bits are still up-to-date.
        /// </summary>
        public static bool IsServerDeploymentOutOfSync(string serverDeploymentRoot, AppDeployment clientApp, out string deploymentDir)
        {
            deploymentDir = ComputeDeploymentDir(serverDeploymentRoot);
            return !Directory.Exists(deploymentDir) || clientApp.TimestampBasedHash.ToHex() != GetDeploymentCacheHash(deploymentDir);
        }
    }
}
