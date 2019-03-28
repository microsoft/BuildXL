// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using BuildXL.Utilities;
using static BuildXL.Utilities.FormattableStringEx;

namespace BuildXL.FrontEnd.Script.Values
{
    /// <summary>
    /// Qualified Module Id used as a key in many storages.
    /// </summary>
    public readonly struct QualifiedModuleId : IEquatable<QualifiedModuleId>
    {
        private readonly int m_hashCode;

        /// <summary>
        /// Module id.
        /// </summary>
        public ModuleLiteralId Id { get; }

        /// <summary>
        /// Qualifier.
        /// </summary>
        public QualifierId QualifierId { get; }

        /// <summary>
        /// Invalid module key.
        /// </summary>
        public static QualifiedModuleId Invalid { get; } = new QualifiedModuleId(ModuleLiteralId.Invalid, QualifierId.Invalid);

        /// <nodoc />
        private QualifiedModuleId(ModuleLiteralId id, QualifierId qualifier)
        {
            Id = id;
            QualifierId = qualifier;
            m_hashCode = HashCodeHelper.Combine(Id.GetHashCode(), QualifierId.GetHashCode());
        }

        /// <summary>
        /// Creates a module key.
        /// </summary>
        public static QualifiedModuleId Create(ModuleLiteralId id, QualifierId qualifierId)
        {
            Contract.Requires(id.IsValid);
            Contract.Requires(qualifierId.IsValid);

            return new QualifiedModuleId(id, qualifierId);
        }

        /// <summary>
        /// Checks if this package id is valid.
        /// </summary>
        public bool IsValid => this != Invalid;

        /// <inheritdoc />
        public bool Equals(QualifiedModuleId other)
        {
            return Id == other.Id && QualifierId == other.QualifierId;
        }

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            return StructUtilities.Equals(this, obj);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return m_hashCode;
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return I($"{Id}[{QualifierId}]");
        }

        /// <summary>
        /// Equality operator.
        /// </summary>
        public static bool operator ==(QualifiedModuleId left, QualifiedModuleId right)
        {
            return left.Equals(right);
        }

        /// <summary>
        /// Inequality operator.
        /// </summary>
        public static bool operator !=(QualifiedModuleId left, QualifiedModuleId right)
        {
            return !left.Equals(right);
        }
    }
}
