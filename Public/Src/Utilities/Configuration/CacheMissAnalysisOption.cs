// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using BuildXL.Utilities.Core;

namespace BuildXL.Utilities.Configuration
{
    /// <summary>
    /// On-the-fly cache miss analysis option
    /// </summary>
    public sealed class CacheMissAnalysisOption
    {
        /// <nodoc />
        public CacheMissMode Mode { get; set; }
        
        /// <summary>
        /// The list of keys that are candidates for comparison
        /// </summary>
        public IReadOnlyList<string> Keys { get; set; }

        /// <summary>
        /// The directory path to read the fingerprint store for <see cref="CacheMissMode.CustomPath"/> mode
        /// </summary>
        public AbsolutePath CustomPath { get; set; }

        /// <summary>
        /// If this mode uses a remote fingerprint store, so the engine should publish the fingerprint store to the cache
        /// </summary>
        public bool ShouldPublishFingerprintStoreToCache => Mode == CacheMissMode.Remote || Mode == CacheMissMode.AzureDevOps || Mode == CacheMissMode.GitHashes;

        /// <nodoc />
        public static CacheMissAnalysisOption Disabled()
        {
            return new CacheMissAnalysisOption(CacheMissMode.Disabled, new List<string>(), AbsolutePath.Invalid);
        }

        /// <nodoc />
        public static CacheMissAnalysisOption LocalMode()
        {
            return new CacheMissAnalysisOption(CacheMissMode.Local, new List<string>(), AbsolutePath.Invalid);
        }

        /// <nodoc />
        public static CacheMissAnalysisOption AdoMode()
        {
            return new CacheMissAnalysisOption(CacheMissMode.AzureDevOps, new List<string>(), AbsolutePath.Invalid);
        }

        /// <nodoc />
        public static CacheMissAnalysisOption RemoteMode(string[] keys)
        {
            return new CacheMissAnalysisOption(CacheMissMode.Remote, keys, AbsolutePath.Invalid);
        }

        /// <nodoc />
        public static CacheMissAnalysisOption GitHashesMode(string[] keys)
        {
            return new CacheMissAnalysisOption(CacheMissMode.GitHashes, keys, AbsolutePath.Invalid);
        }

        /// <nodoc />
        public static CacheMissAnalysisOption CustomPathMode(AbsolutePath path)
        {
            return new CacheMissAnalysisOption(CacheMissMode.CustomPath, new List<string>(), path);
        }

        /// <nodoc />
        public CacheMissAnalysisOption() : this(CacheMissMode.Disabled, new List<string>(), AbsolutePath.Invalid)
        {}

        /// <nodoc />
        internal CacheMissAnalysisOption(CacheMissMode mode, IReadOnlyList<string> keys, AbsolutePath customPath)
        {
            Contract.Requires(keys != null);

            Mode = mode;
            Keys = keys;
            CustomPath = customPath;
        }
    }

    /// <summary>
    /// On-the-fly cache miss analysis mode
    /// </summary>
    public enum CacheMissMode
    {
        /// <summary>
        /// Disabled
        /// </summary>
        Disabled,

        /// <summary>
        /// Using the fingerprint store on the machine
        /// </summary>
        Local,

        /// <summary>
        /// Looking up the fingerprint store in the cache by the given keys
        /// </summary>
        Remote,

        /// <summary>
        /// Using the fingerprint store in the given directory
        /// </summary>
        CustomPath,

        /// <summary>
        /// Look up the fingerprint store in the cache using contextual information available from the environment
        /// on builds running on Azure DevOps pipelines. 
        /// The keys used are the names for repository branches that are related to the running build 
        /// (current branch name, and source and target branches when in a pull request build). 
        /// </summary>
        AzureDevOps,

        /// <summary>
        /// Look up the fingerprint store in the cache using recent git commit hashes
        /// </summary>
        GitHashes,
    }

    /// <summary>
    /// Cache miss diff format.
    /// </summary>
    public enum CacheMissDiffFormat
    {
        /// <summary>
        /// Custom (i.e., non-standard) Json diff format.
        /// </summary>
        CustomJsonDiff,

        /// <summary>
        /// Json patch diff format.
        /// </summary>
        /// <remarks>
        /// This format will soon be deprecated because 
        /// - the format is not easy to understand and looks cryptic, and
        /// - it relies on a buggy thrid-party package.
        /// However, some customers have already play around with this format. Thus,
        /// to avoid breaking customers hard, this format is preserved, but needs to be selected
        /// as the default will be <see cref="CustomJsonDiff"/>.
        /// </remarks>
        JsonPatchDiff,
    }
}
