// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using BuildXL.Utilities.Collections;

namespace BuildXL.Scheduler.Graph
{
    /// <summary>
    /// Typed wrapper around ConcurrentDenseIndex
    /// </summary>
    [SuppressMessage("Microsoft.Naming", "CA1711:IdentifiersShouldNotHaveIncorrectSuffix")]
    [SuppressMessage("Microsoft.Performance", "CA1815:OverrideEqualsAndOperatorEqualsOnValueTypes")]
    public readonly struct ConcurrentNodeDictionary<TValue>
    {
        private readonly ConcurrentDenseIndex<TValue> m_index;

        /// <summary>
        /// Constructor
        /// </summary>
        public ConcurrentNodeDictionary(bool debug)
        {
            m_index = new ConcurrentDenseIndex<TValue>(debug: debug);
        }

        /// <summary>
        /// Gets whether the dictionary represents a valid instance
        /// </summary>
        public bool IsValid => m_index != null;

        /// <summary>
        /// Gets or sets the value for the specified node
        /// </summary>
        [SuppressMessage("Microsoft.Design", "CA1043:UseIntegralOrStringArgumentForIndexers")]
        public TValue this[NodeId node]
        {
            get
            {
                Contract.Requires(IsValid);
                return m_index[node.Value];
            }

            set
            {
                Contract.Requires(IsValid);
                m_index[node.Value] = value;
            }
        }
    }
}
