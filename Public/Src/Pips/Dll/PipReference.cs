// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.ContractsLight;
using BuildXL.Pips.Operations;

namespace BuildXL.Pips
{
    /// <summary>
    /// Represents a lazily loaded pip
    /// </summary>
    public sealed class PipReference
    {
        /// <summary>
        /// The pip table containing the pip
        /// </summary>
        public readonly PipTable PipTable;

        /// <summary>
        /// The pip id for the pip
        /// </summary>
        public readonly PipId PipId;

        /// <summary>
        /// The callsite which created the lazy pip
        /// </summary>
        public readonly PipQueryContext PipQueryContext;

        private Pip m_pip;

        /// <summary>
        /// The type of the pip
        /// </summary>
        public PipType PipType
        {
            get
            {
                if (m_pip != null)
                {
                    return m_pip.PipType;
                }

                return PipTable.GetPipType(PipId);
            }
        }

        /// <summary>
        /// The semi-stable hash of the pip
        /// </summary>
        public long SemiStableHash
        {
            get
            {
                if (m_pip != null)
                {
                    return m_pip.SemiStableHash;
                }

                return PipTable.GetPipSemiStableHash(PipId);
            }
        }

        /// <summary>
        /// Class constructor
        /// </summary>
        public PipReference(PipTable pipTable, PipId pipId, PipQueryContext pipQueryContext)
        {
            Contract.Requires(pipTable != null);

            PipTable = pipTable;
            PipQueryContext = pipQueryContext;
            PipId = pipId;
        }

        /// <summary>
        /// Gets the pip instance, hydrating the pip if necessary
        /// </summary>
        public Pip HydratePip()
        {
            // ReSharper disable once ConvertIfStatementToNullCoalescingExpression
            if (m_pip == null)
            {
                m_pip = PipTable.HydratePip(PipId, PipQueryContext);
            }

            return m_pip;
        }
    }
}
