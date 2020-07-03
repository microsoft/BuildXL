// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using BuildXL.FrontEnd.Script.Evaluator;
using BuildXL.FrontEnd.Script.Util;
using BuildXL.FrontEnd.Script.Values;
using BuildXL.Utilities;
using BuildXL.Utilities.Qualifier;
using static BuildXL.Utilities.FormattableStringEx;
using LineInfo = TypeScript.Net.Utilities.LineInfo;

namespace BuildXL.FrontEnd.Script.Expressions
{
    /// <summary>
    /// Represents 'withQualifier' method invocation expression.
    /// </summary>
    /// <remarks>
    /// DScript V2 allows to 'qualify' namespaces.
    /// To do that, every namespace (including implicit top-level namespace) has a withQualifier function that returns a qualified namespace.
    /// This expression type represents these expressions and performs qualifier coercion at runtime.
    /// </remarks>
    public sealed class WithQualifierExpression : Expression
    {
        /// <summary>
        /// Expression that represents a reference to a namespace.
        /// </summary>
        public Expression ModuleReference { get; }

        /// <summary>
        /// Expression that results in a qualifier object literal.
        /// </summary>
        public Expression QualifierExpression { get; }

        /// <nodoc/>
        public QualifierSpaceId SourceQualifierSpaceId { get; }

        /// <nodoc/>
        public QualifierSpaceId TargetQualifierSpaceId { get; }

        /// <nodoc/>
        public WithQualifierExpression(Expression moduleReference, Expression qualifierExpression, QualifierSpaceId sourceQualifierSpaceId, QualifierSpaceId targetQualifierSpaceId, LineInfo location)
            : base(location)
        {
            Contract.Requires(moduleReference != null, "moduleReference != null");
            Contract.Requires(qualifierExpression != null, "qualifierExpression != null");

            ModuleReference = moduleReference;
            QualifierExpression = qualifierExpression;
            SourceQualifierSpaceId = sourceQualifierSpaceId;
            TargetQualifierSpaceId = targetQualifierSpaceId;
        }

        /// <nodoc />
        public WithQualifierExpression(DeserializationContext context, LineInfo location)
            : base(location)
        {
            ModuleReference = ReadExpression(context);
            QualifierExpression = ReadExpression(context);
            SourceQualifierSpaceId = context.Reader.ReadQualifierSpaceId();
            TargetQualifierSpaceId = context.Reader.ReadQualifierSpaceId();
        }

        /// <inheritdoc />
        protected override void DoSerialize(BuildXLWriter writer)
        {
            ModuleReference.Serialize(writer);
            QualifierExpression.Serialize(writer);

            writer.Write(SourceQualifierSpaceId);
            writer.Write(TargetQualifierSpaceId);
        }

        /// <inheritdoc />
        public override void Accept(Visitor visitor)
        {
            // Intentionally left blank
        }

        /// <inheritdoc />
        public override SyntaxKind Kind => SyntaxKind.WithQualifierExpression;

        /// <inheritdoc />
        public override string ToDebugString()
        {
            return I($"{ModuleReference.ToDebugString()}.withQualifier({QualifierExpression.ToDebugString()})");
        }

        /// <inheritdoc />
        protected override EvaluationResult DoEval(Context context, ModuleLiteral env, EvaluationStackFrame frame)
        {
            // There is some code duplication between this type and the CoerceQualifierTypeExpression.
            // But there is not clear how to reuse this because steps are 'slightly' different.
            var moduleCandidate = ModuleReference.Eval(context, env, frame);

            if (moduleCandidate.IsErrorValue || moduleCandidate.IsUndefined)
            {
                return moduleCandidate;
            }

            // The type checker should make sure that 'this expression' evaluates to a module literal.
            var module = moduleCandidate.Value as ModuleLiteral;
            Contract.Assert(
                module != null,
                I($"The left hand-side of a withQualifier expression should evaluates to 'TypeOrNamespaceModuleLiteral' but got '{moduleCandidate.Value.GetType()}'"));

            Contract.Assert(module.CurrentFileModule != null, "module.CurrentFileModule != null");
            var currentQualifier = env.CurrentFileModule.Qualifier.Qualifier;

            // QualifierExpression can be an object literal or anything that ended up as an object literal.
            EvaluationResult objectQualifier;
            using (var emptyFrame = EvaluationStackFrame.Empty())
            {
                objectQualifier = QualifierExpression.Eval(context, env, emptyFrame);
            }

            if (objectQualifier.IsErrorValue)
            {
                // Error has been reported.
                return EvaluationResult.Error;
            }

            var requestedQualifier = objectQualifier.Value as ObjectLiteral;
            Contract.Assert(
                requestedQualifier != null,
                I($"The right hand-side of a withQualifier expression should evaluates to 'ObjectLiteral' but got '{objectQualifier.Value.GetType()}'"));

            // TODO: This can be made more efficient by talking with the qualifier table directly
            // and maintaining a global map of qualifier id to object literal rather than have many copies of
            // object literal floating around, but that would be more changes than warranted at the moment
            // since withqualifier is not used that heavily at the moment, when this starts showing up on profiles
            // we should consider improving the logic here.
            var qualifierBindings = new Dictionary<StringId, Binding>();
            foreach (var member in currentQualifier.Members)
            {
                qualifierBindings[member.Key] = new Binding(member.Key, member.Value, requestedQualifier.Location);
            }

            foreach (var member in requestedQualifier.Members)
            {
                if (member.Value.IsUndefined)
                {
                    // setting a value to undefined implies explicitly removing they qualifier key.
                    qualifierBindings.Remove(member.Key);
                }
                else
                {
                    qualifierBindings[member.Key] = new Binding(member.Key, member.Value, requestedQualifier.Location); 
                }
            }
            var qualifierToUse = ObjectLiteral.Create(qualifierBindings.Values.ToArray());
            

            if (!QualifierValue.TryCreate(context, env, qualifierToUse, out QualifierValue qualifierValue, requestedQualifier.Location))
            {
                // Error has been reported.
                return EvaluationResult.Error;
            }

            // Coercing qualifier with a given value
            if (
                !QualifierUtilities.CoerceQualifierValueForV2(
                    context,
                    qualifierValue,
                    SourceQualifierSpaceId,
                    TargetQualifierSpaceId,
                    referencingLocation: Location.AsUniversalLocation(env, context),
                    referencedLocation: module.CurrentFileModule.Location.AsUniversalLocation(module.CurrentFileModule, context),
                    coercedQualifierValue: out QualifierValue coercedQualifierValue))
            {
                // Error has been reported
                return EvaluationResult.Error;
            }

            var result = module.Instantiate(context.ModuleRegistry, coercedQualifierValue);
            return EvaluationResult.Create(result);
        }
    }
}
