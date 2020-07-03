// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Cache.Interfaces;

namespace BuildXL.Cache.MemoizationStoreAdapter
{
    /// <summary>
    /// Generic failure for MemoizationStore adapter
    /// </summary>
    /// <remarks>
    /// TODO: split out into more specific failure types
    /// </remarks>
    public class CacheFailure : CacheBaseFailure
    {
        private readonly string m_message;

        /// <summary>
        /// .ctor
        /// </summary>
        public CacheFailure(string message)
        {
            m_message = message;
        }

        /// <inheritdoc />
        public override string Describe()
        {
            return m_message;
        }
    }
}
