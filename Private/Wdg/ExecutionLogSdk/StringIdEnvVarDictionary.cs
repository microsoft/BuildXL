// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections;
using System.Collections.Generic;
using BuildXL.Pips.Operations;
using BuildXL.Utilities;

namespace Tool.ExecutionLogSdk
{
    /// <summary>
    /// This class functions as a wrapper for a Dictionary where the
    /// key type is hardcoded to StringId and the value type is EnvironmentVariable.
    /// </summary>
    /// <remarks>
    /// This class can be exposed as a ReadOnlyDictionary where both the key
    /// and value are string type. This allows us to internally store the
    /// strings in a more memory efficient way but still be able to expose them
    /// to the user as strings.
    /// </remarks>
    /// <typeparam name="TValue">The type of value to store in the dictionary</typeparam>
    internal sealed class StringIdEnvVarDictionary : IReadOnlyDictionary<string, string>, IDictionary<string, string>
    {
        #region Private properties

        /// <summary>
        /// The underlying dictionary that stores the StringIds and their respective EnvironmentVariables
        /// </summary>
        private Dictionary<StringId, EnvironmentVariable> m_dictionary;

        /// <summary>
        /// The PipExecutionContext instance used to convert between various ids and strings
        /// </summary>
        private PipExecutionContext m_context;
        #endregion

        #region Constructor

        /// <summary>
        /// Initializes dictionary and sets the PipExecutionContext instance to use
        /// </summary>
        /// <param name="context">Used to convert between several types of ids and strings</param>
        /// <param name="initialCapacity">Initial capacity of the collection; default value is 31.</param>
        public StringIdEnvVarDictionary(PipExecutionContext context, int initialCapacity = 31)
        {
            m_dictionary = new Dictionary<StringId, EnvironmentVariable>(initialCapacity);
            m_context = context;
        }
        #endregion

        #region Private methods
        private string EnvironmentVariableToString(EnvironmentVariable e)
        {
            if (e.IsPassThrough)
            {
                return string.Empty;
            }
            else if (e.Value.IsValid && (e.Value.FragmentCount > 0))
            {
                string value = string.Empty;
                string fragmentSeparator = (e.Value.FragmentCount > 1) ? m_context.StringTable.GetString(e.Value.FragmentSeparator) : string.Empty;

                foreach (var fragment in e.Value)
                {
                    if (fragment.IsValid)
                    {
                        if (fragment.FragmentType == PipFragmentType.StringLiteral)
                        {
                            value = value + m_context.StringTable.IdToString(fragment.GetStringIdValue()) + fragmentSeparator;
                        }
                        else if (fragment.FragmentType == PipFragmentType.AbsolutePath)
                        {
                            value = value + m_context.PathTable.AbsolutePathToString(fragment.GetPathValue()) + fragmentSeparator;
                        }
                    }
                }

                return value;
            }
            else
            {
                // The logic for what gets stored in this dictionary is such
                // that only EnvironmentVariables that fit certain criteria
                // should ever be stored in here. If we ever come across one
                // that doesn't fit that criteria, we throw an exception.
                throw new ArgumentException("EnvironmentVariable could not be converted to string. It should never have been stored in the StringIdEnvVarDictionary.");
            }
        }
        #endregion

        #region Internal methods
        internal string this[StringId key]
        {
            get
            {
                return EnvironmentVariableToString(m_dictionary[key]);
            }
        }

        internal string this[string key]
        {
            get
            {
                return this[m_context.StringTable.StringToId(key)];
            }
        }

        internal void Update(StringId stringId, EnvironmentVariable environmentVariable)
        {
            m_dictionary[stringId] = environmentVariable;
        }

        internal void Add(StringId stringId, EnvironmentVariable environmentVariable)
        {
            m_dictionary.Add(stringId, environmentVariable);
        }
        #endregion

        #region Public and Interface methods
        string IReadOnlyDictionary<string, string>.this[string key]
        {
            get
            {
                return this[m_context.StringTable.StringToId(key)];
            }
        }

