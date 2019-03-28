// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections;
using System.Collections.Generic;
using BuildXL.Utilities;

namespace Tool.ExecutionLogSdk
{
    /// <summary>
    /// This class functions as a wrapper for a ConcurrentHashSet where the
    /// type is hardcoded to StringId.
    /// </summary>
    /// <remarks>
    /// This class can be exposed as a ReadOnlyCollection where the key type is
    /// set to string. This allows us to expose strings to the user while
    /// only having to store StringIds internally. The translation from
    /// StringId to string is done via the StringTable. The purpose of this
    /// is to save memory at run time.
    /// </remarks>
    /// <typeparam name="TValue">The type of value to store in the dictionary</typeparam>
    internal sealed class StringIdConcurrentHashSet : IReadOnlyCollection<string>, ICollection<string>
    {
        #region Private properties

        /// <summary>
        /// The underlying ConcurrentHashSet that stores the StringIds
        /// </summary>
        private ConcurrentHashSet<StringId> m_hashSet;

        /// <summary>
        /// The StringTable instance used to convert between StringId and strings
        /// </summary>
        private StringTable m_stringTable;
        #endregion

        #region Constructor

        /// <summary>
        /// Initializes the ConcurrentHashSet and sets the StringTable instance to use
        /// </summary>
        /// <param name="stringTable">Used to convert between strings and StringIds</param>
        public StringIdConcurrentHashSet(StringTable stringTable)
        {
            m_hashSet = new ConcurrentHashSet<StringId>();
            m_stringTable = stringTable;
        }
        #endregion

        #region Internal methods
        internal void Add(StringId stringId)
        {
            m_hashSet.Add(stringId);
        }
        #endregion

        #region Public and Interface Methods

        public int Count => m_hashSet.Count;

        public bool IsReadOnly => true;

        public bool Contains(string item)
        {
            return m_hashSet.Contains(m_stringTable.StringToId(item));
        }

        public IEnumerator<string> GetEnumerator()
        {
            foreach (StringId stringId in m_hashSet)
            {
                yield return m_stringTable.IdToString(stringId);
            }
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        public void CopyTo(string[] array, int arrayIndex)
        {
            if (array == null)
            {
                throw new ArgumentNullException("array");
            }

            if (arrayIndex < 0 || arrayIndex > array.Length)
            {
                throw new ArgumentOutOfRangeException("arrayIndex");
            }

            if ((array.Length - arrayIndex) < m_hashSet.Count)
            {
                throw new ArgumentException("Destination array is not large enough. Check array.Length and arrayIndex.");
            }

            foreach (StringId stringId in m_hashSet)
            {
                array[arrayIndex++] = m_stringTable.IdToString(stringId);
            }
        }
        #endregion

        #region Unimplemented Interface Methods
        public void Add(string item)
        {
            throw new NotImplementedException();
        }

        public void Clear()
        {
            throw new NotImplementedException();
        }

        public bool Remove(string item)
        {
            throw new NotImplementedException();
        }
        #endregion
    }
}
