// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using System.Globalization;
using System.Linq;
using TypeScript.Net.Types;
using static BuildXL.Utilities.FormattableStringEx;

namespace BuildXL.FrontEnd.Script.RuntimeModel.AstBridge.Rules
{
    /// <summary>
    /// DScript has some additional restrictions for javascript literal numbers
    /// - It doesn't support floating point. All 'number's in DScript are 32-bit integers.
    /// - It doesn't support 'infinity' values on overflow
    /// </summary>
    internal sealed class EnforceNumberLiteralRule : LanguageRule
    {
        private static readonly NumberFormatInfo s_numberFormatInfo = new NumberFormatInfo { NumberDecimalSeparator = "." };

        /// <summary>
        /// Due to ES Spec, decimal separator in JavaScript is '.'.
        /// </summary>
        private const char DecimalSeparator = '.';

        private const string NaN = "NaN";

        private EnforceNumberLiteralRule()
        { }

        public static EnforceNumberLiteralRule CreateAndRegister(AnalysisContext context)
        {
            var result = new EnforceNumberLiteralRule();
            result.Initialize(context);
            return result;
        }

        public override void Initialize(AnalysisContext context)
        {
            context.RegisterSyntaxNodeAction(
                this,
                CheckLiteralExpression,
                TypeScript.Net.Types.SyntaxKind.NumericLiteral);

            context.RegisterSyntaxNodeAction(
                this,
                CheckNotNaN,
                TypeScript.Net.Types.SyntaxKind.Identifier);
        }

        /// <inheritdoc />
        public override RuleAnalysisScope AnalysisScope => RuleAnalysisScope.SpecFile;

        private static void CheckNotNaN(INode node, DiagnosticContext context)
        {
            // Note, that proper solution require binding phase. But because NaN is a global and well-defined
            // name we can warn just base on syntactic equality!
            var identifier = node.Cast<IIdentifier>();
            if (string.Equals(identifier.Text, NaN, StringComparison.Ordinal))
            {
                context.Logger.ReportNotSupportedFloatingPoints(context.LoggingContext, node.LocationForLogging(context.SourceFile));
            }
        }

        private static void CheckLiteralExpression(INode node, DiagnosticContext context)
        {
            var literal = node.Cast<LiteralExpression>().Text;

            // Failing when number literal has decimal separator
            if (literal.ToCharArray().Contains(DecimalSeparator))
            {
                context.Logger.ReportNotSupportedFloatingPoints(context.LoggingContext, node.LocationForLogging(context.SourceFile));
                return;
            }

            // Failing when the number literal overflows
            try
            {
                var result = double.Parse(literal, NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent, s_numberFormatInfo);
                if (Double.IsInfinity(result))
                {
                    throw new OverflowException();
                }
            }
            catch (OverflowException)
            {
                context.Logger.ReportLiteralOverflows(context.LoggingContext, node.LocationForLogging(context.SourceFile), literal);
            }
            catch (FormatException e)
            {
                Contract.Assert(false, I($"Literal expression '{literal}' was not in a correct format. This shouldn't happen at this point. {e.ToString()}"));
            }
        }
    }
}
