// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.ContractsLight;
using System.Globalization;

namespace BuildXL.Cache.Interfaces
{
    /// <summary>
    /// Represents a failure where the cache has determined it's internal state is not consistent for some reason.
    /// </summary>
    public class InconsistentCacheStateFailure : CacheBaseFailure
    {
        private readonly object[] m_args;
        private readonly string m_format;

        /// <nodoc />
        public InconsistentCacheStateFailure(string format, params object[] args)
        {
            Contract.Requires(format != null);

            m_format = format;
            m_args = args;
        }

        /// <inheritdoc/>
        public override string Describe()
        {
            return string.Format(CultureInfo.InvariantCulture, m_format, m_args);
        }
    }
}
