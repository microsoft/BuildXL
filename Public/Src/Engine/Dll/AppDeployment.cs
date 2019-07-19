// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using BuildXL.Engine.Cache;
using BuildXL.FrontEnd.Script.Constants;
using BuildXL.Native.IO;
using BuildXL.Storage;
using BuildXL.Utilities;

namespace BuildXL.Engine
{
    /// <summary>
    /// Deployment of the BuildXL application
    /// </summary>
    /// <remarks>
    /// This is in the engine rather than BuildXLApp because it is also used for the graph cache identity
    /// </remarks>
    public sealed class AppDeployment
    {
        /// <summary>
        /// Filename of the deployment manifest, used in BuildXL's Deployment description
        /// </summary>
        public const string DeploymentManifestFileName = "BuildXL.Deployment.manifest";

        /// <summary>
        /// Filename of the server deployment manifest, used in BuildXL's Deployment description and
        /// responsible for the package contents of the server deployment only
        /// </summary>
        public const string ServerDeploymentManifestFileName = "BuildXL.ServerDeployment.manifest";

        /// <summary>
        /// The name of the manifest file that describes the version of BuildXL. For safety, the content hash of this
        /// file is included in the TimestampBasedHash to identify the BuildXL version. This prevents issue if the
        /// mechanism by which BuildXL is distributed keeps consistent timestamps.
        /// </summary>
        public const string BuildXLBrandingManifestFileName = "BuildXL.manifest";

        /// <summary>
        /// The base directory of the deployment
        /// </summary>
        public string BaseDirectory { get; }

        /// <summary>
        /// Timestamp based hash of the deployment. This will change whenever the last write times change on any of the
        /// included files
        /// </summary>
        public Fingerprint TimestampBasedHash { get; private set; }

        /// <summary>
        /// Debug info for computation of timestamp based hash
        /// </summary>
        public string TimestampBasedHashDebug { get; private set; }

        /// <summary>
        /// The time it took to compute <see cref="TimestampBasedHash"/>. This is used for logging
        /// </summary>
        public TimeSpan ComputeTimestampBasedHashTime { get; private set; }

        // Static cache of the running application.
        private static AppDeployment s_deployment;
        private readonly List<string> m_filesNames;

        private Fingerprint? m_computedContentHashBasedFingerprint;

        private AppDeployment(string deploymentBaseDir, List<string> fileNamesInDeployment, bool skipManifestCheckTestHook)
        {
            BaseDirectory = deploymentBaseDir;
            m_filesNames = fileNamesInDeployment;
            var result = ComputeTimestampBasedHashInternal(skipManifestCheckTestHook);
            TimestampBasedHash = result.Item1;
            TimestampBasedHashDebug = result.Item2;
        }

        /// <summary>
        /// Creates an AppDeployment based on the deployment manifest
        /// </summary>
        /// <remarks>
        /// This throws rather than logging errors directly because it may be called before logging is set up. The caller
        /// is responsible for handling error logging
        /// </remarks>
        /// <exception cref="BuildXLException">Thrown if the manifest is not found at the expected location or cannot be read</exception>
        public static AppDeployment ReadDeploymentManifestFromRunningApp()
        {
            if (s_deployment == null)
            {
                string baseDir = Path.GetFullPath(AppDomain.CurrentDomain.BaseDirectory);
                s_deployment = ReadDeploymentManifest(baseDir, ServerDeploymentManifestFileName);
            }

            return s_deployment;
        }

        /// <summary>
        /// Creates an AppDeployment based on the provided application base directory and deployment manifest
        /// </summary>
        /// <remarks>
        /// This throws rather than logging errors directly because it may be called before logging is set up. The caller
        /// is responsible for handling error logging
        /// </remarks>
        [SuppressMessage("Microsoft.Usage", "CA2202")]
        public static AppDeployment ReadDeploymentManifest(string baseDir, string fileName, bool skipManifestCheckTestHook = false)
        {
            List<string> fileNames = new List<string>();
            using (FileStream deploymentManifest = FileUtilities.CreateFileStream(
                Path.Combine(baseDir, fileName),
                FileMode.Open, FileAccess.Read, FileShare.Delete))
            {
                using (StreamReader reader = new StreamReader(deploymentManifest))
                {
                    while (!reader.EndOfStream)
                    {
                        fileNames.Add(reader.ReadLine());
                    }
                }
            }

            return new AppDeployment(baseDir, fileNames, skipManifestCheckTestHook);
        }

