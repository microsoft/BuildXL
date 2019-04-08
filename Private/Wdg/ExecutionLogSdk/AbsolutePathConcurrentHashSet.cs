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
    /// type is hardcoded to AbsolutePath.
    /// </summary>
    /// <remarks>
    /// This class can be exposed as a ReadOnlyCollection where the key type is
    /// set to string. This allows us to expose strings to the user while
    /// only having to store AbsolutePaths internally. The translation from
    /// AbsolutePath to string is done via the PathTable. The purpose of this
    /// is to save memory at run time.
    /// </remarks>
    /// <typeparam name="TValue">The type of value to store in the dictionary</typeparam>
    internal sealed class AbsolutePathConcurrentHashSet : IReadOnlyCollection<string>, ICollection<string>
    {
        #region Private properties

        /// <summary>
        /// The underlying ConcurrentHashSet that stores the AbsolutePaths
        /// </summary>
        private ConcurrentHashSet<AbsolutePath> m_hashSet;

        /// <summary>
        /// The PathTable instance used to convert between AbsolutePaths and strings
        /// </summary>
        private PathTable m_pathTable;
        #endregion

        #region Constructor

        /// <summary>
        /// Initializes the ConcurrentHashSet and sets the PathTable instance to use
        /// </summary>
        /// <param name="pathTable">Used to convert between strings and AbsolutePaths</param>
        public AbsolutePathConcurrentHashSet(PathTable pathTable)
        {
            m_hashSet = new ConcurrentHashSet<AbsolutePath>();
            m_pathTable = pathTable;
        }
        #endregion

        #region Internal methods
        internal void Add(AbsolutePath absolutePath)
        {
            m_hashSet.Add(absolutePath);
        }
        #endregion

        #region Public and Interface Methods

        public int Count => m_hashSet.Count;

        public bool IsReadOnly => true;

        public bool Contains(string item)
        {
            return m_hashSet.Contains(m_pathTable.StringToAbsolutePath(item));
        }

        public IEnumerator<string> GetEnumerator()
        {
            foreach (AbsolutePath absolutePath in m_hashSet)
            {
                yield return m_pathTable.AbsolutePathToString(absolutePath);
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

            foreach (AbsolutePath absolutePath in m_hashSet)
            {
                array[arrayIndex++] = m_pathTable.AbsolutePathToString(absolutePath);
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
