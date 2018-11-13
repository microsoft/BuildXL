// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;

namespace BuildXL.Utilities
{
    /// <summary>
    /// An interval in a total order that can have open, closed or unbounded bounds
    /// </summary>
    public readonly struct Interval<T> : IEquatable<Interval<T>> where T : IComparable<T>
    {
        /// <nodoc/>
        public T LowerBound { get; }

        /// <nodoc/>
        public T UpperBound { get; }

        /// <nodoc/>
        public IntervalBoundType LowerBoundType { get; }

        /// <nodoc/>
        public IntervalBoundType UpperBoundType { get; }

        private Interval(T lowerBound, T upperBound, IntervalBoundType lowerBoundType, IntervalBoundType upperBoundType)
        {
            LowerBound = lowerBound;
            UpperBound = upperBound;
            LowerBoundType = lowerBoundType;
            UpperBoundType = upperBoundType;
        }

        /// <nodoc/>
        public static Interval<T> CreateInterval(T lowerBound, T upperBound, IntervalBoundType lowerBoundType = IntervalBoundType.Closed,
            IntervalBoundType upperBoundType = IntervalBoundType.Closed)
        {
            Contract.Requires(!(lowerBoundType == IntervalBoundType.Unbounded && upperBoundType == IntervalBoundType.Unbounded) || lowerBound.CompareTo(upperBound) <= 0);

            return new Interval<T>(lowerBound, upperBound, lowerBoundType, upperBoundType);
        }

        /// <summary>
        /// Creates an interval of the form {T, +infinite}
        /// </summary>
        public static Interval<T> CreateIntervalWithNoUpperBound(T lowerBound, IntervalBoundType lowerBoundBoundType = IntervalBoundType.Closed)
        {
            return new Interval<T>(lowerBound, default(T), lowerBoundBoundType, IntervalBoundType.Unbounded);
        }

        /// <summary>
        /// Creates an interval of the form {-infinite, T}
        /// </summary>
        public static Interval<T> CreateIntervalWithNoLowerBound(T upperBound, IntervalBoundType upperBoundBoundType = IntervalBoundType.Closed)
        {
            return new Interval<T>(default(T), upperBound, IntervalBoundType.Unbounded, upperBoundBoundType);
        }

        /// <summary>
        /// Whether an element is contained in the interval
        /// </summary>
        public bool Contains(T element)
        {
            var satisfyLowerConstraint = (LowerBoundType == IntervalBoundType.Unbounded) ||
                                         (LowerBoundType == IntervalBoundType.Closed
                                             ? LowerBound.CompareTo(element) <= 0
                                             : LowerBound.CompareTo(element) < 0);

            var satisfyUpperConstraint = (UpperBoundType == IntervalBoundType.Unbounded) ||
                                         (UpperBoundType == IntervalBoundType.Closed
                                             ? element.CompareTo(UpperBound) <= 0
                                             : element.CompareTo(UpperBound) < 0);

            return satisfyLowerConstraint && satisfyUpperConstraint;
        }

        /// <nodoc/>
        public override bool Equals(object obj)
        {
            if (!(obj is Interval<T>))
            {
                return false;
            }

            return Equals((Interval<T>)obj);
        }

        /// <nodoc/>
        public bool Equals(Interval<T> interval)
        {
            return LowerBound.Equals(interval.LowerBound) &&
                   UpperBound.Equals(interval.UpperBound) &&
                   LowerBoundType == interval.LowerBoundType &&
                   UpperBoundType == interval.UpperBoundType;
        }

        /// <nodoc/>
        public override int GetHashCode()
        {
            return HashCodeHelper.Combine(LowerBound.GetHashCode(), UpperBound.GetHashCode(), LowerBoundType.GetHashCode(), UpperBoundType.GetHashCode());
        }

        /// <nodoc/>
        public static bool operator ==(Interval<T> interval1, Interval<T> interval2)
        {
            return interval1.Equals(interval2);
        }

        /// <nodoc/>
        public static bool operator !=(Interval<T> interval1, Interval<T> interval2)
        {
            return !interval1.Equals(interval2);
        }
    }

    /// <summary>
    /// An interval bound can be open, closed or unbounded.
    /// </summary>
    public enum IntervalBoundType
    {
        /// <nodoc/>
        Unbounded,

        /// <nodoc/>
        Open,

        /// <nodoc/>
        Closed,
    }
}