        string IDictionary<string, string>.this[string key]
        {
            get
            {
                return this[m_context.StringTable.StringToId(key)];
            }

            set
            {
                throw new NotImplementedException();
            }
        }

        IEnumerable<string> IReadOnlyDictionary<string, string>.Keys
        {
            get
            {
                foreach (var k in m_dictionary.Keys)
                {
                    yield return m_context.StringTable.IdToString(k);
                }
            }
        }

        IEnumerable<string> IReadOnlyDictionary<string, string>.Values
        {
            get
            {
                foreach (var v in m_dictionary.Values)
                {
                    yield return EnvironmentVariableToString(v);
                }
            }
        }

        public int Count => m_dictionary.Count;

        public ICollection<string> Keys
        {
            get
            {
                IList<string> keys = new List<string>();
                foreach (var k in m_dictionary.Keys)
                {
                    keys.Add(m_context.StringTable.IdToString(k));
                }

                return keys;
            }
        }

        public ICollection<string> Values
        {
            get
            {
                IList<string> values = new List<string>();
                foreach (var v in m_dictionary.Values)
                {
                    values.Add(EnvironmentVariableToString(v));
                }

                return values;
            }
        }

        public bool IsReadOnly => true;

        public bool ContainsKey(StringId key)
        {
            return m_dictionary.ContainsKey(key);
        }

        public bool ContainsKey(string key)
        {
            return ContainsKey(m_context.StringTable.StringToId(key));
        }

        bool IDictionary<string, string>.ContainsKey(string key)
        {
            return ContainsKey(m_context.StringTable.StringToId(key));
        }

        public bool Contains(KeyValuePair<string, string> item)
        {
            StringId keyStringId = m_context.StringTable.StringToId(item.Key);
            if (ContainsKey(keyStringId))
            {
                if (this[keyStringId].Equals(item.Value))
                {
                    return true;
                }
            }

            return false;
        }

        public bool TryGetValue(StringId key, out string value)
        {
            EnvironmentVariable e;
            if (m_dictionary.TryGetValue(key, out e))
            {
                value = EnvironmentVariableToString(e);
                return true;
            }
            else
            {
                value = null;
                return false;
            }
        }

        public bool TryGetValue(string key, out string value)
        {
            return TryGetValue(m_context.StringTable.StringToId(key), out value);
        }

        bool IDictionary<string, string>.TryGetValue(string key, out string value)
        {
            return TryGetValue(m_context.StringTable.StringToId(key), out value);
        }

        public IEnumerator<KeyValuePair<string, string>> GetEnumerator()
        {
            foreach (var kv in m_dictionary)
            {
                yield return new KeyValuePair<string, string>(m_context.StringTable.IdToString(kv.Key), EnvironmentVariableToString(kv.Value));
            }
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        public void CopyTo(KeyValuePair<string, string>[] array, int arrayIndex)
        {
            if (array == null)
            {
                throw new ArgumentNullException("array");
            }

            if (arrayIndex < 0 || arrayIndex > array.Length)
            {
                throw new ArgumentOutOfRangeException("arrayIndex");
            }

            if ((array.Length - arrayIndex) < m_dictionary.Count)
            {
                throw new ArgumentException("Destination array is not large enough. Check array.Length and arrayIndex.");
            }

            foreach (KeyValuePair<StringId, EnvironmentVariable> kv in m_dictionary)
            {
                array[arrayIndex++] = new KeyValuePair<string, string>(m_context.StringTable.IdToString(kv.Key), EnvironmentVariableToString(kv.Value));
            }
        }
        #endregion

        #region Unimplemented Interface Methods
        public void Add(string key, string value)
        {
            throw new NotImplementedException();
        }

        public bool Remove(string key)
        {
            throw new NotImplementedException();
        }

        public void Add(KeyValuePair<string, string> item)
        {
            throw new NotImplementedException();
        }

        public void Clear()
        {
            throw new NotImplementedException();
        }

        public bool Remove(KeyValuePair<string, string> item)
        {
            throw new NotImplementedException();
        }
        #endregion
    }
}
