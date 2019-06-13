// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;

namespace BuildXL.Utilities.Configuration
{
    /// <summary>
    /// Represents a discriminating union type from TypeScript (the usual T1 | T2 | ... | Tn construct)
    /// </summary>
    /// <remarks>
    /// Useful for declaring C# backed-values that map to a union. Works together with the configuration converter.
    /// </remarks>
    public abstract class DiscriminatingUnion
    {
        private HashSet<Type> m_allowedTypes;
        private object m_value;

        /// <nodoc/>
        public DiscriminatingUnion(params Type[] types)
        {
            m_allowedTypes = new HashSet<Type>(types);
        }

        /// <nodoc/>
        public bool TrySetValue(object o)
        {
            if (m_allowedTypes.Contains(o.GetType()))
            {
                m_value = o;
                return true;
            }
            return false;
        }

        /// <nodoc/>
        public object GetValue()
        {
            return m_value;
        }
    }

    /// <summary>
    /// A specialization of <see cref="DiscriminatingUnion"/> for the case of two disjuncts
    /// </summary>
    public sealed class DiscriminatingUnion<T, Q> : DiscriminatingUnion
    {
        /// <nodoc/>
        public DiscriminatingUnion() : base(typeof(T), typeof(Q))
        { }

        /// <nodoc/>
        public DiscriminatingUnion(T value) : base(typeof(T), typeof(Q))
        {
            TrySetValue(value);
        }

        /// <nodoc/>
        public DiscriminatingUnion(Q value) : base(typeof(T), typeof(Q))
        {
            TrySetValue(value);
        }

        /// <nodoc/>
        public void SetValue(T value)
        {
            TrySetValue(value);
        }

        /// <nodoc/>
        public void SetValue(Q value)
        {
            TrySetValue(value);
        }
    }
}
