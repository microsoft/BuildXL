// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using BuildXL.FrontEnd.Script.Declarations;
using BuildXL.FrontEnd.Script.Values;
using BuildXL.FrontEnd.Script.Evaluator;
using TypeScript.Net.Extensions;
using LineInfo = TypeScript.Net.Utilities.LineInfo;

namespace BuildXL.FrontEnd.Script
{
    /// <summary>
    /// A build project file, e.g. a DScript file, MSBuild .proj, NMake sources, etc.
    /// </summary>
    public class SourceFile : Node
    {
        /// <summary>
        /// Declarations.
        /// </summary>
        public IReadOnlyList<Declaration> Declarations { get; }

        /// <summary>
        /// Absolute path for a current source file.
        /// </summary>
        public AbsolutePath Path { get; }

        /// <nodoc />
        public SourceFile(AbsolutePath path, IReadOnlyList<Declaration> declarations)
            : base(location: default(LineInfo)) // Source file itself doesn't have location inside a file.
        {
            Contract.Requires(declarations != null);
            Contract.RequiresForAll(declarations, d => d != null);

            Declarations = declarations;
            Path = path;
        }

        /// <nodoc/>
        public SourceFile(DeserializationContext context, LineInfo location)
            : base(location)
        {
            Declarations = ReadArrayOf<Declaration>(context);
            Path = context.Reader.ReadAbsolutePath();
        }

        /// <inheritdoc/>
        protected override void DoSerialize(BuildXLWriter writer)
        {
            WriteArrayOf(Declarations, writer);
            writer.Write(Path);
        }

        /// <inheritdoc />
        public override void Accept(Visitor visitor)
        {
            visitor.Visit(this);
            foreach (var declaration in Declarations.AsStructEnumerable())
            {
                declaration.Accept(visitor);
            }
        }

        /// <inheritdoc />
        protected override EvaluationResult DoEval(Context context, ModuleLiteral env, EvaluationStackFrame frame)
        {
            throw new NotImplementedException();
        }

        /// <inheritdoc />
        public override SyntaxKind Kind => SyntaxKind.SourceFile;

        /// <inheritdoc/>
        public override string ToDebugString()
        {
            return string.Join(Environment.NewLine, Declarations.Select(n => n.ToDebugString()));
        }
    }
}
