// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using BuildXL.FrontEnd.Script.Evaluator;
using BuildXL.FrontEnd.Script.Expressions;
using BuildXL.FrontEnd.Script.Literals;
using BuildXL.FrontEnd.Script.RuntimeModel.AstBridge;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using static BuildXL.Utilities.FormattableStringEx;
using LineInfo = TypeScript.Net.Utilities.LineInfo;

namespace BuildXL.FrontEnd.Script.Values
{
    /// <summary>
    /// Array literal.
    /// </summary>
    public abstract class ArrayLiteral : ObjectLiteral
    {
        private static readonly EvaluatedArrayLiteral s_empty = new EvaluatedArrayLiteral(CollectionUtilities.EmptyArray<EvaluationResult>(), default(LineInfo), AbsolutePath.Invalid);

        /// <nodoc />
        protected ArrayLiteral(LineInfo location, AbsolutePath path)
            : base(location, path)
        {
        }

        /// <summary>
        /// Creates array literal with a given <paramref name="arguments"/> as expressions array.
        /// </summary>
        public static ArrayLiteral Create(Expression[] arguments, LineInfo location, AbsolutePath path)
        {
            if (arguments.Length == 0)
            {
                return s_empty;
            }

            return new UnevaluatedArrayLiteral(arguments.ToArray(), location, path);
        }

        /// <summary>
        /// Creates evaluated array.
        /// </summary>
        public static ArrayLiteral CreateEvaluated(Expression[] arguments, LineInfo location, AbsolutePath path)
        {
            var evaluatedArray = new EvaluationResult[arguments.Length];
            for (int i = 0; i < arguments.Length; i++)
            {
                evaluatedArray[i] = EvaluationResult.Create(((IConstantExpression)arguments[i]).Value);
            }

            return new EvaluatedArrayLiteral(evaluatedArray, location, path);
        }

        /// <summary>
        /// Creates array literal with a given <paramref name="arguments"/> as array literal slim.
        /// </summary>
        public static ArrayLiteral CreateWithoutCopy(EvaluationResult[] arguments, LineInfo location, AbsolutePath path)
        {
            if (arguments.Length == 0)
            {
                return s_empty;
            }

            return new EvaluatedArrayLiteral(arguments, location, path);
        }

        /// <summary>
        /// Creates an array literal from the stream.
        /// </summary>
        public static new ArrayLiteral Create(DeserializationContext context, LineInfo location)
        {
            var reader = context.Reader;

            var path = reader.ReadAbsolutePath();
            var length = reader.ReadInt32Compact();
            bool evaluatedArray = reader.ReadBoolean();
            if (evaluatedArray)
            {
                var values = new EvaluationResult[length];
                for (int i = 0; i < length; i++)
                {
                    values[i] = ConstExpressionSerializer.ReadConstValueAsEvaluationResult(context.Reader);
                }

                return CreateWithoutCopy(values, location, path);
            }

            var expressions = new Expression[length];
            for (int i = 0; i < length; i++)
            {
                expressions[i] = ReadExpression(context);
            }

            return Create(expressions, location, path);
        }

        /// <summary>
        /// Values.
        /// </summary>
        public abstract IReadOnlyList<EvaluationResult> Values { get; }

        /// <summary>
        /// Gets length.
        /// </summary>
        public abstract int Length { get; }

        /// <summary>
        /// Array accessor.
        /// </summary>
        public abstract EvaluationResult this[int index] { get; }

        /// <inheritdoc />
        public override SyntaxKind Kind => SyntaxKind.ArrayLiteral;

        // TODO:ST: Array has member length, but a["length"] will return undefined!

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
        public override string ToStringShort(StringTable stringTable)
        {
            return "Array";
        }

        /// <inheritdoc />
        public override IEnumerable<KeyValuePair<StringId, EvaluationResult>> Members => CollectionUtilities.EmptyArray<KeyValuePair<StringId, EvaluationResult>>();

        /// <inheritdoc />
        public override IEnumerable<StringId> Keys => CollectionUtilities.EmptyArray<StringId>();

        /// <inheritdoc />
        public override void Accept(Visitor visitor)
        {
            visitor.Visit(this);
        }

