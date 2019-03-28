// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Globalization;
using BuildXL.Utilities;

namespace TypeScript.Net.Incrementality
{
    /// <summary>
    /// Kind of a symbol.
    /// See <see cref="InteractionSymbol"/> comment for more details.
    /// </summary>
    public enum SymbolKind : byte
    {
        /// <nodoc />
        None,

        /// <nodoc />
        EnumDeclaration,

        /// <nodoc />
        EnumValueDeclaration,

        /// <nodoc />
        TypeParameter,

        /// <nodoc />
        InterfaceDeclaration,

        /// <nodoc />
        InterfaceMemberDeclaration,

        /// <nodoc />
        NamespaceDeclaration,

        /// <nodoc />
        TypeDeclaration,

        /// <nodoc />
        ImportAlias,

        /// <nodoc />
        ImportedModule,

        /// <nodoc />
        FunctionDeclaration,

        /// <nodoc />
        VariableDeclaration,

        /// <nodoc />
        ParameterDeclaration,

        /// <nodoc />
        Reference,

        /// <summary>
        /// Type that is used in inheritance clause or in other type reference contexts like method signature.
        /// </summary>
        TypeReference,
    }

    /// <summary>
    /// Symbol used or provided by the spec.
    /// </summary>
    /// <remarks>
    /// Interaction symbol has a kind and a full name that uniquely identify a declaration or usage.
    /// If any of the interaction symbols changed by the user it can change the binding information for this file or for another file within the same module.
    /// For instance, even commenting out the local variable inside the function could change the name resolution:
    /// <code>
    /// // state 1
    /// // file1.dsc
    /// namespace X {
    ///     export const x = 42;
    /// }
    ///
    /// // file2.dsc
    /// namespace X {
    ///     function foo() {
    ///         const x = 42;
    ///         return x; // using local variable
    ///     }
    /// }
    ///
    /// // state 2: user comments out the local variable
    /// // file1.dsc
    /// namespace X {
    ///     export const x = 42;
    /// }
    ///
    /// // file2.dsc
    /// namespace X {
    ///     function foo() {
    ///         // const x = 42;
    ///         return x; // using x from the file1.dsc
    ///     }
    /// }
    /// </code>
    /// </remarks>
    public readonly struct InteractionSymbol : IEquatable<InteractionSymbol>
    {
        /// <summary>
        /// Kind of the symbol.
        /// </summary>
        public SymbolKind Kind { get; }

        /// <summary>
        /// Full name of the symbol.
        /// </summary>
        public string FullName { get; }

        /// <nodoc />
        public InteractionSymbol(SymbolKind kind, string fullName)
        {
            Kind = kind;
            FullName = fullName;
        }

        /// <inheritdoc />
        public bool Equals(InteractionSymbol other)
        {
            return Kind == other.Kind && FullName.Equals(other.FullName);
        }

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj))
            {
                return false;
            }

            return obj is InteractionSymbol && Equals((InteractionSymbol)obj);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return HashCodeHelper.Combine((int)Kind, FullName.GetHashCode());
        }

        /// <nodoc />
        public static bool operator ==(InteractionSymbol left, InteractionSymbol right)
        {
            return left.Equals(right);
        }

        /// <nodoc />
        public static bool operator !=(InteractionSymbol left, InteractionSymbol right)
        {
            return !(left == right);
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            return string.Format(CultureInfo.InvariantCulture, "{0}: {1}", Kind, FullName);
        }
    }
}
