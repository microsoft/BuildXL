// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;

namespace BuildXL.Utilities.PackedTable
{
    /// <summary>
    /// Interface implemented by ID types, to enable generic conversion to and from int.
    /// </summary>
    /// <remarks>
    /// Theoretically, thanks to generic specialization, these methods should be callable with zero overhead
    /// (no boxing, no virtual calls).
    /// 
    /// Note that ID values are 1-based; IDs range from [1 .. Count] inclusive, rather than [0 .. Count-1] inclusive
    /// as with zero-based indices. This is deliberate, to allow the default ID value to indicate "no ID" (and
    /// to catch bugs relating to uninitialized IDs).
    /// 
    /// Note that a subtlety of struct types implementing interfaces is that the default Object methods, especially
    /// Equals, behave differently. Code style warnings at one point advised implementing Object.Equals on these
    /// struct ID types. Unfortunately it turned out this breaks this assertion:
    /// 
    /// SomeId defaultSomeId = default(SomeId);
    /// Assert.True(defaultSomeId.Equals(default));
    /// 
    /// If SomeId is a struct type, this will wind up comparing default(SomeId) to default(object), in other
    /// words comparing a struct to null. Which is always false, whereas this comparison was expected to return
    /// true. 
    /// 
    /// Note that this is fine:
    /// 
    /// Assert.True(defaultSomeId.Equals(default(SomeId)));
    /// 
    /// But the pit of failure above from just using "default" is real (we fell into it).
    /// 
    /// So we deliberately do not implement Object.Equals (or Object.GetHashCode) on struct types implementing
    /// this interface. Instead, all ID types define IEqualityComparer on themselves; this turns out to be much
    /// more convenient for comparing instances in generic code. It doesn't prevent the user from writing
    /// "SomeId.Equals(default)" but at least it provides a better pattern instead.
    /// </remarks>
    public interface Id<TId> : IComparable<TId>
        where TId : unmanaged
    {
        /// <summary>
        /// The underlying 1-based integer value.
        /// </summary>
        /// <remarks>
        /// To convert into an array index, you must subtract 1.
        /// </remarks>
        public int Value { get; }

        /// <summary>
        /// Convert a 1-based integer value to this type of ID.
        /// </summary>
        /// <remarks>
        /// This is a bit roundabout but is a generic way of constructing the appropriate kind of ID struct.
        /// Note that the argument here must be 1-based; this method does not modify the value.
        /// </remarks>
        public TId CreateFrom(int value);

        /// <summary>
        /// Get a comparer usable on this type of ID.
        /// </summary>
        /// <remarks>
        /// Best not to call this in a tight loop inn case the result struct is boxed.
        /// </remarks>
        public IEqualityComparer<TId> Comparer { get; }

        /// <summary>
        /// Check that the value is not zero; throw ArgumentException if it is.
        /// </summary>
        public static void CheckValidId(int value) { if (value < 1) { throw new ArgumentException("Cannot create ID with zero or negative value (use default instead)"); } }
    }
}
