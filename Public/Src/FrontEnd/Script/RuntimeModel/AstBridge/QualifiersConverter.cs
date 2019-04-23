// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using BuildXL.Utilities.Qualifier;
using TypeScript.Net.Types;
using QualifierSpaceDeclaration = System.Collections.Generic.Dictionary<string, System.Collections.Generic.IReadOnlyList<string>>;

namespace BuildXL.FrontEnd.Script.RuntimeModel.AstBridge
{
    /// <summary>
    /// Helper class that is responsible for converting qualifiers.
    /// </summary>
    internal sealed class QualifiersConverter
    {
        private readonly AstConversionContext m_conversionContext;

        private RuntimeModelContext RuntimeModelContext => m_conversionContext.RuntimeModelContext;

        private DiagnosticContext DiagnosticContext
            =>
                new DiagnosticContext(
                    m_conversionContext.CurrentSourceFile,
                    RuleAnalysisScope.SpecFile,
                    m_conversionContext.Logger,
                    m_conversionContext.LoggingContext,
                    m_conversionContext.PathTable,
                    workspace: null);

        /// <nodoc />
        public QualifiersConverter(AstConversionContext conversionContext)
        {
            Contract.Requires(conversionContext != null);

            m_conversionContext = conversionContext;
        }
        
        private static QualifierSpaceDeclaration ExtractQualifierSpace(IObjectLiteralExpression objectLiteral, DiagnosticContext context, bool emitLogEvents)
        {
            QualifierSpaceDeclaration result = new QualifierSpaceDeclaration();
            bool hasErrors = false;
            foreach (var objectLiteralElement in objectLiteral.Properties)
            {
                switch (objectLiteralElement.Kind)
                {
                    case TypeScript.Net.Types.SyntaxKind.PropertyAssignment:
                    {
                        var propertyAssignment = objectLiteralElement.Cast<IPropertyAssignment>();
                        var initializer = propertyAssignment.Initializer;
                        var valueSpace = initializer.As<IArrayLiteralExpression>();
                        if (valueSpace == null || valueSpace.Elements.Count == 0)
                        {
                            if (emitLogEvents)
                            {
                                context.Logger.ReportQualifierSpacePossibleValuesMustBeNonEmptyArrayLiteral(
                                    context.LoggingContext,
                                    initializer.LocationForLogging(context.SourceFile));
                            }

                            hasErrors = true;
                        }
                        else
                        {
                            var values = ExtractQualifierSpaceValues(valueSpace, context, emitLogEvents);
                            if (values == null)
                            {
                                hasErrors = true;
                            }
                            else
                            {
                                var text = propertyAssignment.Name.Text;
                                if (!QualifierTable.IsValidQualifierKey(text))
                                {
                                    if (emitLogEvents)
                                    {
                                        context.Logger.ReportQualifierSpaceValueMustBeValidKey(context.LoggingContext, propertyAssignment.LocationForLogging(context.SourceFile), text);
                                    }

                                    hasErrors = true;
                                }
                                else
                                {
                                    result.Add(propertyAssignment.Name.Text, values);
                                }
                            }
                        }

                        break;
                    }

                    case TypeScript.Net.Types.SyntaxKind.ShorthandPropertyAssignment:
                    {
                        if (emitLogEvents)
                        {
                            context.Logger.ReportQualifierSpacePropertyCannotBeInShorthand(
                                context.LoggingContext,
                                objectLiteralElement.LocationForLogging(context.SourceFile));
                        }

                        hasErrors = true;
                        break;
                    }
                }
            }

            return hasErrors ? null : result;
        }

        private static List<string> ExtractQualifierSpaceValues(IArrayLiteralExpression valueSpace, DiagnosticContext context, bool emitLogEvents)
        {
            var values = new List<string>(valueSpace.Elements.Count);
            bool hasErrors = false;
            foreach (var value in valueSpace.Elements)
            {
                if (value.Kind != TypeScript.Net.Types.SyntaxKind.StringLiteral)
                {
                    if (emitLogEvents)
                    {
                        context.Logger.ReportQualifierSpaceValueMustBeStringLiteral(context.LoggingContext, value.LocationForLogging(context.SourceFile));
                    }

                    hasErrors = true;
                }

                var text = value.Cast<IStringLiteral>().Text;
                if (!QualifierTable.IsValidQualifierValue(text))
                {
                    if (emitLogEvents)
                    {
                        context.Logger.ReportQualifierSpaceValueMustBeValidValue(context.LoggingContext, value.LocationForLogging(context.SourceFile), text);
                    }

                    hasErrors = true;
                }

                values.Add(text);
            }

            return hasErrors ? null : values;
        }

        private QualifierSpaceId CreateQualifierSpaceId(QualifierSpaceDeclaration qualifierSpaceDeclaration)
        {
            if (qualifierSpaceDeclaration.Count == 0)
            {
                return RuntimeModelContext.QualifierTable.EmptyQualifierSpaceId;
            }

            return RuntimeModelContext.QualifierTable.CreateQualifierSpace(qualifierSpaceDeclaration);
        }
    }
}
