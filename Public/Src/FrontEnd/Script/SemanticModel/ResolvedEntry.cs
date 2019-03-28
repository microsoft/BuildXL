// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Threading.Tasks;
using BuildXL.Utilities;
using BuildXL.Utilities.Qualifier;
using BuildXL.FrontEnd.Script.Evaluator;
using BuildXL.FrontEnd.Script.Expressions;
using BuildXL.FrontEnd.Script.Literals;

using LineInfo = TypeScript.Net.Utilities.LineInfo;

using static BuildXL.Utilities.FormattableStringEx;

namespace BuildXL.FrontEnd.Script.Values
{
    /// <summary>
    /// Kinds of resolved entries.
    /// </summary>
    public enum ResolvedEntryKind : byte
    {
        /// <nodoc />
        Function,

        /// <nodoc />
        ConstExpression,

        /// <nodoc />
        Expression,

        /// <nodoc />
        Thunk,

        /// <nodoc />
        ResolverCallback,
    }

    /// <summary>
    /// Callback signature for resolved entries backed by frontend implementations rather than expressions.
    /// </summary>
    public delegate Task<EvaluationResult> FrontEndCallback(Context context, ModuleLiteral env, EvaluationStackFrame args);

    /// <summary>
    /// Represents resolved entry in a DScript object tree.
    /// </summary>
    /// <remarks>
    /// Effectively, this struct is a union type: Thunk | FunctionLikeExpression | ConstantExpression | Expression | FrontEndImplementation.
    /// This struct is stored in a <see cref="FileModuleLiteral"/> to represents an entry that could be referenced
    /// by other expressions/statements at runtime.
    /// </remarks>
    [DebuggerDisplay("{ToString(),nq}")]
    [SuppressMessage("Microsoft.Performance", "CA1815:OverrideEqualsAndOperatorEqualsOnValueTypes")]
    public readonly struct ResolvedEntry
    {
        private readonly FullSymbol m_thunkContextNameOrSymbolName;

        /// <nodoc/>
        public Thunk Thunk { get; }

        /// <summary>
        /// The context name of the thunk. Only available when the entry represents a Thunk.
        /// </summary>
        public FullSymbol ThunkContextName => m_thunkContextNameOrSymbolName;

        /// <nodoc />
        public FullSymbol SymbolName => m_thunkContextNameOrSymbolName;

        /// <nodoc/>
        public FunctionLikeExpression Function { get; }

        /// <nodoc/>
        public IConstantExpression ConstantExpression { get; }

        /// <nodoc/>
        public Expression Expression { get; }

        /// <summary>
        /// This represents a resolved entry that custom frontends can add.
        /// </summary>
        public FrontEndCallback ResolverCallback { get; }

        /// <summary>
        /// Location of the resolved entry
        /// </summary>
        public LineInfo Location { get; }

        /// <summary>
        /// Returns true if the entry points to a top level variable declaration.
        /// </summary>
        public bool IsVariableDeclaration { get; }

        /// <summary>
        /// Qualifier space Id, computed during ast conversion.
        /// </summary>
        /// <remarks>
        /// Only top-level declarations has this property and this property is used to separate top-most declarations from the
        /// nested declarations that should not be evaluated by default.
        /// </remarks>
        public QualifierSpaceId QualifierSpaceId { get; }

        /// <nodoc/>
        public ResolvedEntry(FullSymbol symbolName, Expression expression)
            : this()
        {
            Contract.Requires(expression != null);

            Expression = expression;
            Location = expression.Location;
            m_thunkContextNameOrSymbolName = symbolName;
        }

        /// <nodoc/>
        public ResolvedEntry(FullSymbol symbolName, IConstantExpression constantExpression)
            : this()
        {
            Contract.Requires(constantExpression != null);

            ConstantExpression = constantExpression;
            Location = constantExpression.Location;
            m_thunkContextNameOrSymbolName = symbolName;
        }

        /// <nodoc/>
        public ResolvedEntry(FullSymbol thunkContextName, Thunk thunk, QualifierSpaceId qualifierSpaceId, bool isVariableDeclaration)
            : this()
        {
            Contract.Requires(thunk != null);

            Thunk = thunk;
            Location = thunk.Expression.Location;
            m_thunkContextNameOrSymbolName = thunkContextName;
            IsVariableDeclaration = isVariableDeclaration;
            QualifierSpaceId = qualifierSpaceId;
        }

        /// <nodoc/>
        public ResolvedEntry(FullSymbol symbolName, FunctionLikeExpression function)
            : this()
        {
            Contract.Requires(function != null);

            Function = function;
            Location = function.Location;
            m_thunkContextNameOrSymbolName = symbolName;
        }

        /// <nodoc/>
        public ResolvedEntry(FullSymbol symbolName, FrontEndCallback resolverCallback, LineInfo location)
            : this()
        {
            Contract.Requires(resolverCallback != null);

            ResolverCallback = resolverCallback;
            Location = location;
            m_thunkContextNameOrSymbolName = symbolName;
        }

        /// <nodoc />
        public static ResolvedEntry ReadResolvedEntry(DeserializationContext context)
        {
            var reader = context.Reader;
            var kind = (ResolvedEntryKind)reader.ReadByte();
            var name = reader.ReadFullSymbol();

            switch (kind)
            {
                case ResolvedEntryKind.Function:
                    return new ResolvedEntry(name, (FunctionLikeExpression)Node.Read(context));
                case ResolvedEntryKind.Expression:
                    return new ResolvedEntry(name, (Expression)Node.Read(context));
                case ResolvedEntryKind.ConstExpression:
                    return new ResolvedEntry(name, (IConstantExpression)ConstExpressionSerializer.Read(context));
                case ResolvedEntryKind.Thunk:
                    var expression = (Expression)Node.Read(context);
                    var template = (Expression)Node.Read(context);
                    var thunk = new Thunk(expression, template);

                    var qualifierSpaceId = reader.ReadQualifierSpaceId();
                    var isVariableDeclaration = reader.ReadBoolean();
                    return new ResolvedEntry(name, thunk, qualifierSpaceId, isVariableDeclaration);

                default:
                    throw new InvalidOperationException(I($"Unknown resolved entry kind '{kind}'."));
            }
        }

        /// <nodoc />
        public void Serialize(BuildXLWriter writer)
        {
            var kind = Kind;

            Contract.Assert(kind != ResolvedEntryKind.ResolverCallback, "Resolver Callbacks are not serializable and therefore this module should not have been marked as cacheable.");

            writer.Write((byte)kind);
            writer.Write(m_thunkContextNameOrSymbolName);

            if (Thunk != null)
            {
                Thunk.Expression.Serialize(writer);
                Node.Serialize(Thunk.CapturedTemplateReference, writer);

                writer.Write(QualifierSpaceId);
                writer.Write(IsVariableDeclaration);
            }
            else
            {
                if (Function != null)
                {
                    Function.Serialize(writer);
                }
                else if (ConstantExpression != null)
                {
                    ConstExpressionSerializer.Write(writer, ConstantExpression);
                }
                else if (Expression != null)
                {
                    Expression.Serialize(writer);
                }
            }
        }

        private ResolvedEntryKind Kind
        {
            get
            {
                if (ResolverCallback != null)
                {
                    return ResolvedEntryKind.ResolverCallback;
                }
                if (Thunk != null)
                {
                    return ResolvedEntryKind.Thunk;
                }

                if (Function != null)
                {
                    return ResolvedEntryKind.Function;
                }

                if (ConstantExpression != null)
                {
                    return ResolvedEntryKind.ConstExpression;
                }

                Contract.Assert(Expression != null, "Expression != null");
                return ResolvedEntryKind.Expression;
            }
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            if (Thunk != null)
            {
                return I($"Thunk: {Thunk}");
            }

            if (Function != null)
            {
                return I($"Function: {Function}");
            }

            if (Expression != null)
            {
                return I($"Expression: {Expression}");
            }

            Contract.Assert(ConstantExpression != null);

            return I($"Contant: {ConstantExpression}");
        }

        /// <summary>
        /// Returns whichever one of <see cref="Thunk"/>, <see cref="Function"/>, <see cref="Expression"/>, and <see cref="ConstantExpression"/> is not null.
        /// </summary>
        public object GetValue()
        {
            return Thunk ?? Function ?? Expression ?? (object)ConstantExpression;
        }
    }
}
