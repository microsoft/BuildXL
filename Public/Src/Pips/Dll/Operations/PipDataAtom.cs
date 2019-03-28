// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using BuildXL.Utilities;

namespace BuildXL.Pips.Operations
{
    /// <summary>
    /// Represents a snippet of dynamic data for execution.
    /// </summary>
    /// <remarks>
    /// This can contains paths or string literals or string ids.
    /// The main purpose of this is to have implicit conversions
    /// </remarks>
    public readonly struct PipDataAtom : IEquatable<PipDataAtom>
    {
        private readonly string m_stringValue;
        private readonly int m_idValue;

        /// <summary>
        /// Exposes the type of this Fragment so consumers can choose which data to extract.
        /// </summary>
        public PipDataAtomType DataType { get; }

        /// <summary>
        /// Private constructor, please use CreateSourceFile.... overloads to instantiate this type.
        /// </summary>
        private PipDataAtom(StringId value)
        {
            m_stringValue = null;
            m_idValue = value.Value;
            DataType = PipDataAtomType.StringId;
        }

        /// <summary>
        /// Private constructor, please use CreateSourceFile.... overloads to instantiate this type.
        /// </summary>
        private PipDataAtom(string value)
        {
            m_stringValue = value;
            m_idValue = 0;
            DataType = PipDataAtomType.String;
        }

        /// <summary>
        /// Private constructor, please use CreateSourceFile.... overloads to instantiate this type.
        /// </summary>
        private PipDataAtom(AbsolutePath value)
        {
            m_stringValue = null;
            m_idValue = value.Value.Value;
            DataType = PipDataAtomType.AbsolutePath;
        }

        /// <summary>
        /// Whether this pip fragment is valid (and not the default value)
        /// </summary>
        public bool IsValid => DataType != PipDataAtomType.Invalid;

        #region Conversions

        /// <summary>
        /// Returns the current value as AbsolulePath
        /// </summary>
        /// <remarks>
        /// You can only call this function for instances where Type is equal to PipDataFragmentType.AbsolutePath.
        /// </remarks>
        /// <returns>Value as a FileArtifact</returns>
        public AbsolutePath GetPathValue()
        {
            Contract.Requires(DataType == PipDataAtomType.AbsolutePath);
            return new AbsolutePath(m_idValue);
        }

        /// <summary>
        /// Returns the current value as a string id
        /// </summary>
        /// <remarks>
        /// You can only call this function for instances where Type is equal to PipDataFragmentType.StringLiteral.
        /// </remarks>
        /// <returns>Value as string</returns>
        public StringId GetStringIdValue(StringTable stringTable)
        {
            Contract.Requires(DataType == PipDataAtomType.String || DataType == PipDataAtomType.StringId);
            return DataType == PipDataAtomType.String ? StringId.Create(stringTable, m_stringValue) : new StringId(m_idValue);
        }

        /// <summary>
        /// Implicitly convert a string to a string literal PipDataAtom.
        /// </summary>
        public static implicit operator PipDataAtom(string value)
        {
            Contract.Requires(value != null);
            return new PipDataAtom(value);
        }

        /// <summary>
        /// Implicitly convert a string id to a string id PipDataAtom.
        /// </summary>
        public static implicit operator PipDataAtom(StringId value)
        {
            Contract.Requires(value.IsValid);
            return new PipDataAtom(value);
        }

        /// <summary>
        /// Implicitly convert a file artifact to a path PipDataAtom.
        /// </summary>
        public static implicit operator PipDataAtom(FileArtifact inputFile)
        {
            return FromAbsolutePath(inputFile);
        }

        /// <summary>
        /// Implicitly convert a path to a path PipDataAtom.
        /// </summary>
        public static implicit operator PipDataAtom(AbsolutePath path)
        {
            return FromAbsolutePath(path);
        }

        /// <summary>
        /// Implicitly convert a path atom to a path PipDataAtom.
        /// </summary>
        public static implicit operator PipDataAtom(PathAtom pathAtom)
        {
            Contract.Requires(pathAtom.IsValid);
            return new PipDataAtom(pathAtom.StringId);
        }

        /// <summary>
        /// Convert a string to a string literal PipDataAtom.
        /// </summary>
        public static PipDataAtom FromString(string value)
        {
            Contract.Requires(value != null);
            return new PipDataAtom(value);
        }

        /// <summary>
        /// Convert a string id to a string id PipDataAtom.
        /// </summary>
        public static PipDataAtom FromStringId(StringId value)
        {
            Contract.Requires(value.IsValid);
            return new PipDataAtom(value);
        }

        /// <summary>
        /// Convert a path to a path PipDataAtom.
        /// </summary>
        public static PipDataAtom FromAbsolutePath(AbsolutePath path)
        {
            Contract.Requires(path.IsValid);
            return new PipDataAtom(path);
        }

        #endregion

        #region IEquatable<PipDataAtom> implementation

        /// <summary>
        /// Determines whether the specified object is equal to the current object.
        /// </summary>
        /// <returns>
        /// true if the specified object  is equal to the current object; otherwise, false.
        /// </returns>
        /// <param name="obj">The object to compare with the current object. </param>
        /// <filterpriority>2</filterpriority>
        public override bool Equals(object obj)
        {
            return StructUtilities.Equals(this, obj);
        }

        /// <summary>
        /// Indicates whether the current object is equal to another object of the same type.
        /// </summary>
        /// <returns>
        /// true if the current object is equal to the <paramref name="other" /> parameter; otherwise, false.
        /// </returns>
        /// <param name="other">An object to compare with this object.</param>
        /// <filterpriority>2</filterpriority>
        public bool Equals(PipDataAtom other)
        {
            return DataType == other.DataType &&
                   m_stringValue == other.m_stringValue &&
                   m_idValue == other.m_idValue;
        }

        /// <summary>
        /// Serves as a hash function for a particular type.
        /// </summary>
        /// <returns>
        /// A hash code for the current object.
        /// </returns>
        /// <filterpriority>2</filterpriority>
        public override int GetHashCode()
        {
            return (int)DataType ^ (m_stringValue == null ? 0 : m_stringValue.GetHashCode()) ^ m_idValue.GetHashCode();
        }

        /// <summary>
        /// Indicates whether two object instances are equal.
        /// </summary>
        /// <returns>
        /// true if the values of <paramref name="left" /> and <paramref name="right" /> are equal; otherwise, false.
        /// </returns>
        /// <param name="left">The first object to compare. </param>
        /// <param name="right">The second object to compare. </param>
        /// <filterpriority>3</filterpriority>
        public static bool operator ==(PipDataAtom left, PipDataAtom right)
        {
            return left.Equals(right);
        }

        /// <summary>
        /// Indicates whether two objects instances are not equal.
        /// </summary>
        /// <returns>
        /// true if the values of <paramref name="left" /> and <paramref name="right" /> are not equal; otherwise, false.
        /// </returns>
        /// <param name="left">The first object to compare.</param>
        /// <param name="right">The second object to compare.</param>
        /// <filterpriority>3</filterpriority>
        public static bool operator !=(PipDataAtom left, PipDataAtom right)
        {
            return !left.Equals(right);
        }
        #endregion

    }
}
