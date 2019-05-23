// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using BuildXL.FrontEnd.Sdk;
using BuildXL.Processes;
using BuildXL.Processes.Containers;
using BuildXL.Utilities;

namespace BuildXL.FrontEnd.Utilities
{
    /// <summary>
    /// Static methods with common logic for the FrontEnd resolvers
    /// </summary>
    public class FrontEndUtilities
    {
        /// <summary>
        /// Retrieves a list of search locations for an executable, inspecting a list of explicit candidates first or using PATH.
        /// The onEmptyResult action will be invoked if there are no search locations available
        /// The onPathParseFailure action will be invoked with the PATH as an argument if the PATH is malformed
        /// </summary>
        public static bool TryRetrieveExecutableSearchLocations(
            string frontEnd,
            FrontEndContext context,
            FrontEndEngineAbstraction engine,
            IReadOnlyCollection<AbsolutePath> explicitCandidates,
            out IEnumerable<AbsolutePath> searchLocations,
            Action onEmptyResult = null,
            Action<string> onPathParseFailure = null)
        {
            // If there are explicit search locations specified, use those
            if (explicitCandidates?.Count > 0)
            {
                searchLocations = explicitCandidates;
                return true;
            }

            // Otherwise use %PATH%
            if (!engine.TryGetBuildParameter("PATH", frontEnd, out string paths))
            {
                onEmptyResult?.Invoke();
                searchLocations = null;
                return false;
            }

            var locations = new List<AbsolutePath>();
            foreach (string path in paths.Split(new[] { Path.PathSeparator }, StringSplitOptions.RemoveEmptyEntries))
            {
                var nonEscapedPath = path.Trim('"');
                if (AbsolutePath.TryCreate(context.PathTable, nonEscapedPath, out var absolutePath))
                {
                    locations.Add(absolutePath);
                }
            }

            if (locations.Count == 0)
            {
                onPathParseFailure?.Invoke(paths);
                searchLocations = null;
                return false;
            }

            searchLocations = locations;
            return true;
        }

        /// <summary>
        /// Runs the a tool in a sandboxed process and returns the result.
        /// These optional callback Actions can be provided:
        ///     beforeLaunch is invoked right before the process is launched
        ///     onResult is invoked after getting a successful result
        /// </summary>>
        public static async Task<SandboxedProcessResult> RunSandboxedToolAsync(FrontEndContext context,
            string pathToTool,
            string buildStorageDirectory,
            FileAccessManifest fileAccessManifest,
            string arguments,
            string workingDirectory,
            string description,
            BuildParameters.IBuildParameters buildParameters,
            Action beforeLaunch = null,   // Invoked right before the process starts
            Action onResult = null      // Action to be taken after getting a successful result
            )
        {
            var info =
                new SandboxedProcessInfo(
                    context.PathTable,
                    new ToolBuildStorage(buildStorageDirectory),
                    pathToTool,
                    fileAccessManifest,
                    disableConHostSharing: true,
                    ContainerConfiguration.DisabledIsolation,
                    loggingContext: context.LoggingContext)
                {
                    Arguments = arguments,
                    WorkingDirectory = workingDirectory,
                    PipSemiStableHash = 0,
                    PipDescription = "CMakeRunner - Ninja specs and input files generator",
                    EnvironmentVariables = buildParameters
                };

            var process = await SandboxedProcessFactory.StartAsync(info, forceSandboxing: true);

            var registration = context.CancellationToken.Register(
                () =>
                {
                    try
                    {
                        process.KillAsync().GetAwaiter().GetResult();
                    }
                    catch (TaskCanceledException)
                    {
                        // If the process has already terminated or doesn't exist, an TaskCanceledException is raised.
                        // In either case, we swallow the exception, cancellation is already requested by the user
                    }
                });


            beforeLaunch?.Invoke();
            var result = process.GetResultAsync().ContinueWith(
                r =>
                {
                    // Dispose the registration for the cancellation token once the process is done
#pragma warning disable AsyncFixer02
                    registration.Dispose();
#pragma warning restore AsyncFixer02


                    //
                    onResult?.Invoke();

                    return r.GetAwaiter().GetResult();
                });

            return await result;
        }

        /// <summary>
        /// Get all the environment exposed to the process, with the values overriden by the engine
        /// </summary>
        public static IDictionary<string, string> GetEngineEnvironment(FrontEndEngineAbstraction engine, string frontEndName)
        {
            var engineEnvironment = new Dictionary<string, string>();
            IDictionary environment = Environment.GetEnvironmentVariables();

            foreach (string environmentVariable in environment.Keys)
            {
                // Expose as much of the environment as we can -- use the ones overriden by the Engine
                if (engine.TryGetBuildParameter(environmentVariable, frontEndName, out var value))
                {
                    engineEnvironment[environmentVariable] = value;
                }
            }

            return engineEnvironment;
        }

