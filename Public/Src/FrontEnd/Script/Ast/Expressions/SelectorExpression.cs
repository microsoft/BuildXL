// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using System.Threading;
using BuildXL.FrontEnd.Script;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using BuildXL.FrontEnd.Script.Values;
using BuildXL.FrontEnd.Script.Evaluator;
using static BuildXL.Utilities.FormattableStringEx;
using LineInfo = TypeScript.Net.Utilities.LineInfo;

namespace BuildXL.FrontEnd.Script.Expressions
{
    /// <summary>
    /// Selector expression like <code>receiver.member</code>
    /// </summary>
    public sealed class SelectorExpression : SelectorExpressionBase
    {
        private static readonly object[] s_zeroValues = CollectionUtilities.EmptyArray<object>();

        /// <summary>
        /// Selector.
        /// </summary>
        public override SymbolAtom Selector { get; }

        /// <nodoc />
        public SelectorExpression(Expression thisExpression, SymbolAtom selector, LineInfo location)
            : base(thisExpression, location)
        {
            Contract.Requires(selector.IsValid);

            Selector = selector;
        }

        /// <nodoc />
        public SelectorExpression(DeserializationContext context, LineInfo location)
            : base(ReadExpression(context), location)
        {
            Selector = context.Reader.ReadSymbolAtom();
        }

        /// <inheritdoc />
        protected override void DoSerialize(BuildXLWriter writer)
        {
            ThisExpression.Serialize(writer);
            writer.Write(Selector);
        }

        /// <inheritdoc />
        public override void Accept(Visitor visitor)
        {
            visitor.Visit(this);
        }

        /// <inheritdoc />
        public override SyntaxKind Kind => SyntaxKind.SelectorExpression;

        /// <inheritdoc />
        public override string ToStringShort(StringTable stringTable)
        {
            return I($"{ThisExpression.ToStringShort(stringTable)}.{Selector.ToString(stringTable)}");
        }

        /// <inheritdoc />
        public override string ToDebugString()
        {
            return I($"{ThisExpression.ToDebugString()}.{ToDebugString(Selector)}");
        }

        /// <inheritdoc />
        protected override EvaluationResult DoEval(Context context, ModuleLiteral env, EvaluationStackFrame frame)
        {
            var receiver = ThisExpression.Eval(context, env, frame);
            if (receiver.IsErrorValue)
            {
                return EvaluationResult.Error;
            }

            if (receiver.IsUndefined)
            {
                context.Errors.ReportFailResolveSelectorDueToUndefined(env, this);
                return EvaluationResult.Error;
            }

            if (receiver.Value is Expression thisExpressionResult)
            {
                if (thisExpressionResult.TryProject(context, Selector, env, out EvaluationResult projectionResult, Location))
                {
                    return projectionResult;
                }

                context.Errors.ReportUnexpectedValueType(
                    env,
                    ThisExpression,
                    receiver, typeof(ObjectLiteral), typeof(ArrayLiteral), typeof(ModuleLiteral));
                return EvaluationResult.Error;
            }

            // Trying to find member function for well-known types.
            var boundMember = ((ModuleRegistry)context.FrontEndHost.ModuleRegistry).PredefinedTypes.ResolveMember(receiver.Value, Selector);

            if (boundMember != null)
            {
                // If bound member represents a property we need to evaluate it.
                return boundMember.IsProperty ? EvaluateAmbientProperty(context, env, boundMember) : EvaluationResult.Create(boundMember);
            }

            context.Errors.ReportMissingProperty(env, Selector, receiver.Value, Location);
            return EvaluationResult.Error;
        }

        private EvaluationResult EvaluateAmbientProperty(Context context, ModuleLiteral env, CallableValue property)
        {
            context.SetPropertyProvenance(env.Path, Location);

            try
            {
                using (var frame = EvaluationStackFrame.Empty())
                {
                    return property.Apply(context, frame);
                }
            }
            catch (EvaluationException e)
            {
                e.ReportError(context.Errors, env, Location, expression: this, context: context);
            }
            catch (OperationCanceledException)
            {
                return EvaluationResult.Canceled;
            }
            catch (Exception exception)
            {
                context.Errors.ReportUnexpectedAmbientException(env, exception, Location);
                throw;
            }
            finally
            {
                context.UnsetPropertyProvenance();
            }

            return EvaluationResult.Error;
        }
    }
}
