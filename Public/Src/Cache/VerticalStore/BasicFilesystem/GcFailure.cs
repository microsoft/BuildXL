// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.ContractsLight;
using System.Globalization;
using BuildXL.Cache.Interfaces;
using BuildXL.Utilities.Core;

namespace BuildXL.Cache.BasicFilesystem
{
    internal sealed class GcFailure : CacheBaseFailure
    {
        private readonly string m_cacheId;
        private readonly string m_failedAction;
        private readonly Exception m_ex;

        public GcFailure(string cacheId, string failedAction, Exception ex = null, Failure innerFailure = null)
            : base(innerFailure)
        {
            Contract.Requires(cacheId != null);
            Contract.Requires(failedAction != null);

            m_cacheId = cacheId;
            m_failedAction = failedAction;
            m_ex = ex;
        }

        /// <inheritdoc/>
        public override string Describe()
        {
            return string.Format(CultureInfo.InvariantCulture, "Cache [{0}] GC Failure: {1} {2}", m_cacheId, m_failedAction, m_ex?.ToString() ?? string.Empty);
        }
    }
}
