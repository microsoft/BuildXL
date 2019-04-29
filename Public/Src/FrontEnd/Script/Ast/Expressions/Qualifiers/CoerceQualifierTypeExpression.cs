// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.ContractsLight;
using BuildXL.FrontEnd.Script.Evaluator;
using BuildXL.FrontEnd.Script.RuntimeModel.AstBridge;
using BuildXL.FrontEnd.Script.Util;
using BuildXL.FrontEnd.Script.Values;
using BuildXL.Utilities;
using BuildXL.Utilities.Qualifier;
using static BuildXL.Utilities.FormattableStringEx;
using LineInfo = TypeScript.Net.Utilities.LineInfo;

namespace BuildXL.FrontEnd.Script.Expressions
{
    /// <summary>
    /// Expression that coerces qualifier types from the target expression with a given one.
    /// </summary>
    /// <remarks>
    /// When execution crosses namespace boundaries (explicitely via 'const x = A.B.value' or implicitely via 'const x = valueFromEnclosingNamespace')
    /// then qualifier type coercion should happen.
    /// </remarks>
    public sealed class CoerceQualifierTypeExpression : Expression
    {
        /// <summary>
        /// Whether coercion should use defaults
        /// </summary>
        public bool ShouldUseDefaultsOnCoercion { get; }

        /// <summary>
        /// Location that references the target expression
        /// </summary>
        public UniversalLocation ReferencedLocation { get; }

        /// <summary>
        /// Expression that evaluates first before qualifier type coercion happens.
        /// </summary>
        /// <remarks>
        /// The result of this expression should be a <see cref="ModuleLiteral"/>, because only
        /// namespaces can be used as an argument for qualifier type coercion.
        /// </remarks>
        public Expression TargetExpression { get; }

        /// <summary>
        /// Target qualifier space id that needs to be coerced with a qualifier type of the target expression.
        /// </summary>
        public QualifierSpaceId TargetQualifierSpaceId { get; }

        /// <nodoc/>
        public CoerceQualifierTypeExpression(Expression targetExpression, QualifierSpaceId targetQualifierSpaceId, bool shouldUseDefault, LineInfo referencingLocation, UniversalLocation referencedLocation)
            : base(referencingLocation)
        {
            Contract.Requires(targetExpression != null, "targetExpression != null");
            Contract.Requires(targetQualifierSpaceId.IsValid, "targetQualifierSpaceId.IsValid");

            TargetExpression = targetExpression;
            TargetQualifierSpaceId = targetQualifierSpaceId;
            ShouldUseDefaultsOnCoercion = shouldUseDefault;
            ReferencedLocation = referencedLocation;
        }

        /// <summary>
        /// Constructor which deserializes from the given DeserializationContext.
        /// </summary>
        public CoerceQualifierTypeExpression(DeserializationContext context, LineInfo location)
            : base(location)
        {
            TargetExpression = ReadExpression(context);
            TargetQualifierSpaceId = context.Reader.ReadQualifierSpaceId();
            ShouldUseDefaultsOnCoercion = context.Reader.ReadBoolean();
            ReferencedLocation = new UniversalLocation(context, location);
        }

        /// <summary>
        /// Serializes into the given writer.
        /// </summary>
        protected override void DoSerialize(BuildXLWriter writer)
        {
            TargetExpression.Serialize(writer);

            writer.Write(TargetQualifierSpaceId);
            writer.Write(ShouldUseDefaultsOnCoercion);

            // Force computation of line and column. When deserializing,
            // the wrong file map will be available.
            ReferencedLocation.DoSerialize(writer, forceLineAndColumn: true);
        }

        /// <inheritdoc />
        public override void Accept(Visitor visitor)
        {
            // Intentionally left blank
        }

        /// <inheritdoc />
        public override SyntaxKind Kind => SyntaxKind.CoerceQualifierTypeExpression;

        /// <inheritdoc />
        public override string ToDebugString()
        {
            return I($"coerceQualifier({TargetExpression.ToDebugString()}, {TargetQualifierSpaceId})");
        }

        /// <inheritdoc />
        protected override EvaluationResult DoEval(Context context, ModuleLiteral env, EvaluationStackFrame frame)
        {
            var moduleCandidate = TargetExpression.Eval(context, env, frame);
            if (moduleCandidate.IsErrorValue || moduleCandidate.IsUndefined)
            {
                return moduleCandidate;
            }

            // Qualifier type coercion could happen on both namespaces and files when importFrom is used.
            var module = moduleCandidate.Value as ModuleLiteral;

            // Moving Assert into the if block to avoid relatively expensive message computation for non-error case.
            if (module == null)
            {
                Contract.Assert(
                false,
                I($"TargetExpression '{TargetExpression.ToDisplayString(context)}' should produce 'ModuleLiteral' but produced '{moduleCandidate.Value.GetType()}'"));
            }

            var pathTable = context.FrontEndContext.PathTable;

            // TODO: Consider if it is possible to use QualifierUtilities.CoerceQualifierValue instead.
            QualifierValue oldQualifierValue = module.Qualifier;
            if (
                !oldQualifierValue.TryCoerce(
                    TargetQualifierSpaceId,
                    context.FrontEndContext.QualifierTable,
                    context.QualifierValueCache,
                    pathTable,
                    context.FrontEndContext.StringTable,
                    context.FrontEndContext.LoggingContext,
                    out QualifierValue coercedQualifier,
                    Location,
                    ShouldUseDefaultsOnCoercion,
                    context.LastActiveUsedPath))
            {
                context.Errors.ReportQualifierCannotBeCoarcedToQualifierSpaceWithProvenance(
                    oldQualifierValue.QualifierId,
                    TargetQualifierSpaceId,
                    ReferencedLocation.AsLoggingLocation(),
                    Location.AsLoggingLocation(env, context));

                return EvaluationResult.Error;
            }

            return EvaluationResult.Create(module.Instantiate(context.ModuleRegistry, coercedQualifier));
        }
    }
}
