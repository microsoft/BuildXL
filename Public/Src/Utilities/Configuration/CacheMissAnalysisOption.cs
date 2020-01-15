// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;

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
        public static CacheMissAnalysisOption RemoteMode(string[] keys)
        {
            return new CacheMissAnalysisOption(CacheMissMode.Remote, keys, AbsolutePath.Invalid);
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
        CustomPath
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
