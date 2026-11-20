// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BuildXL.Native.IO;
using BuildXL.Utilities.Core;

namespace BuildXL.Scheduler.Tracing
{
    /// <summary>
    /// Uploads BuildXL logs to an Azure DevOps build artifact through ##vso[artifact.upload] commands.
    /// </summary>
    /// <remarks>
    /// Logs that are complete before the end of the build (e.g. the graph files and the execution log parts) are written to folders
    /// next to the logs directory, under the same parent, and uploaded as soon as they are complete. At the end of the build only the
    /// logs directory is uploaded. In the artifact every file keeps its path relative to the parent, so a pipeline fallback that
    /// uploads the parent folder to the same artifact fills in or overwrites the same paths. The parent must therefore hold only these
    /// logs (e.g. /logsDirectory:Out\Logs\Build123\Logs).
    /// </remarks>
    public static class LogsArtifact
    {
        private static Func<string, string> s_translatePath = p => p;
        private static bool s_earlyUploadFailed;

        /// <summary>
        /// Name of the artifact, or null when BuildXL does not upload its logs.
        /// </summary>
        public static string Name { get; private set; }

        /// <summary>
        /// Sets up uploads for one build; a null <paramref name="name"/> disables them. <paramref name="translatePath"/> maps subst
        /// paths (e.g. B:\) to real paths, because the subst is removed once BuildXL exits.
        /// </summary>
        public static void Initialize(string name, Func<string, string> translatePath)
        {
            s_translatePath = translatePath;
            s_earlyUploadFailed = false;
            Name = name;
        }

        /// <summary>
        /// Folder next to the logs directory for logs that are uploaded during the build. Its name is its path in the artifact.
        /// </summary>
        public static string GetEarlyUploadFolder(string logsDirectory, string name) =>
            Path.Combine(Path.GetDirectoryName(logsDirectory.TrimEnd(Path.DirectorySeparatorChar)), name);

        /// <summary>
        /// Uploads completed files of a folder from <see cref="GetEarlyUploadFolder"/> to that folder in the artifact.
        /// <paramref name="uploadName"/> names this upload (e.g. the file uploaded), and is shown in the agent's log.
        /// </summary>
        public static void UploadEarly(string folder, IEnumerable<string> files, string uploadName)
        {
            // The agent uploads the contents of a folder to the artifact root, so the files are staged at their path relative to the
            // parent of the logs directory, i.e. under <parent>.upload\<upload name>\<folder name>, and the staging folder is uploaded.
            string parent = Path.GetDirectoryName(folder);
            string uploadFolder = Path.Combine(parent + ".upload", uploadName);
            try
            {
                Stage(uploadFolder, parent, files);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                s_earlyUploadFailed = true;
                Console.Out.WriteLine($"Could not upload logs from '{folder}' during the build: {ex.GetLogEventMessage()}");
                return;
            }

            UploadFolder(uploadFolder);
        }

        /// <summary>
        /// Uploads the logs directory, which holds the logs that were not uploaded during the build, to the artifact folder named
        /// after it. Returns false if some logs could not be uploaded during the build, so the pipeline fallback should upload
        /// everything.
        /// </summary>
        public static bool UploadRemaining(string logsDirectory)
        {
            // The agent adds each file of an upload to the artifact separately, two at a time, which takes most of the upload time
            // for many small files. Uploads run in parallel, so split the logs into a few uploads with about the same number of files.
            // Files keep their path relative to the parent, so the artifact has the same layout as the parent folder, which the
            // pipeline fallback uploads.
            const int UploadCount = 4;
            string parent = Path.GetDirectoryName(logsDirectory.TrimEnd(Path.DirectorySeparatorChar));
            var uploadFolders = Enumerable.Range(1, UploadCount).Select(i => Path.Combine(parent + ".upload", $"Logs.{i}")).ToList();
            try
            {
                var files = Directory.EnumerateFiles(logsDirectory, "*", SearchOption.AllDirectories).ToList();
                for (int i = 0; i < UploadCount; i++)
                {
                    Stage(uploadFolders[i], parent, files.Where((_, index) => index % UploadCount == i));
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                // Uploading the parent re-uploads the early logs to the same paths, which costs time but loses nothing.
                Console.Out.WriteLine($"Uploading all logs in one upload: {ex.GetLogEventMessage()}");
                uploadFolders = new List<string> { parent };
            }

            uploadFolders.Where(Directory.Exists).ToList().ForEach(UploadFolder);
            return !s_earlyUploadFailed;
        }

        /// <summary>
        /// Hard-links (or copies) files into a new staging folder at their path relative to <paramref name="root"/>.
        /// </summary>
        private static void Stage(string stagingFolder, string root, IEnumerable<string> files)
        {
            // Clear a folder left by an earlier build that used the same logs directory, so its files are not uploaded again.
            if (Directory.Exists(stagingFolder))
            {
                Directory.Delete(stagingFolder, recursive: true);
            }

            foreach (string file in files)
            {
                string link = Path.Combine(stagingFolder, file.Substring(root.Length).TrimStart(Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(link));
                if (FileUtilities.TryCreateHardLink(link, file) != CreateHardLinkStatus.Success)
                {
                    File.Copy(file, link, overwrite: true);
                }
            }
        }

        private static void UploadFolder(string folder)
        {
            // The agent ties the artifact to the container folder of its first upload, so every upload uses the artifact root.
            Console.Out.WriteLine($"##vso[artifact.upload containerfolder={Name};artifactname={Name}]{s_translatePath(folder)}");
            Console.Out.Flush();
        }
    }
}
