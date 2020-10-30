// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

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
        private readonly HashSet<Type> m_allowedTypes;
        private object m_value;

        /// <nodoc/>
        public DiscriminatingUnion(params Type[] types)
        {
            m_allowedTypes = new HashSet<Type>(types);
        }

        /// <nodoc/>
        public bool TrySetValue(object o)
        {
            // If we find a direct type match, then we are good to go
            Type targetType = o?.GetType();
            if (m_allowedTypes.Contains(targetType))
            {
                m_value = o;
                return true;
            }

            // Otherwise, use an 'enhanced' assignment relationship, potentially lifting the target
            // value to a discriminating union, as if an implicit conversion was defined
            foreach(var allowedType in m_allowedTypes)
            {
                if (IsAssignableFrom(allowedType, targetType, o, out object liftedObject))
                {
                    m_value = liftedObject;
                    return true;
                }
            }

            return false;
        }

        private bool IsAssignableFrom(Type allowedType, Type targetType, object targetValue, out object liftedTargetValue)
        {
            liftedTargetValue = targetValue;
            // If the allowed type is a discriminating union itself and the target value is not, let's see if we can implicitly
            // convert it
            if (allowedType.IsSubclassOf(typeof(DiscriminatingUnion)) && !(targetValue is DiscriminatingUnion))
            {
                var union = Activator.CreateInstance(allowedType) as DiscriminatingUnion;
                if (union.TrySetValue(targetValue))
                {
                    liftedTargetValue = union;
                    return true;
                }
            }
            else
            {
                return allowedType.IsAssignableFrom(targetType);
            }

            return false;
        }

        /// <nodoc/>
        public object GetValue()
        {
            return m_value;
        }

        /// <nodoc/>
        public IEnumerable<Type> GetAllowedTypes()
        {
            return m_allowedTypes;
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

    /// <summary>
    /// A specialization of <see cref="DiscriminatingUnion"/> for the case of three disjuncts
    /// </summary>
    public sealed class DiscriminatingUnion<T, Q, R> : DiscriminatingUnion
    {
        /// <nodoc/>
        public DiscriminatingUnion() : base(typeof(T), typeof(Q), typeof(R))
        { }

        /// <nodoc/>
        public DiscriminatingUnion(T value) : base(typeof(T), typeof(Q), typeof(R))
        {
            TrySetValue(value);
        }

        /// <nodoc/>
        public DiscriminatingUnion(Q value) : base(typeof(T), typeof(Q), typeof(R))
        {
            TrySetValue(value);
        }

        /// <nodoc/>
        public DiscriminatingUnion(R value) : base(typeof(T), typeof(Q), typeof(R))
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

        /// <nodoc/>
        public void SetValue(R value)
        {
            TrySetValue(value);
        }
    }

    /// <summary>
    /// A specialization of <see cref="DiscriminatingUnion"/> for the case of four disjuncts
    /// </summary>
    public sealed class DiscriminatingUnion<T, Q, R, S> : DiscriminatingUnion
    {
        /// <nodoc/>
        public DiscriminatingUnion() : base(typeof(T), typeof(Q), typeof(R), typeof(S))
        { }

        /// <nodoc/>
        public DiscriminatingUnion(T value) : base(typeof(T), typeof(Q), typeof(R), typeof(S))
        {
            TrySetValue(value);
        }

        /// <nodoc/>
        public DiscriminatingUnion(Q value) : base(typeof(T), typeof(Q), typeof(R), typeof(S))
        {
            TrySetValue(value);
        }

        /// <nodoc/>
        public DiscriminatingUnion(R value) : base(typeof(T), typeof(Q), typeof(R), typeof(S))
        {
            TrySetValue(value);
        }

        /// <nodoc/>
        public DiscriminatingUnion(S value) : base(typeof(T), typeof(Q), typeof(R), typeof(S))
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

        /// <nodoc/>
        public void SetValue(R value)
        {
            TrySetValue(value);
        }

        /// <nodoc/>
        public void SetValue(S value)
        {
            TrySetValue(value);
        }
    }
}
