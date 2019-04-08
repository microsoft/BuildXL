// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.ContractsLight;
using BuildXL.FrontEnd.Script;
using BuildXL.Utilities;
using BuildXL.FrontEnd.Script.Values;
using BuildXL.FrontEnd.Script.Evaluator;
using LineInfo = TypeScript.Net.Utilities.LineInfo;

namespace BuildXL.FrontEnd.Script.Expressions
{
    /// <summary>
    /// Symbol reference that uses absolute location as a symbol identity.
    /// </summary>
    /// <remarks>
    /// When semantic information from the managed checker is available, all name resolution
    /// is happening during AST conversion time.
    /// </remarks>
    public sealed class LocationBasedSymbolReference : SymbolReferenceExpression
    {
        private const int MaxQualifierId = 4;
        private readonly EvaluationResult[] m_qualifiedResults = new EvaluationResult[MaxQualifierId];

        /// <summary>
        /// Name of a referenced symbol.
        /// </summary>
        /// <remarks>
        /// Used only for diagnostics purposed and not used by the resolution logic.
        /// </remarks>
        public SymbolAtom Name { get; }

        /// <summary>
        /// Absolute location of a referenced symbol.
        /// </summary>
        public FilePosition FilePosition { get; }

        private readonly FullSymbol m_fullSymbol;

        /// <nodoc/>
        public LocationBasedSymbolReference(FilePosition filePosition, SymbolAtom symbolName, LineInfo location, SymbolTable symbolTable)
            : base(location)
        {
            Contract.Requires(filePosition.IsValid);
            Contract.Requires(symbolName.IsValid);

            FilePosition = filePosition;
            Name = symbolName;

            m_fullSymbol = FullSymbol.Create(symbolTable, symbolName);
        }

        /// <nodoc/>
        public LocationBasedSymbolReference(DeserializationContext context, LineInfo location)
            : base(location)
        {
            var reader = context.Reader;

            FilePosition = ReadFilePosition(reader);
            Name = reader.ReadSymbolAtom();
            m_fullSymbol = reader.ReadFullSymbol();
        }

        /// <inheritdoc />
        protected override void DoSerialize(BuildXLWriter writer)
        {
            WriteFilePosition(FilePosition, writer);
            writer.Write(Name);
            writer.Write(m_fullSymbol);
        }

        /// <inheritdoc />
        public override void Accept(Visitor visitor)
        {
            visitor.Visit(this);
        }

        /// <inheritdoc />
        public override SyntaxKind Kind => SyntaxKind.LocationBasedSymbolReference;

        /// <inheritdoc />
        public override string ToDebugString()
        {
            return ToDebugString(m_fullSymbol);
        }

        /// <inheritdoc />
        protected override EvaluationResult DoEval(Context context, ModuleLiteral env, EvaluationStackFrame frame)
        {
            var qualifierId = env.Qualifier.QualifierId.Id;
            if (qualifierId >= MaxQualifierId)
            {
                return env.EvaluateByLocation(context, FilePosition, m_fullSymbol, Location);
            }

            return m_qualifiedResults[qualifierId].IsValid
                ? m_qualifiedResults[qualifierId]
                : (m_qualifiedResults[qualifierId] = env.EvaluateByLocation(context, FilePosition, m_fullSymbol, Location));
        }

        /// <summary>
        /// Resolve a current location-based reference as <see cref="FunctionLikeExpression"/>.
        /// </summary>
        /// <remarks>
        /// This method is used to resolve a function once and use it for all the invocations.
        /// </remarks>
        internal bool TryResolveFunction(Context context, ModuleLiteral env, out FunctionLikeExpression function, out FileModuleLiteral file)
        {
            return env.TryResolveFunction(context, FilePosition, m_fullSymbol, out function, out file);
        }
    }
}
