// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Threading;
using TypeScript.Net.Core;
using TypeScript.Net.Types;

namespace TypeScript.Net.TypeChecking
{
    internal class ThreadSafeCounters
    {
        public const int DefaultMinNodeId = (int)WellKnownNodeIds.DefaultMinNodeId;

        // Used concurrently
        private int m_typeCount = 0;

        public int GetTypeCount() => Volatile.Read(ref m_typeCount);

        // Used concurrently
        private int m_symbolCount = 0;

        public int IncrementSymbolCount() => Interlocked.Increment(ref m_symbolCount);

        public int GetSymbolCount() => Volatile.Read(ref m_symbolCount);

        private int m_nextMergeId;

        private int m_nextNodeId;

        private int m_nextSymbolId;

        public ThreadSafeCounters(int nextMergeId, int nextNodeId, int nextSymbolId)
        {
            m_nextMergeId = nextMergeId;
            m_nextNodeId = nextNodeId;
            m_nextSymbolId = nextSymbolId;
        }

        public int GetCurrentMergeId()
        {
            return Volatile.Read(ref m_nextMergeId);
        }

        public int GetCurrentNodeId()
        {
            return Volatile.Read(ref m_nextNodeId);
        }

        public int GetCurrentSymbolId()
        {
            return Volatile.Read(ref m_nextSymbolId);
        }

        /// <nodoc/>
        public int GetNextTypeId()
        {
            return Interlocked.Increment(ref m_typeCount);
        }

        /// <nodoc/>
        public int GetNodeId(INode node)
        {
            if (node.Id.IsValid())
            {
                return node.Id;
            }

            // Need to get NodeBase instance to access an internal field
            // in order to use lock-free id computation.
            var nodeBase = (NodeBase)node.ResolveUnionType();

            return SetNextIdentifierAtomic(ref nodeBase.m_id, ref m_nextNodeId);
        }

        /// <nodoc/>
        public int GetSymbolId(ISymbol symbol)
        {
            if (symbol.Id.IsValid())
            {
                return symbol.Id;
            }

            var symbolBase = (Symbol)symbol;

            return SetNextIdentifierAtomic(ref symbolBase.m_id, ref m_nextSymbolId);
        }

        /// <nodoc/>
        public int GetMergeId(ISymbol symbol)
        {
            if (symbol.MergeId.IsValid())
            {
                return symbol.MergeId;
            }

            var symbolBase = (Symbol)symbol;
            return SetNextIdentifierAtomic(ref symbolBase.m_mergeId, ref m_nextMergeId);
        }

        /// <summary>
        /// Helper function that updates a given id with a next value from a counter in an atomic fasion.
        /// </summary>
        private static int SetNextIdentifierAtomic(ref int id, ref int counter)
        {
            if (Interlocked.CompareExchange(ref id, CoreUtilities.ReservedInvalidIdentifier, CoreUtilities.InvalidIdentifier) ==
                CoreUtilities.InvalidIdentifier)
            {
                id = Interlocked.Increment(ref counter);
            }
            else
            {
                while (!Volatile.Read(ref id).IsValid())
                {
                    // Do nothing. should be quick to acquire node id
                }
            }

            return id;
        }
    }
}
