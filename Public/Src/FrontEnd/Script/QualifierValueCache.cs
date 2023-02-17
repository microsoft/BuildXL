// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.ContractsLight;
using System.Threading;
using BuildXL.FrontEnd.Script.Values;
using BuildXL.Utilities.Core;
using JetBrains.Annotations;

namespace BuildXL.FrontEnd.Script
{
    /// <summary>
    /// Cache for constructed <see cref="QualifierValue"/>.
    /// </summary>
    public sealed class QualifierValueCache
    {
        private const int CacheSize = 24;
        private readonly QualifierValue[] m_qualifierValuesCache = new QualifierValue[CacheSize];

        /// <summary>
        /// Returns <see cref="QualifierValue"/> if the cache already contains it.
        /// </summary>
        public QualifierValue TryGet(QualifierId qualifierId)
        {
            Contract.Requires(qualifierId.IsValid);

            if (qualifierId.Id < CacheSize)
            {
                return Volatile.Read(ref m_qualifierValuesCache[qualifierId.Id]);
            }

            return null;
        }

        /// <summary>
        /// Adds a given <paramref name="value"/> to the cache if the corresponding qualifier id is less then a size of the cache.
        /// </summary>
        public bool TryAdd([NotNull]QualifierValue value)
        {
            if (value.QualifierId.Id < CacheSize)
            {
                Volatile.Write(ref m_qualifierValuesCache[value.QualifierId.Id], value);
                return true;
            }

            return false;
        }
    }
}
