// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using BuildXL.Utilities;

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
}
