// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Globalization;

namespace BuildXL.Utilities
{
    /// <summary>
    /// Efficient reference of a qualifier in the QualifierTable.
    /// </summary>
    [DebuggerDisplay("{ToDebuggerDisplay(),nq}")]
    public readonly struct QualifierId : IEquatable<QualifierId>
    {
        private readonly int m_id;

        /// <summary>
        /// The singleton to reference Unqualified values.
        /// </summary>
        public static readonly QualifierId Invalid = new QualifierId(false);

        /// <summary>
        /// The singleton to reference Unqualified values.
        /// </summary>
        public static readonly QualifierId Unqualified = new QualifierId(0);

        /// <summary>
        /// Constructs a qualifier id.
        /// </summary>
        /// <remarks>
        /// The id should always be a representation for an integer that is used to index arrays.
        /// </remarks>
        public QualifierId(int id)
        {
            Contract.Requires(id >= 0);
            m_id = id;
        }

        /// <summary>
        /// Constructor for invalid entry
        /// </summary>
        [SuppressMessage("Microsoft.Usage", "CA1801:ReviewUnusedParameters", Justification = "Struct need a parameterized contructor.")]
        private QualifierId(bool dummy)
        {
            m_id = -1;
        }

        /// <summary>
        /// The id of this qualifier
        /// </summary>
        public int Id
        {
            get
            {
                Contract.Requires(IsValid);
                return m_id;
            }
        }

        /// <inherit />
        public bool Equals(QualifierId other)
        {
            return m_id == other.m_id;
        }

        /// <inherit />
        public override bool Equals(object obj)
        {
            return StructUtilities.Equals(this, obj);
        }

        /// <inherit />
        public override int GetHashCode()
        {
            return m_id;
        }

        /// <summary>
        /// Checks whether two qualifierIDs are the same.
        /// </summary>
        public static bool operator ==(QualifierId left, QualifierId right)
        {
            return left.Equals(right);
        }

        /// <summary>
        /// Checks whether two qualifierIDs are different.
        /// </summary>
        public static bool operator !=(QualifierId left, QualifierId right)
        {
            return !left.Equals(right);
        }

        /// <summary>
        /// Checks if this qualifier id is valid.
        /// </summary>
        public bool IsValid => this != Invalid;

        [SuppressMessage("Microsoft.Performance", "CA1811")]
        private string ToDebuggerDisplay()
        {
            return IsValid ? m_id.ToString(CultureInfo.InvariantCulture) : "Invalid";
        }
    }
}
