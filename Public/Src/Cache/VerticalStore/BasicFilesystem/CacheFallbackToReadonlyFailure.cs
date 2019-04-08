// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Globalization;
using BuildXL.Cache.Interfaces;

namespace BuildXL.Cache.BasicFilesystem
{
    /// <summary>
    /// Represents a failure to write to the configured storage location.
    /// </summary>
    internal sealed class CacheFallbackToReadonlyFailure : CacheBaseFailure
    {
        private readonly string m_cacheId;
        private readonly string m_path;
        internal const string FailureMessage = "Cache {0} cannot write to path {1}. The cache will be constructed read-only.";

        internal CacheFallbackToReadonlyFailure(string path, string cacheId)
        {
            m_path = path;
            m_cacheId = cacheId;
        }

        public override string Describe()
        {
            return string.Format(CultureInfo.InvariantCulture, FailureMessage, m_cacheId, m_path);
        }
    }
}