        /// <summary>
        /// Gets the relative paths for files that are relevant to this deployment
        /// </summary>
        public IEnumerable<string> GetRelevantRelativePaths(bool forServerDeployment)
        {
            List<string> result = new List<string>(m_filesNames.Count);
            foreach (var fileName in m_filesNames)
            {
                string extension = Path.GetExtension(fileName);
                if (!string.IsNullOrEmpty(extension) &&
                    (extension.Equals(".dll", StringComparison.OrdinalIgnoreCase) ||
                     extension.Equals(".exe", StringComparison.OrdinalIgnoreCase) ||
                     // For the server deployment we want to copy .pdb files in order to get symbols when crashes happen.
                     // But it's wasteful to hash them for sake of the engine version. So exclude them in that context.
                     (forServerDeployment ? extension.Equals(".pdb", StringComparison.OrdinalIgnoreCase) : false) ||
                     extension.Equals(".config", StringComparison.OrdinalIgnoreCase) ||
                     ExtensionUtilities.IsScriptExtension(extension) ||
                     extension.Equals(".ts", StringComparison.OrdinalIgnoreCase) ||
                     extension.Equals(".json", StringComparison.OrdinalIgnoreCase)) ||
                     string.Equals(BuildXLBrandingManifestFileName, fileName, StringComparison.OrdinalIgnoreCase)
                    )
                {
                    // E.g. BuildXLEngine.DefaultCacheConfigFileName, EnlistmentLibrary configs
                    result.Add(fileName);
                }
            }

            // Sort the list by path so the hash computed is consistent run to run on the same machine.
            result.Sort();

            // Lets sanitize if we are on Unix based systems and make sure the paths are correct
            return OperatingSystemHelper.IsUnixOS ? result.Select(entry => NormalizePath(entry)) : result;
        }

        private Tuple<Fingerprint, string> ComputeTimestampBasedHashInternal(bool skipManifestCheckTestHook)
        {
            Stopwatch sw = Stopwatch.StartNew();

            // Computes a hash based on the paths and timestamps of all of the referenced files
            using (var wrapper = Pools.StringBuilderPool.GetInstance())
            {
                StringBuilder sb = wrapper.Instance;

                foreach (var file in GetRelevantRelativePaths(forServerDeployment: true))
                {
                    try
                    {
                        FileInfo fi = new FileInfo(Path.Combine(BaseDirectory, file));

                        sb.Append(fi.Name);
                        sb.Append(':');
                        sb.Append(fi.LastWriteTimeUtc.ToBinary());
                        sb.AppendLine();
                    }
#pragma warning disable ERP022 // TODO: This should really handle specific errors
                    catch
                    {
                        // noop for files that cannot be found. The manifest will include exteraneous files
                    }
#pragma warning restore ERP022 // Unobserved exception in generic exception handler
                }

                if (!skipManifestCheckTestHook)
                {
                    ContentHashingUtilities.SetDefaultHashType();
                    AddHashForManifestFile(sb);
                }

                if (sb.Length == 0)
                {
                    throw new BuildXLException("App Deployment hash could not be computed because no files from the deployment manifest could be accessed.");
                }

                string text = sb.ToString();
                Fingerprint fingerprint = FingerprintUtilities.Hash(text);
                ComputeTimestampBasedHashTime = sw.Elapsed;
                return new Tuple<Fingerprint, string>(fingerprint, text);
            }
        }

        /// <summary>
        /// The hash of the BuildXL version manifest file is added to the timestamp based hash for an extra level of security in
        /// case the timestamps of the BuildXL binaries don't change.
        /// </summary>
        private void AddHashForManifestFile(StringBuilder sb)
        {
            var possibleManifestFiles = m_filesNames.Where(s => Path.GetFileName(s).Equals(BuildXLBrandingManifestFileName, StringComparison.OrdinalIgnoreCase));
            if (possibleManifestFiles.Count() == 1)
            {
                string path = NormalizePath(possibleManifestFiles.First());
                sb.Append(path);
                sb.Append(':');

                try
                {
                    sb.Append(ContentHashingUtilities.HashString(File.ReadAllText(Path.Combine(BaseDirectory, path))).ToHex());
                }
                catch (Exception ex)
                {
                    throw new BuildXLException($"Could not hash manifest file '{path}' to include in server deployment identity.", ex);
                }
            }
            else
            {
                throw new BuildXLException($"Could not find manifest file '{BuildXLBrandingManifestFileName}' to include in server deployment identity.");
            }
        }

        /// <summary>
        /// Computes content hash based fingerprint.
        /// </summary>
        public Fingerprint ComputeContentHashBasedFingerprint(FileContentTable fileContentTable, Action<string, ContentHash> handlePathAndHash = null)
        {
            Contract.Requires(fileContentTable != null);

            if (m_computedContentHashBasedFingerprint.HasValue)
            {
                return m_computedContentHashBasedFingerprint.Value;
            }

            var relativePaths = GetRelevantRelativePaths(forServerDeployment: false).ToArray();
            var hashes = new ContentHash[relativePaths.Length];

            Parallel.For(
                0,
                relativePaths.Length,
                new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount },
                i =>
                {
                    hashes[i] = fileContentTable.GetAndRecordContentHashAsync(Path.Combine(BaseDirectory, relativePaths[i]))
                        .Result
                        .VersionedFileIdentityAndContentInfo
                        .FileContentInfo.Hash;
                });

            using (var hasher = new CoreHashingHelper(false))
            {
                for (int i = 0; i < relativePaths.Length; ++i)
                {
                    handlePathAndHash?.Invoke(relativePaths[i], hashes[i]);
                    hasher.Add(relativePaths[i], hashes[i]);
                }

                m_computedContentHashBasedFingerprint = hasher.GenerateHash();
                return m_computedContentHashBasedFingerprint.Value;
            }
        }

        private static string NormalizePath(string unnormalizedPath)
        {
            return unnormalizedPath.Replace('\\', Path.DirectorySeparatorChar);
        }
    }
}
