// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;

namespace BuildXL.Utilities.Qualifier
{
    /// <summary>
    /// Efficient reference of a qualifier in the QualifierTable.
    /// </summary>
    public readonly struct QualifierSpaceId : IEquatable<QualifierSpaceId>
    {
        /// <summary>
        /// Invalid qualifier space id.
        /// </summary>
        public static readonly QualifierSpaceId Invalid = new QualifierSpaceId(-1);

        /// <summary>
        /// Internal id.
        /// </summary>
        public readonly int Id;

        /// <summary>
        /// Constructor.
        /// </summary>
        public QualifierSpaceId(int id)
        {
            Id = id;
        }

        /// <inherit />
        public bool Equals(QualifierSpaceId other)
        {
            return Id == other.Id;
        }

        /// <inherit />
        public override bool Equals(object obj)
        {
            return StructUtilities.Equals(this, obj);
        }

        /// <inherit />
        public override int GetHashCode()
        {
            return Id;
        }

        /// <summary>
        /// Checks whether two qualifier space id's are equal.
        /// </summary>
        public static bool operator ==(QualifierSpaceId left, QualifierSpaceId right)
        {
            return left.Equals(right);
        }

        /// <summary>
        /// Checks whether two qualifier space id's are not equal.
        /// </summary>
        public static bool operator !=(QualifierSpaceId left, QualifierSpaceId right)
        {
            return !left.Equals(right);
        }

        /// <summary>
        /// Checks if this qualifier space id is valid.
        /// </summary>
        public bool IsValid => this != Invalid;
    }
}
