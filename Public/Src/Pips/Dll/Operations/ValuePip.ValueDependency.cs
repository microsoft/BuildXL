// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using BuildXL.Utilities;

namespace BuildXL.Pips.Operations
{
    public partial class ValuePip
    {
        /// <summary>
        /// A value to value dependency
        /// </summary>
        public readonly struct ValueDependency : IEquatable<ValueDependency>
        {
            /// <summary>
            /// Parent value's identifier
            /// </summary>
            public readonly FullSymbol ParentIdentifier;

            /// <summary>
            /// Parent value's qualifier
            /// </summary>
            public readonly QualifierId ParentQualifier;

            /// <summary>
            /// Parent value's location
            /// </summary>
            public readonly LocationData ParentLocation;

            /// <summary>
            /// Child value's identifier
            /// </summary>
            public readonly FullSymbol ChildIdentifier;

            /// <summary>
            /// Child value's qualifier
            /// </summary>
            public readonly QualifierId ChildQualifier;

            /// <summary>
            /// Child value's location
            /// </summary>
            public readonly LocationData ChildLocation;

            /// <summary>
            /// Creates a ValueDependency
            /// </summary>
            public ValueDependency(
                FullSymbol parentIdentifier,
                QualifierId parentQualifier,
                LocationData parentLocation,
                FullSymbol childIdentifier,
                QualifierId childQualifier,
                LocationData childLocation)
            {
                ParentIdentifier = parentIdentifier;
                ParentQualifier = parentQualifier;
                ChildIdentifier = childIdentifier;
                ChildQualifier = childQualifier;
                ParentLocation = parentLocation;
                ChildLocation = childLocation;
            }

            #region IEquatable<ValueDependency> implementation

            /// <inheritdoc />
            public override int GetHashCode()
            {
                return HashCodeHelper.Combine(
                    ParentIdentifier.GetHashCode(),
                    ParentQualifier.GetHashCode(),
                    ChildIdentifier.GetHashCode(),
                    ChildQualifier.GetHashCode());
            }

            /// <inheritdoc />
            public override bool Equals(object obj)
            {
                return StructUtilities.Equals(this, obj);
            }

            /// <summary>
            /// Indicates whether two object instances are equal.
            /// </summary>
            public static bool operator ==(ValueDependency left, ValueDependency right)
            {
                return left.Equals(right);
            }

            /// <summary>
            /// Indicates whether two objects instances are not equal.
            /// </summary>
            public static bool operator !=(ValueDependency left, ValueDependency right)
            {
                return !left.Equals(right);
            }

            /// <summary>
            /// Whether a ValueDependency equals this one
            /// </summary>
            public bool Equals(ValueDependency other)
            {
                return ParentIdentifier == other.ParentIdentifier &&
                       ParentQualifier == other.ParentQualifier &&
                       ChildIdentifier == other.ChildIdentifier &&
                       ChildQualifier == other.ChildQualifier;
            }

            #endregion
        }
    }
}
