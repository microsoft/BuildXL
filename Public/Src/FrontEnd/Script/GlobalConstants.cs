// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.ContractsLight;
using BuildXL.FrontEnd.Script.Values;
using BuildXL.Utilities;

namespace BuildXL.FrontEnd.Script
{
    /// <summary>
    /// Class representing data needed by DScript front-ends, and possibly shared by multiple DScript front-ends.
    /// </summary>
    /// <remarks>
    /// Ideally this data should be held by the front-end host. However to avoid cyclic dependency
    /// this data is placed here specifically only for DScript front-end.
    /// </remarks>
    public sealed class GlobalConstants
    {
        /// <nodoc />
        public Util.Literals Literals { get; }

        /// <nodoc /> TODO: Merge it with literals.
        public Names Names { get; }

        /// <nodoc />
        public PrimitiveTypes KnownTypes { get; }

        /// <nodoc />
        public GlobalModuleLiteral Global { get; }

        /// <nodoc />
        public Ambients.PredefinedTypes PredefinedTypes { get; }

        /// <nodoc />
        public GlobalConstants(SymbolTable symbolTable)
        {
            Contract.Requires(symbolTable != null);

            Literals = new Util.Literals(symbolTable.StringTable);
            Names = new Names(symbolTable);
            KnownTypes = new PrimitiveTypes(symbolTable.StringTable);
            Global = new GlobalModuleLiteral(symbolTable);
            PredefinedTypes = new Ambients.PredefinedTypes(KnownTypes);
            PredefinedTypes.Register(Global);

            // TODO: We are relying on the entire codebase pinky-swearing they won't mutate global. Ideally we want to force this.
        }
    }
}
