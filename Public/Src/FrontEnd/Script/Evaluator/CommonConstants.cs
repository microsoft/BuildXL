// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.ContractsLight;
using BuildXL.FrontEnd.Script.Constants;
using BuildXL.Utilities;

namespace BuildXL.FrontEnd.Script.Evaluator
{
    /// <summary>
    /// CommonConstants for value AST.
    /// </summary>
    public sealed class CommonConstants
    {
        /// <nodoc />
        private readonly StringTable m_stringTable;

        /// <nodoc />
        public SymbolAtom Qualifier { get; }

        /// <nodoc />
        public SymbolAtom Length { get; }

        /// <nodoc />
        public SymbolAtom CustomMergeFunction { get; }

        /// <nodoc />
        public CommonConstants(StringTable stringTable)
        {
            Contract.Requires(stringTable != null);

            m_stringTable = stringTable;

            Qualifier = Create(Names.CurrentQualifier);
            Length = Create(Names.ArrayLengthName);
            CustomMergeFunction = Create(Names.CustomMergeFunctionName);
        }

        /// <summary>
        /// Creates name.
        /// </summary>
        public SymbolAtom Create(string name)
        {
            Contract.Requires(!string.IsNullOrWhiteSpace(name));
            return SymbolAtom.Create(m_stringTable, name);
        }
    }
}
