// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics;
using System.Diagnostics.ContractsLight;
using System.Runtime.CompilerServices;
using BuildXL.FrontEnd.Script.Literals;
using JetBrains.Annotations;

namespace BuildXL.FrontEnd.Script.Values
{
    /// <summary>
    /// Result of a node evaluation.
    /// </summary>
    /// <remarks>
    /// The struct holds an object that was obtained during evaluation and the kind.
    /// In the future this instance will be used as argument for evaluating other values that will prevent
    /// boxing allocations for value-type results like AbsolutePath, PathAtom etc.
    /// </remarks>
    [DebuggerDisplay("{ToDebugString(),nq}")]
    public readonly struct EvaluationResult : IEquatable<EvaluationResult>
    {
        /// <summary>
        /// Result of the evaluation.
        /// </summary>
        [NotNull]
        public readonly object Value;

        /// <nodoc />
        public bool IsValid => Value != null;

        /// <nodoc />
        public EvaluationResult([NotNull] object value)
        {
#if DEBUG
            Contract.Requires(value != null);
            Contract.Requires(!(value is EvaluationResult));
#endif
            Value = value;
        }

        /// <nodoc />
        public static EvaluationResult Error { get; } = new EvaluationResult(ErrorValue.Instance);

        /// <summary>
        /// Evaluation result returned when evaluation is canceled.  Currently indistinguishable from <see cref="Error"/>.
        /// </summary>
        public static EvaluationResult Canceled { get; } = Error;

        /// <nodoc />
        public static EvaluationResult Undefined { get; } = new EvaluationResult(UndefinedValue.Instance);

        /// <nodoc />
        public static EvaluationResult Continue { get; } = new EvaluationResult(ContinueValue.Instance);

        /// <nodoc />
        public static EvaluationResult Break { get; } = new EvaluationResult(BreakValue.Instance);

        /// <nodoc />
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static EvaluationResult Create([NotNull]string result) => new EvaluationResult(result);

        /// <nodoc />
        public static EvaluationResult Create([NotNull]object result) => new EvaluationResult(result);

        /// <nodoc />
        public static EvaluationResult Create(int result) => Create(BoxedNumber.Box(result));

        /// <nodoc />
        public static EvaluationResult Create(bool result) => result ? True : False;

        /// <nodoc />
        public bool IsErrorValue => Value.IsErrorValue();

        /// <nodoc />
        public bool IsUndefined => Value == UndefinedValue.Instance;

        /// <nodoc />
        public static EvaluationResult True { get; } = new EvaluationResult(true);

        /// <nodoc />
        public static EvaluationResult False { get; } = new EvaluationResult(false);

        /// <summary>
        /// Returns the type of an undelying value.
        /// </summary>
        public new Type GetType() => Value.GetType();

        /// <inheritdoc />
        public bool Equals(EvaluationResult other)
        {
            if (ReferenceEquals(Value, other.Value))
            {
                return true;
            }

            if (Value == null && other.Value != null)
            {
                return false;
            }

            return Value.Equals(other.Value);
        }

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj))
            {
                return false;
            }

            return obj is EvaluationResult && Equals((EvaluationResult)obj);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return Value.GetHashCode();
        }

        /// <nodoc />
        public static bool operator ==(EvaluationResult left, EvaluationResult right)
        {
            return left.Equals(right);
        }

        /// <nodoc />
        public static bool operator !=(EvaluationResult left, EvaluationResult right)
        {
            return !left.Equals(right);
        }

        private string ToDebugString()
        {
            if (!IsValid)
            {
                return "{invalid}";
            }

            if (IsUndefined)
            {
                return "{undefined}";
            }

            if (IsErrorValue)
            {
                return "{error}";
            }

            if (this == Continue)
            {
                return "{continue}";
            }

            if (this == Break)
            {
                return "{break}";
            }

            return $"{Value.ToString()} ({Value.GetType().Name})";
        }
    }
}
