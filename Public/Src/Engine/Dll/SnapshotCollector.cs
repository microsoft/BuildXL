// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Native.Processes;
using BuildXL.Pips;
using BuildXL.Scheduler.Graph;
using BuildXL.Utilities;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.Tasks;
using BuildXL.Utilities.Tracing;
using BuildXL.Utilities.Configuration;

using static BuildXL.Utilities.FormattableStringEx;
using ProcessNativeMethods = BuildXL.Native.Processes.ProcessUtilities;

namespace BuildXL.Engine
{
    /// <summary>
    /// Collector to collect snapshots
    /// </summary>
    public sealed class SnapshotCollector
    {
        /// <summary>
        /// The file to write the snapshot to
        /// </summary>
        private readonly AbsolutePath m_snapshotFile;

        /// <summary>
        /// The snapshot mode
        /// </summary>
        private readonly SnapshotMode m_snapshotMode;

        /// <summary>
        /// The environment variables
        /// </summary>
        private readonly Dictionary<string, string> m_environmentVariables;

        /// <summary>
        /// The encountered mounts
        /// </summary>
        private readonly List<IMount> m_mounts;

        /// <summary>
        /// The encountered config files
        /// </summary>
        private readonly ConcurrentBag<AbsolutePath> m_files;

        /// <summary>
        /// The pip graph
        /// </summary>
        private PipGraph m_pipGraph;

        /// <summary>
        /// The semantic path expander for retrieving path semantic info
        /// </summary>
        private SemanticPathExpander m_semanticPathExpander;

        /// <summary>
        /// The command line arguments
        /// </summary>
        private readonly IReadOnlyCollection<string> m_commandLineArguments;

        private readonly LoggingContext m_loggingContext;

        /// <summary>
        /// Constructs a new collector
        /// </summary>
        public SnapshotCollector(
            LoggingContext loggingContext,
            AbsolutePath snapshotFile,
            SnapshotMode snapshotMode,
            IReadOnlyCollection<string> commandLineArguments)
        {
            m_loggingContext = loggingContext;
            m_snapshotFile = snapshotFile;
            m_snapshotMode = snapshotMode;
            m_environmentVariables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            m_mounts = new List<IMount>();
            m_files = new ConcurrentBag<AbsolutePath>();
            m_commandLineArguments = commandLineArguments;
        }

        /// <summary>
        /// Records the pip graph
        /// </summary>
        public void SetPipGraph(PipGraph pipGraph)
        {
            m_pipGraph = pipGraph;
            m_semanticPathExpander = pipGraph.SemanticPathExpander;
        }

        /// <summary>
        /// Records a config file
        /// </summary>
        public void RecordFile(AbsolutePath path)
        {
            m_files.Add(path);
        }

        /// <summary>
        /// Records an environment variable
        /// </summary>
        public void RecordEnvironmentVariable(string key, string value)
        {
            m_environmentVariables[key] = value;
        }

        /// <summary>
        /// Records a Mount
        /// </summary>
        public void RecordMount(IMount mount)
        {
            m_mounts.Add(mount);
        }