        /// <summary>
        /// Generate a basic file access manifest for front end tools
        /// </summary>
        public static FileAccessManifest GenerateToolFileAccessManifest(FrontEndContext context, AbsolutePath toolDirectory)
        {
            var pathTable = context.PathTable;
            // We make no attempt at understanding what the tool is going to do
            // We just configure the manifest to not fail on unexpected accesses, so they can be collected
            // later if needed
            var fileAccessManifest = new FileAccessManifest(pathTable)
                                     {
                                         FailUnexpectedFileAccesses = false,
                                         ReportFileAccesses = true,
                                         MonitorNtCreateFile = true,
                                         MonitorZwCreateOpenQueryFile = true,
                                         MonitorChildProcesses = true,
                                     };

            fileAccessManifest.AddScope(
                AbsolutePath.Create(pathTable, SpecialFolderUtilities.GetFolderPath(Environment.SpecialFolder.Windows)),
                FileAccessPolicy.MaskAll,
                FileAccessPolicy.AllowAllButSymlinkCreation);

            fileAccessManifest.AddScope(
                AbsolutePath.Create(pathTable, SpecialFolderUtilities.GetFolderPath(Environment.SpecialFolder.InternetCache)),
                FileAccessPolicy.MaskAll,
                FileAccessPolicy.AllowAllButSymlinkCreation);

            fileAccessManifest.AddScope(
                AbsolutePath.Create(pathTable, SpecialFolderUtilities.GetFolderPath(Environment.SpecialFolder.History)),
                FileAccessPolicy.MaskAll,
                FileAccessPolicy.AllowAllButSymlinkCreation);

            fileAccessManifest.AddScope(toolDirectory, FileAccessPolicy.MaskAll, FileAccessPolicy.AllowReadAlways);

            return fileAccessManifest;
        }



        /// <summary>
        /// The FrontEnds that use an out-of-proc tool should sandbox that process and call this method
        /// in order to tack the tool's file accesses, enumerations, etc. in order to make graph caching sound
        /// </summary>
        public static void TrackToolFileAccesses(FrontEndEngineAbstraction engine, FrontEndContext context, string frontEndName, ISet<ReportedFileAccess> fileAccesses, AbsolutePath frontEndFolder)
        {
            // Compute all parseable paths
            // TODO: does it make sense to consider enumerations, or as a result the graph will be too unstable? Does it matter for MsBuild graph construction?
            foreach (var access in fileAccesses)
            {
                string accessPath = access.GetPath(context.PathTable);
                if (AbsolutePath.TryCreate(context.PathTable, accessPath, out AbsolutePath path))
                {
                    // Ignore accesses under the frontend folder: these are files used for internal communication between
                    // BuildXL and the graph builder tool, and they are never files that MSBuild itself interacted with
                    if (path.IsWithin(context.PathTable, frontEndFolder))
                    {
                        continue;
                    }

                    if ((access.RequestedAccess & RequestedAccess.Enumerate) != 0)
                    {
                        engine.TrackDirectory(path.ToString(context.PathTable));
                    }
                    if ((access.RequestedAccess & RequestedAccess.Probe) != 0)
                    {
                        engine.FileExists(path);
                    }
                    if ((access.RequestedAccess & RequestedAccess.Read) != 0)
                    {
                        // Two things are happening here: we want to register if the file is present or absent. Engine.FileExists takes
                        // care of that. And in the case the file exists, record the content.
                        // There are apparently some repos that create and delete files during graph construction :(
                        // So we cannot trust detours and check for IsNonexistent on the access itself. Even though there were read/write accesses on a given file,
                        // the file may not exist at this point
                        if (engine.FileExists(path))
                        {
                            engine.RecordFrontEndFile(path, frontEndName);
                        }
                    }
                }
            }
        }

        private sealed class ToolBuildStorage : ISandboxedProcessFileStorage
        {
            private readonly string m_directory;

            /// <nodoc />
            public ToolBuildStorage(string directory)
            {
                m_directory = directory;
            }

            /// <inheritdoc />
            public string GetFileName(SandboxedProcessFile file)
            {
                return Path.Combine(m_directory, file.DefaultFileName());
            }
        }
    }
}
