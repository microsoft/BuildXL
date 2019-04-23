// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using BuildXL.FrontEnd.Script.Evaluator;
using BuildXL.Utilities;

namespace BuildXL.FrontEnd.Script.Values
{
    /// <summary>
    /// Helper struct that glues together property nameId and value.
    /// </summary>
    public readonly struct NamedValue : IEquatable<NamedValue>
    {
        /// <nodoc />
        public NamedValue(int nameId, EvaluationResult value)
        {
            NameId = nameId;
            Value = value;
        }

        /// <summary>
        /// Helper factory method used by tests.
        /// </summary>
        public static NamedValue Create(int nameId, object value)
        {
            return new NamedValue(nameId, EvaluationResult.Create(value));
        }

        /// <summary>
        /// Property name in a form of string Id value.
        /// </summary>
        public int NameId { get; }

        /// <summary>
        /// Current value.
        /// </summary>
        public EvaluationResult Value { get; }

        /// <summary>
        /// Returns true if current named-value pair is valid.
        /// </summary>
        public bool IsValid => NameId != StringId.Invalid.Value;

        /// <summary>
        /// Returns true when current value is valid and has an error.
        /// </summary>
        public bool IsError => Value.IsErrorValue;

        /// <summary>
        /// Converts current instance to key value pair.
        /// </summary>
        [Pure]
        public KeyValuePair<StringId, EvaluationResult> AsKeyValuePair()
        {
            return new KeyValuePair<StringId, EvaluationResult>(new StringId(NameId), Value);
        }

        /// <summary>
        /// Evals current value and produces new value.
        /// </summary>
        [Pure]
        public NamedValue Eval(Context context, ModuleLiteral env, EvaluationStackFrame args)
        {
            if (!IsValid)
            {
                return this;
            }

            var result = ObjectLiteral.EvalExpression(context, env, Value, args);
            return new NamedValue(NameId, result);
        }

        /// <nodoc />
        public static NamedValue Create(Binding binding)
        {
            return new NamedValue(binding.Name.Value, EvaluationResult.Create(binding.Body));
        }

        /// <nodoc />
        public static NamedValue Invalid { get; } = new NamedValue(StringId.Invalid.Value, EvaluationResult.Undefined);

        /// <inheritdoc/>
        public bool Equals(NamedValue other)
        {
            return NameId == other.NameId && Value == other.Value;
        }

        /// <inheritdoc/>
        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj))
            {
                return false;
            }

            return obj is NamedValue && Equals((NamedValue)obj);
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            return HashCodeHelper.Combine(NameId.GetHashCode(), Value.GetHashCode());
        }

        /// <nodoc />
        public static bool operator ==(NamedValue left, NamedValue right)
        {
            return left.Equals(right);
        }

        /// <nodoc />
        public static bool operator !=(NamedValue left, NamedValue right)
        {
            return !left.Equals(right);
        }
    }
}
