// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using BuildXL.FrontEnd.Script.RuntimeModel.AstBridge;
using BuildXL.Utilities;

namespace BuildXL.FrontEnd.Script.RuntimeModel
{
    /// <summary>
    /// Definition of the variable.
    /// </summary>
    public readonly struct VariableDefinition : IEquatable<VariableDefinition>
    {
        private const int GlobalIndex = -1;

        /// <nodoc />
        private VariableDefinition(SymbolAtom name, int index, UniversalLocation locationDefinition, bool isConstant)
        {
            Contract.Requires(name.IsValid);

            Name = name;
            Index = index;
            LocationDefinition = locationDefinition;
            IsConstant = isConstant;
        }

        /// <summary>
        /// Creates a local variable definition.
        /// </summary>
        public static VariableDefinition CreateLocal(SymbolAtom name, int index, in UniversalLocation location, bool isConstant)
        {
            Contract.Requires(index >= 0);
            return new VariableDefinition(name, index, location, isConstant);
        }

        /// <summary>
        /// Creates a global variable definition.
        /// </summary>
        public static VariableDefinition CreateGlobal(SymbolAtom name, in UniversalLocation location)
        {
            // Top-level values are always constants in DScript.
            return new VariableDefinition(name, GlobalIndex, location, isConstant: true);
        }

        /// <summary>
        /// Name of the variable
        /// </summary>
        public SymbolAtom Name { get; }

        /// <summary>
        /// Index in the local variable table.
        /// </summary>
        public int Index { get; }

        /// <summary>
        /// Returns true if a local variable is a constant.
        /// </summary>
        public bool IsConstant { get; }

        /// <summary>
        /// True when variable is defined inside a function or part of the function arguments.
        /// </summary>
        /// <remarks>
        /// This diference is significant, because for "global" variables (i.e., top level or namespace level)
        /// different expressions are used for evaluation.
        /// </remarks>
        public bool IsLocal => Index >= 0;

        /// <summary>
        /// Location where variable is defined.
        /// </summary>
        public UniversalLocation LocationDefinition { get; }

        /// <summary>
        /// Value equality.
        /// </summary>
        public bool Equals(VariableDefinition other)
        {
            return
                Index == other.Index &&
                LocationDefinition.AsFilePosition() == other.LocationDefinition.AsFilePosition();
        }

        /// <inheridoc/>
        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj))
            {
                return false;
            }

            return obj is VariableDefinition && Equals((VariableDefinition)obj);
        }

        /// <inheridoc/>
        public override int GetHashCode()
        {
            return HashCodeHelper.Combine(
                Index.GetHashCode(),
                LocationDefinition.GetHashCode());
        }

        /// <nodoc />
        public static bool operator ==(VariableDefinition left, VariableDefinition right)
        {
            return left.Equals(right);
        }

        /// <nodoc />
        public static bool operator !=(VariableDefinition left, VariableDefinition right)
        {
            return !left.Equals(right);
        }
    }
}
