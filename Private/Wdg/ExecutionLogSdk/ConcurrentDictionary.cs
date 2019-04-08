// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;

namespace Tool.ExecutionLogSdk
{
    /// <summary>
    /// This class is identical to a ConcurrentDictionary but it also implements IReadOnlyDictionary
    /// </summary>
    /// <typeparam name="TKey">Specifies the dictionary's keys type</typeparam>
    /// <typeparam name="TValue">Specifies the dictionary's values type</typeparam>
    /// <remarks>In .NET 4.6 ConcurrentDictionary does implement IReadOnlyDictionary, however BuildXL is not built with .NET 4.6 yet.</remarks>
    internal sealed class ConcurrentDictionary<TKey, TValue> : System.Collections.Concurrent.ConcurrentDictionary<TKey, TValue>, IReadOnlyDictionary<TKey, TValue>
    {
        #region Constructor

        /// <summary>
        /// Public constructor. 31 is the default capacity used by ConcurrentDictionary.
        /// </summary>
        /// <param name="concurrencyLevel">This setting affects the number of locks that the dictionary is using. The default value is 32</param>
        /// <param name="initialCapacity">Initial capacity of the collection. Default value is 31, same value as the value from the base class</param>
        public ConcurrentDictionary(int concurrencyLevel = 32, int initialCapacity = 31)
            : base(concurrencyLevel, initialCapacity)
        {
        }
        #endregion

        #region IReadOnlyDictionary implementation

        /// <summary>
        /// Enumerator implementation for IReadOnlyDictionary
        /// </summary>
        IEnumerable<TKey> IReadOnlyDictionary<TKey, TValue>.Keys
        {
            get
            {
                foreach (var k in Keys)
                {
                    yield return k;
                }
            }
        }

        /// <summary>
        /// Enumerator implementation for IReadOnlyDictionary
        /// </summary>
        IEnumerable<TValue> IReadOnlyDictionary<TKey, TValue>.Values
        {
            get
            {
                foreach (var v in Values)
                {
                    yield return v;
                }
            }
        }
        #endregion
    }
}
