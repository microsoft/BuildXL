// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.Pips;
using BuildXL.Pips.Operations;
using BuildXL.Utilities;

namespace BuildXL.Scheduler
{
    /// <summary>
    /// Defines the information needed for caching of a pip.
    /// NOTE: No behavior should be defined in this class
    /// </summary>
    public class PipInfo
    {
        private string m_description;
        private readonly PipExecutionContext m_context;

        /// <nodoc />
        public PipInfo(
            Pip pip,
            PipExecutionContext context)
        {
            m_context = context;
            UnderlyingPip = pip;
        }

        /// <summary>
        /// The underlying pip
        /// </summary>
        public Pip UnderlyingPip { get; }

        /// <summary>
        /// The semistable hash of the underlying pip
        /// </summary>
        public long SemiStableHash => UnderlyingPip.SemiStableHash;

        /// <summary>
        /// The pip id
        /// </summary>
        public PipId PipId => UnderlyingPip.PipId;

        /// <summary>
        /// Gets the description of the pip
        /// </summary>
        public string Description => m_description ?? (m_description = UnderlyingPip.GetDescription(m_context));
    }
}
