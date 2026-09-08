// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections;
using System.Collections.Generic;

namespace BuildXL.Pips
{
    /// <summary>
    /// Provides an efficient read-only list of contiguous <see cref="PipId"/> values from one through a specified maximum.
    /// </summary>
    /// <remarks>
    /// This list backs <see cref="IPipTable.StableKeys"/> and the contiguous-ID fast path for <see cref="IPipTable.Keys"/>.
    /// Each pip ID is computed from its list index instead of being stored in a separate collection.
    /// </remarks>
    internal sealed class PipIdRangeList : IList<PipId>
    {
        private readonly int m_lastId;

        public PipIdRangeList(int lastId)
        {
            m_lastId = lastId;
        }

        public int IndexOf(PipId item) => Contains(item) ? (int)item.Value - 1 : -1;

        public void Insert(int index, PipId item) => throw new NotImplementedException();

        public void RemoveAt(int index) => throw new NotImplementedException();

        public PipId this[int index]
        {
            get => new PipId((uint)index + 1);
            set => throw new NotImplementedException();
        }

        public void Add(PipId item) => throw new NotImplementedException();

        public void Clear() => throw new NotImplementedException();

        public bool Contains(PipId item) => item.Value > 0 && item.Value <= (uint)m_lastId;

        public void CopyTo(PipId[] array, int arrayIndex)
        {
            for (int i = 1; i <= m_lastId; i++)
            {
                array[i + arrayIndex - 1] = new PipId((uint)i);
            }
        }

        public int Count => m_lastId;

        public bool IsReadOnly => true;

        public bool Remove(PipId item) => throw new NotImplementedException();

        public IEnumerator<PipId> GetEnumerator()
        {
            for (uint i = 1; i <= m_lastId; i++)
            {
                yield return new PipId(i);
            }
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
