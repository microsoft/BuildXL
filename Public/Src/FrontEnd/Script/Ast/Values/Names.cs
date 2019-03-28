// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.ContractsLight;
using BuildXL.Utilities;

namespace BuildXL.FrontEnd.Script.Values
{
    /// <summary>
    /// Names for value AST.
    /// </summary>
    /// <remarks>
    /// The methods in this class should only be used after the global front-end context is created.
    /// </remarks>
    public sealed class Names
    {
        /// <summary>
        /// Qualifier.
        /// </summary>
        public SymbolAtom Qualifier { get; }

        /// <summary>
        /// Length.
        /// </summary>
        public SymbolAtom Length { get; }

        /// <nodoc />
        public SymbolAtom CmdArgumentNameField { get; }

        /// <nodoc />
        public SymbolAtom CmdArgumentValueField { get; }

        /// <nodoc />
        public SymbolAtom CmdArtifactPathField { get; }

        /// <nodoc />
        public SymbolAtom CmdArtifactKindField { get; }

        /// <nodoc />
        public SymbolAtom CmdArtifactOriginalField { get; }

        /// <nodoc />
        public SymbolAtom CmdPrimitiveArgumentValueField { get; }

        /// <nodoc />
        public SymbolAtom CmdPrimitiveArgumentKindField { get; }

        /// <nodoc />
        public SymbolAtom CmdListArgumentValuesField { get; }

        /// <nodoc />
        public SymbolAtom CmdListArgumentSeparatorField { get; }


        /// <summary>
        /// Separator.
        /// </summary>
        public SymbolAtom DataSeparator { get; }

        /// <summary>
        /// Contents.
        /// </summary>
        public SymbolAtom DataContents { get; }


        /// <nodoc />
        private SymbolTable SymbolTable { get; }

        /// <nodoc />
        public Names(SymbolTable symbolTable)
        {
            Contract.Requires(symbolTable != null);

            SymbolTable = symbolTable;

            Qualifier = Create("qualifier");
            Length = Create("length");

            // Args.

            // Runtime environment.

          
            // Command-line arguments.
            CmdArgumentNameField = Create("name");
            CmdArgumentValueField = Create("value");
            CmdArtifactPathField = Create("path");
            CmdArtifactKindField = Create("kind");
            CmdArtifactOriginalField = Create("original");
            CmdPrimitiveArgumentValueField = Create("value");
            CmdPrimitiveArgumentKindField = Create("kind");
            CmdListArgumentValuesField = Create("values");
            CmdListArgumentSeparatorField = Create("separator");

            

            // Data.
            DataSeparator = Create("separator");
            DataContents = Create("contents");

            // Testing

        }

        /// <summary>
        /// Creates name.
        /// </summary>
        public SymbolAtom Create(string name)
        {
            Contract.Requires(!string.IsNullOrWhiteSpace(name));
            return SymbolAtom.Create(SymbolTable.StringTable, name);
        }

        /// <summary>
        /// Creates full name.
        /// </summary>
        public FullSymbol CreateFull(string name)
        {
            Contract.Requires(!string.IsNullOrWhiteSpace(name));
            return FullSymbol.Create(SymbolTable, name);
        }

        /// <summary>
        /// Creates full name.
        /// </summary>
        public FullSymbol CreateFull(SymbolAtom name)
        {
            Contract.Requires(name.IsValid);
            return FullSymbol.Create(SymbolTable, name);
        }

        /// <summary>
        /// Creates id.
        /// </summary>
        public int CreateId(string name)
        {
            Contract.Requires(!string.IsNullOrWhiteSpace(name));
            return Create(name).StringId.Value;
        }

        /// <summary>
        /// Gets the string representation of a name.
        /// </summary>
        public string ToString(SymbolAtom name)
        {
            return name.ToString(SymbolTable);
        }
    }
}
