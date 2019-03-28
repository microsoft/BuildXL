// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using BuildXL.Utilities;
using static BuildXL.Utilities.FormattableStringEx;

namespace BuildXL.Native.IO
{
    /// <summary>
    /// Structure for file version.
    /// </summary>
    /// <remarks>
    /// File version can be implemented differently in different OSes. For Windows,
    /// the version is the NTFS USN, and this structure is simply a cursor to the file system
    /// change journal. For Unix, this structure can represent file timestamp. The requirement is
    /// the file version should be totally ordered, and is monotonically increasing.
    /// </remarks>
    public readonly struct Usn : IEquatable<Usn>, IComparable<Usn>
    {
        /// <summary>
        /// Version value.
        /// </summary>
        /// <remarks>
        /// For NTFS change journal, this value represents journal offsets, which are totally ordered within a volume.
        /// </remarks>
        public readonly ulong Value;

        /// <summary>
        /// Zero USN.
        /// </summary>
        public static readonly Usn Zero = new Usn(0);

        /// <nodoc />
        public Usn(ulong value)
        {
            Value = value;
        }

        /// <summary>
        /// Indicates if this is the lowest representable USN (0) == <c>default(Usn)</c>.
        /// </summary>
        /// <remarks>
        /// For NTFS change journal, the zero USN is special in that all files claim that USN if the volume's journal is disabled
        /// (or if they have not been modified since the journal being enabled).
        /// </remarks>
        public bool IsZero => Value == 0;

        /// <nodoc />
        public bool Equals(Usn other)
        {
            return Value == other.Value;
        }

        /// <nodoc />
        public override bool Equals(object obj)
        {
            return StructUtilities.Equals(this, obj);
        }

        /// <nodoc />
        public override int GetHashCode()
        {
            return Value.GetHashCode();
        }

        /// <nodoc />
        public static bool operator ==(Usn left, Usn right)
        {
            return left.Equals(right);
        }

        /// <nodoc />
        public static bool operator !=(Usn left, Usn right)
        {
            return !left.Equals(right);
        }

        /// <nodoc />
        public static bool operator <(Usn left, Usn right)
        {
            return left.Value < right.Value;
        }

        /// <nodoc />
        public static bool operator >(Usn left, Usn right)
        {
            return left.Value > right.Value;
        }

        /// <nodoc />
        public static bool operator <=(Usn left, Usn right)
        {
            return left.Value <= right.Value;
        }

        /// <nodoc />
        public static bool operator >=(Usn left, Usn right)
        {
            return left.Value >= right.Value;
        }

        /// <nodoc />
        public int CompareTo(Usn other)
        {
            return Value.CompareTo(other.Value);
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return I($"{{ USN {Value:x} }}");
        }
    }
}
