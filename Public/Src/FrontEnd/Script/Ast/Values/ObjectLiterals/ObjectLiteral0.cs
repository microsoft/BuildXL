// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using BuildXL.FrontEnd.Script;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using BuildXL.FrontEnd.Script.Evaluator;
using LineInfo = TypeScript.Net.Utilities.LineInfo;

namespace BuildXL.FrontEnd.Script.Values
{
    /// <summary>
    /// Object literal of size 0.
    /// </summary>
    [SuppressMessage("Microsoft.Naming", "CA1710")]
    public sealed class ObjectLiteral0 : ObjectLiteral
    {
        /// <summary>
        /// Empty object literal without provenance.
        /// </summary>
        /// <remarks>
        /// This instance shouldn't be used to represent empty objects literals declared in specs, since those should have provenance
        /// </remarks>
        public static ObjectLiteral0 SingletonWithoutProvenance { get; } = new ObjectLiteral0(location: default(LineInfo), path: default(AbsolutePath));

        /// <nodoc />
        public ObjectLiteral0(LineInfo location, AbsolutePath path)
            : base(location, path)
        {
        }

        /// <inheritdoc />
        public override void Accept(Visitor visitor)
        {
            visitor.Visit(this);
        }

        /// <inheritdoc />
        public override SyntaxKind Kind => SyntaxKind.ObjectLiteral0;

        /// <inheritdoc />
        protected override EvaluationResult DoEval(Context context, ModuleLiteral env, EvaluationStackFrame frame)
        {
            return EvaluationResult.Create(this);
        }

        /// <inheritdoc />
        public override EvaluationResult GetOrEvalField(Context context, StringId name, bool recurs, ModuleLiteral origin, LineInfo location)
        {
            return EvaluationResult.Undefined;
        }

        /// <inheritdoc />
        public override EvaluationResult this[SymbolAtom name]
        {
            get
            {
                Contract.Requires(name.IsValid);
                return EvaluationResult.Undefined;
            }
        }

        /// <inheritdoc />
        public override EvaluationResult this[StringId name]
        {
            get
            {
                Contract.Requires(name.IsValid);
                return EvaluationResult.Undefined;
            }
        }

        /// <inheritdoc />
        public override int Count => 0;

        /// <inheritdoc />
        public override IEnumerable<KeyValuePair<StringId, EvaluationResult>> Members => CollectionUtilities.EmptyArray<KeyValuePair<StringId, EvaluationResult>>();

        /// <inheritdoc />
        public override IEnumerable<StringId> Keys => CollectionUtilities.EmptyArray<StringId>();

        /// <inheritdoc />
        public override bool HasKey(StringId key)
        {
            return false;
        }
    }
}