        /// <inheritdoc />
        public override EvaluationResult GetOrEvalField(Context context, StringId name, bool recurs, ModuleLiteral origin, LineInfo location)
        {
            Contract.Requires(name.IsValid);

            if (name == context.ContextTree.CommonConstants.Length.StringId)
            {
                return EvaluationResult.Create(Length);
            }

            return EvaluationResult.Undefined;
        }

        /// <inheritdoc />
        public override bool HasKey(StringId key)
        {
            return false;
        }

        /// <summary>
        /// Copies this array literal to the destination array.
        /// </summary>
        public abstract void Copy(int sourceIndex, EvaluationResult[] destination, int destinationIndex, int length);

        /// <inheritdoc />
        public override bool TryProject(Context context, SymbolAtom name, ModuleLiteral origin, out EvaluationResult result, LineInfo location)
        {
            if (name == context.ContextTree.CommonConstants.Length)
            {
                result = EvaluationResult.Create(Length);
                return true;
            }

            var resolvedMember = ((ModuleRegistry)context.FrontEndHost.ModuleRegistry).PredefinedTypes.AmbientArray.ResolveMember(this, name);

            if (resolvedMember == null)
            {
                // Array literals could not have arbitrary members.
                // So if the name was not resolved, reporting an error.
                // var locationForLogging = LocationForLogging(context, origin);
                var locationForLogging = UniversalLocation.FromLineInfo(location, origin.Path, context.PathTable);

                context.Logger.ReportMissingInstanceMember(
                    context.LoggingContext,
                    locationForLogging.AsLoggingLocation(),
                    name.ToDisplayString(context),
                    DisplayStringHelper.TypeToString(GetType(), context),
                    context.GetStackTraceAsString(locationForLogging));

                result = EvaluationResult.Error;
            }
            else
            {
                result = EvaluationResult.Create(resolvedMember);
            }

            return true;
        }

        /// <inheritdoc/>
        public override EvaluationResult Override(Context context, EvaluationResult right)
        {
            // Overriding an array always returns the right side
            return right;
        }

        /// <inheritdoc/>
        public override EvaluationResult Merge(Context context, EvaluationStackFrame captures, EvaluationResult right)
        {
            var mergeFunction = GetMergeFunction(context, captures, this, right);
            return mergeFunction(EvaluationResult.Create(this), right);
        }

        /// <summary>
        /// Append is the default for an array
        /// </summary>
        protected override MergeFunction GetDefaultMergeFunction(Context context, EvaluationStackFrame captures) => MergeAppend();
        
        /// <summary>
        /// A regular array never has a custom merge function defined
        /// </summary>
        protected override MergeFunction TryGetCustomMergeFunction(Context context, EvaluationStackFrame captures) => null;

        internal static MergeFunction MergeAppend()
        {
            return (leftObject, rightObject) =>
                   {
                       // The left object is always an array literal,
                       // since this is a merge function used for arrays only
                       var lhsArray = leftObject.Value as ArrayLiteral;
                       if (leftObject.Value == null)
                       {
                           Contract.Assert(false, I($"Merge append should always get an array literal in its left operand, but got '{leftObject.Value?.GetType()}'"));
                       }

                       // If the right hand side is not an array, then this acts as an override.
                       if (!(rightObject.Value is ArrayLiteral rhsArray))
                       {
                           return rightObject;
                       }

                       // Otherwise the right array is appended to this array
                       var mergedArray = new EvaluationResult[lhsArray.Length + rhsArray.Length];
                       if (lhsArray.Length > 0)
                       {
                           lhsArray.Copy(0, mergedArray, 0, lhsArray.Length);
                       }

                       if (rhsArray.Length > 0)
                       {
                           rhsArray.Copy(0, mergedArray, lhsArray.Length, rhsArray.Length);
                       }

                       return EvaluationResult.Create(CreateWithoutCopy(mergedArray, rhsArray.Location, rhsArray.Path));
                   };
        }

        /// <nodoc />
        public IEnumerator<EvaluationResult> GetEnumerator()
        {
            return Values.GetEnumerator();
        }
    }
}