        /// <summary>
        /// Creates a vhd file containing all inputs to the build
        /// </summary>
        [SuppressMessage("Microsoft.Naming", "CA2204:Literals should be spelled correctly")]
        [SuppressMessage("Microsoft.Reliability", "CA2000",
            Justification = "Dispose is indeed being called on the Timer object, not just the Dispose method FxCop expects")]
        private bool PersistFullBuild(IConfiguration configuration, PathTable pathTable, CancellationToken cancellationToken)
        {
            var snapshotFile = m_snapshotFile.ToString(pathTable);
            var mountFolder = snapshotFile + ".tmpdir";
            try
            {
                // Find a drive to mount the VHD as for this process. The VHD will be mapped to a particular NTFS folder
                // and then mounted as a drive using the drive mapping functionality. This is done to avoid issues with MAX_PATH
                // issues since many codebases have paths nearing MAX_PATH which would go over if rooted in a subdirectory
                // of even a reasonably short path length.
                char unusedDrive = char.MinValue;
                var systemDriveLetter = SpecialFolderUtilities.SystemDirectory[0];
                for (char drive = 'A'; drive <= 'Z'; drive++)
                {
                    if (drive == systemDriveLetter || drive == 'C')
                    {
                        continue;
                    }

                    if (!AbsolutePath.TryGet(pathTable, new StringSegment(drive + @":\"), out _))
                    {
                        unusedDrive = drive;
                    }
                }

                if (unusedDrive == char.MinValue)
                {
                    Tracing.Logger.Log.GenericSnapshotError(m_loggingContext, "Snapshot error: Could not find unused drive to use for snapshot destination");
                    return false;
                }

                bool mounted = VhdUtilities.Mount(snapshotFile, sizeMb: 1 << 20 /* 1 TB */, mountFolder: mountFolder);
                if (!mounted)
                {
                    Tracing.Logger.Log.GenericSnapshotError(
                        m_loggingContext,
                        I($"Snapshot error: Could not create VHD '{unusedDrive}' and mount at '{mountFolder}'"));
                    return false;
                }

                // Map drive for mounted vhd in order to minimize chance of MAX_PATH issues copying files to VHD.
                bool mappingApplied =
                    ProcessNativeMethods.ApplyDriveMappings(new[] { new PathMapping(unusedDrive, Path.GetFullPath(mountFolder)) });
                if (!mappingApplied)
                {
                    Tracing.Logger.Log.GenericSnapshotError(
                        m_loggingContext,
                        I($"Snapshot error: Drive mapping could not be applied from '{unusedDrive}' to '{mountFolder}'"));
                    return false;
                }

                var mountDriveRoot = unusedDrive + @":\";
                var buildEvaluationFiles = new HashSet<AbsolutePath>(m_files);

                if (configuration.Cache.CacheConfigFile.IsValid)
                {
                    buildEvaluationFiles.Add(configuration.Cache.CacheConfigFile);
                }

                // Track used roots so mappings can be added for inputs and outputs when running BuildXL on snap
                ConcurrentDictionary<AbsolutePath, Unit> usedRoots = new ConcurrentDictionary<AbsolutePath, Unit>();

                int capturedFileCount = 0;
                int totalFileCount = buildEvaluationFiles.Count + m_pipGraph.FileCount;

                Action updateStatus = () => { Console.WriteLine("Snapshot: {0} of {1} files captured", capturedFileCount, totalFileCount); };

                updateStatus();
                var t = new StoppableTimer(updateStatus, 5000, 5000);

                // Copy the build evaluation files
                Parallel.ForEach(
                    buildEvaluationFiles.ToArray(),
                    new ParallelOptions() { MaxDegreeOfParallelism = Environment.ProcessorCount, CancellationToken = cancellationToken },
                    buildEvaluationFile =>
                    {
                        usedRoots[buildEvaluationFile.GetRoot(pathTable)] = Unit.Void;
                        CopyPath(pathTable, buildEvaluationFile, mountDriveRoot);
                        Interlocked.Increment(ref capturedFileCount);
                    });

                if (m_pipGraph != null)
                {
                    // Copy all source files
                    Parallel.ForEach(
                        m_pipGraph.AllFiles.ToArray(),
                        new ParallelOptions {MaxDegreeOfParallelism = Environment.ProcessorCount, CancellationToken = cancellationToken},
                        pipFile =>
                        {
                            try
                            {
                                usedRoots[pipFile.Path.GetRoot(pathTable)] = Unit.Void;
                                if (pipFile.IsOutputFile)
                                {
                                    return;
                                }

                                if (!buildEvaluationFiles.Contains(pipFile.Path))
                                {
                                    CopyPath(pathTable, pipFile.Path, mountDriveRoot);
                                }
                            }
                            finally
                            {
                                Interlocked.Increment(ref capturedFileCount);
                            }
                        });
                }

                // kill and wait for the status timer to die...
                t.Dispose();

                using (var writer = new StreamWriter(File.OpenWrite(Path.Combine(mountDriveRoot, "notes.txt"))))
                {
                    writer.WriteLine("Full Build Snapshot Info");
                    writer.WriteLine();
                    WriteNotes(configuration, pathTable, writer, path => path.ToString(pathTable));
                }

                using (var writer = new StreamWriter(File.OpenWrite(Path.Combine(mountDriveRoot, "runbxl.cmd"))))
                {
                    writer.Write("%BUILDXL_EXE_PATH% \"@%~dp0\\buildxl.rsp\" %*");
                    foreach (var usedRoot in usedRoots.Keys)
                    {
                        var usedRootLetter = usedRoot.ToString(pathTable)[0];
                        if (usedRootLetter.ToUpperInvariantFast() == 'C')
                        {
                            continue;
                        }

                        Directory.CreateDirectory(Path.Combine(mountDriveRoot, usedRootLetter.ToString()));
                        writer.Write(" /rootMap:{0}=\"%~dp0\\{0}\"", usedRootLetter);
                    }
                }

                using (var writer = new StreamWriter(File.OpenWrite(Path.Combine(mountDriveRoot, "buildxl.rsp"))))
                {
                    foreach (var argument in m_commandLineArguments)
                    {
                        writer.WriteLine(argument);
                    }

                    // Disable snapshot when running from snapshot
                    writer.WriteLine("/snapshotMode:None");

                    foreach (var envVar in m_environmentVariables)
                    {
                        writer.WriteLine("/envVar:{0}={1}", envVar.Key, envVar.Value ?? string.Empty);
                    }
                }
            }
            finally
            {
                Analysis.IgnoreResult(VhdUtilities.Dismount(snapshotFile, mountFolder));

                // TODO: Report error.
            }

            return true;
        }

        private void CopyPath(PathTable pathTable, AbsolutePath path, string mountDriveRoot)
        {
            if (m_semanticPathExpander != null)
            {
                var semanticInfo = m_semanticPathExpander.GetSemanticPathInfo(path);
                if (semanticInfo.IsValid && semanticInfo.IsSystem)
                {
                    // System files are not captured by build snapshot
                    return;
                }
            }

            var filePath = path.ToString(pathTable);
            if (File.Exists(filePath))
            {
                var destinationPath = Path.Combine(mountDriveRoot, filePath.Replace(":", string.Empty));
                Directory.CreateDirectory(Path.GetDirectoryName(destinationPath));
                File.Copy(filePath, destinationPath);
            }
        }

        /// <summary>
        /// Creates a zip file containing the spec files or a VHD containing the full set of build inputs.
        /// </summary>
        public bool Persist(IConfiguration configuration, PathTable pathTable, CancellationToken cancellationToken = default(CancellationToken))
        {
            try
            {
                switch (m_snapshotMode)
                {
                    case SnapshotMode.Evaluation:
                        PersistEvaluationZip(configuration, pathTable);
                        return true;
                    case SnapshotMode.Full:
                    default:
                        Contract.Assume(m_snapshotMode == SnapshotMode.Full, "Unhandled snapshot mode");
                        return PersistFullBuild(configuration, pathTable, cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                return false;
            }
        }

        [SuppressMessage("Microsoft.Usage", "CA2202:Do not dispose objects multiple times")]
        private void PersistEvaluationZip(IConfiguration configuration, PathTable pathTable)
        {
            var snapshotFile = m_snapshotFile.ToString(pathTable);
            var layout = configuration.Layout;

            HashSet<AbsolutePath> paths = new HashSet<AbsolutePath>(m_files);

            try
            {
                using (var stream = File.Open(snapshotFile, FileMode.Create))
                {
                    int maxLength = 0;
                    var notesPath = new AbsolutePath(-1);
                    Dictionary<AbsolutePath, string> pathMap = new Dictionary<AbsolutePath, string>();
                    pathMap.Add(notesPath, "notes.txt");

                    // Use relative path from obj if src is under obj
                    bool bypassSource = layout.ObjectDirectory.TryGetRelative(pathTable, layout.SourceDirectory, out RelativePath relativePath);

                    // Use relative path from src if obj is under src
                    bool bypassObject = layout.SourceDirectory.TryGetRelative(pathTable, layout.ObjectDirectory, out relativePath);

                    using (ZipArchive archive = new ZipArchive(stream, ZipArchiveMode.Update))
                    {
                        foreach (var path in paths)
                        {
                            string result;
                            if (!bypassObject && layout.ObjectDirectory.TryGetRelative(pathTable, path, out relativePath))
                            {
                                // All output files are root under obj
                                result = Path.Combine("obj", relativePath.ToString(pathTable.StringTable));
                            }
                            else if (!bypassSource && layout.SourceDirectory.TryGetRelative(pathTable, path, out relativePath))
                            {
                                // All source files are root under src
                                result = Path.Combine("src", relativePath.ToString(pathTable.StringTable));
                            }
                            else
                            {
                                // Just make full path into a 'relative path' under the external root (ext).
                                result = Path.Combine("ext", path.ToString(pathTable).Replace(":", string.Empty));
                            }

                            pathMap[path] = result;
                            maxLength = Math.Max(result.Length, maxLength);
                        }

                        foreach (var pathEntry in pathMap)
                        {
                            var entry = archive.CreateEntry(pathEntry.Value, CompressionLevel.Fastest);
                            var pathKey = pathEntry.Key;
                            bool deleteEntry = false;
                            using (var entryStream = entry.Open())
                            {
                                if (pathKey.Value.Value > 0)
                                {
                                    // This path corresponds to an actual file.
                                    // Copy the file into the zip file if it exists. If it doesn't exist,
                                    // it may be a directory or just doesn't exist at all. We need to delete
                                    // the zip entry we optimistically created.
                                    var fullPath = pathKey.ToString(pathTable);
                                    if (File.Exists(fullPath))
                                    {
                                        using (var fileStream = File.OpenRead(fullPath))
                                        {
                                            fileStream.CopyTo(entryStream);
                                        }
                                    }
                                    else
                                    {
                                        deleteEntry = true;
                                    }
                                }
                                else
                                {
                                    // Write the custom files
                                    using (var writer = new StreamWriter(entryStream))
                                    {
                                        if (pathKey == notesPath)
                                        {
                                            Func<AbsolutePath, string> mapPath = p => pathMap[p];

                                            WriteNotes(configuration, pathTable, writer, mapPath);

                                            WriteHeader(writer, "Mapping");
                                            foreach (var pathMapping in pathMap)
                                            {
                                                if (pathMapping.Key.Value.Value > 0)
                                                {
                                                    writer.WriteLine(
                                                        "{0} <= {1}",
                                                        pathMapping.Value.PadRight(maxLength, ' '),
                                                        pathMapping.Key.ToString(pathTable));
                                                }
                                            }
                                        }
                                    }
                                }
                            }

                            if (deleteEntry)
                            {
                                entry.Delete();
                            }
                        }
                    }
                }
            }
            catch (IOException e)
            {
                Tracing.Logger.Log.ErrorSavingSnapshot(Events.StaticContext, snapshotFile, e.Message);
            }
            catch (UnauthorizedAccessException e)
            {
                Tracing.Logger.Log.ErrorSavingSnapshot(Events.StaticContext, snapshotFile, e.Message);
            }
        }

        private void WriteNotes(IConfiguration configuration, PathTable pathTable, StreamWriter writer, Func<AbsolutePath, string> mapPath)
        {
            var layout = configuration.Layout;

            // Write the notes file which contains some useful info about the repro
            // TODO: We need to add support for RSP files to be precise
            WriteHeader(writer, "Command Line");
            writer.WriteLine(CurrentProcess.GetCommandLine());

            WriteHeader(writer, "Root Configuration File");
            writer.WriteLine(mapPath(configuration.Layout.PrimaryConfigFile));

            WriteHeader(writer, "Layout");
            writer.WriteLine("SourceDirectory = {0}", layout.SourceDirectory.ToString(pathTable));
            writer.WriteLine("ObjectDirectory = {0}", layout.ObjectDirectory.ToString(pathTable));
            writer.WriteLine("CacheDirectory = {0}", layout.CacheDirectory.ToString(pathTable));
            writer.WriteLine("FileContentTableFile = {0}", layout.FileContentTableFile.ToString(pathTable));
            writer.WriteLine("MaxRelativeOutputDirectoryLength = {0}", configuration.Engine.MaxRelativeOutputDirectoryLength);

            WriteHeader(writer, "Effective Environment Variables");
            foreach (var envVar in m_environmentVariables)
            {
                writer.WriteLine("{0}={1}", envVar.Key, envVar.Value ?? "      <THIS VARIABLE WAS NOT SET, BUT WAS USED>");
            }

            WriteHeader(writer, "Mounts");
            foreach (var mount in m_mounts)
            {
                writer.WriteLine(mount.Name.ToString(pathTable.StringTable));
                writer.WriteLine("\tPath: {0}", mount.Path.ToString(pathTable));
                writer.WriteLine("\tIsReadable: {0}", mount.IsReadable);
                writer.WriteLine("\tIsWritable: {0}", mount.IsWritable);
                writer.WriteLine("\tAllowCreateDirectory: {0}", mount.AllowCreateDirectory);
                writer.WriteLine("\tTrackSourceFileChanges: {0}", mount.TrackSourceFileChanges);
            }
        }

        /// <summary>
        /// Helper to write a header
        /// </summary>
        private static void WriteHeader(TextWriter writer, string header)
        {
            writer.WriteLine();
            writer.WriteLine("********************************************");
            writer.WriteLine("* {0}", header);
            writer.WriteLine("********************************************");
        }
    }
}
