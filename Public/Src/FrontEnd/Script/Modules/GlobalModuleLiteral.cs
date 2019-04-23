// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using BuildXL.FrontEnd.Script.Evaluator;
using BuildXL.FrontEnd.Sdk;
using BuildXL.Utilities;
using LineInfo = TypeScript.Net.Utilities.LineInfo;

namespace BuildXL.FrontEnd.Script.Values
{
    /// <summary>
    /// "Module" that holds all global ambient functions, types and values.
    /// </summary>
    public sealed class GlobalModuleLiteral : ModuleLiteral
    {
        /// <summary>
        /// The symboltable to use when adding namespaces.
        /// </summary>
        public SymbolTable SymbolTable { get; }

        /// <inheritdoc/>
        public override Package Package => null;

        /// <inheritdoc/>
        public override ModuleLiteral Instantiate(ModuleRegistry moduleRegistry, QualifierValue qualifier)
        {
            Contract.Assert(false, "GlobalModuleLiteral cannot be instantiated");
            throw new NotImplementedException();
        }

        /// <summary>
        /// For globals current file is null, because this is no file module for it.
        /// </summary>
        public override FileModuleLiteral CurrentFileModule => null;

        /// <inheritdoc/>
        public override QualifierValue GetFileQualifier() => null;

        /// <inheritdoc />
        public override SyntaxKind Kind => SyntaxKind.GlobalModuleLiteral;

        /// <nodoc/>
        public GlobalModuleLiteral(SymbolTable symbolTable)
            : base(ModuleLiteralId.Invalid, qualifier: QualifierValue.Unqualified, outerScope: null, location: default(LineInfo))
        {
            SymbolTable = symbolTable;
        }
    }
}
