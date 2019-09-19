// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Runtime.Serialization;
using BuildXL.FrontEnd.Script.Declarations;
using JetBrains.Annotations;
using TypeScript.Net;
using TypeScript.Net.Reformatter;
using TypeScript.Net.Types;
using static BuildXL.Utilities.FormattableStringEx;
using Expression = BuildXL.FrontEnd.Script.Expressions.Expression;

#pragma warning disable SA1649 // File name must match first type name

namespace BuildXL.FrontEnd.Script.RuntimeModel.AstBridge
{
    /// <summary>
    /// Exception that would be thrown when conversion from TypeScript AST to evaluation AST fails.
    /// </summary>
    [Serializable]
    internal sealed class ConversionException : Exception
    {
        public ConversionException(string message)
            : base(message)
        {
        }

        public ConversionException(string message, Exception e)
            : base(message, e)
        {
        }

        public ConversionException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        { }
    }

    internal static class StringExtensions
    {
        public static string SubstringMax(this string str, int startIndex, int max)
        {
            return str.Substring(startIndex, Math.Min(str.Length, max));
        }
    }

    internal sealed class AstConverterWithExceptionWrappingDecorator : IAstConverter
    {
        private const int MaxSourceLength = 512;

        private readonly AstConversionContext m_conversionContext;

        private readonly IAstConverter m_decoratee;

        public AstConverterWithExceptionWrappingDecorator(AstConversionContext conversionContext, IAstConverter decoratee)
        {
            m_conversionContext = conversionContext;
            m_decoratee = decoratee;
        }

        public ConfigurationDeclaration ConvertConfiguration()
        {
            try
            {
                return m_decoratee.ConvertConfiguration();
            }
            catch (Exception e)
            {
                string sourceFragment = m_conversionContext.CurrentSourceFile.Text.SubstringFromTo(0, MaxSourceLength);

                string message = "Failed to convert configuration.\r\n" + sourceFragment;
                throw new ConversionException(message, e);
            }
        }

        public PackageDeclaration ConvertPackageConfiguration()
        {
            try
            {
                return m_decoratee.ConvertPackageConfiguration();
            }
            catch (Exception e)
            {
                string sourceFragment = m_conversionContext.CurrentSourceFile.Text.SubstringFromTo(0, MaxSourceLength);

                string message = "Failed to convert package configuration.\r\n" + sourceFragment;
                throw new ConversionException(message, e);
            }
        }

        public SourceFileParseResult ConvertSourceFile()
        {
            try
            {
                return m_decoratee.ConvertSourceFile();
            }
            catch (Exception e)
            {
                string sourceFragment = m_conversionContext.CurrentSourceFile.Text.SubstringFromTo(0, MaxSourceLength);

                string message =
                   I($"Failed to convert source file '{m_conversionContext.CurrentSpecPath.ToString(m_conversionContext.PathTable)}'.\r\n{sourceFragment}");

                throw new ConversionException(message, e);
            }
        }

        public Expression ConvertExpression(ICallExpression node, FunctionScope localScope, bool useSemanticNameResolution)
        {
            try
            {
                return m_decoratee.ConvertExpression(node, localScope, useSemanticNameResolution);
            }
            catch (Exception e)
            {
                string sourceFragment = node.GetText().SubstringMax(0, MaxSourceLength);

                string message = "Failed to convert expression.\r\n" + sourceFragment;
                throw new ConversionException(message, e);
            }
        }
    }

    /// <summary>
    /// Interface for building current envaluation AST from parsed AST.
    /// </summary>
    internal interface IAstConverter
    {
        /// <summary>
        /// Converts source file as config.dsc file.
        /// </summary>
        /// <returns>
        /// Returns processed configuration or null in case of error.
        /// </returns>
        [CanBeNull]
        ConfigurationDeclaration ConvertConfiguration();

        /// <summary>
        /// Converts package configuration file.
        /// </summary>
        /// <returns>
        /// Returns processed configuration or null in case of error.
        /// </returns>
        [CanBeNull]
        PackageDeclaration ConvertPackageConfiguration();

        /// <summary>
        /// Converts source file.
        /// </summary>
        [JetBrains.Annotations.NotNull]
        SourceFileParseResult ConvertSourceFile();

        /// <summary>
        /// Converts node to expression.
        /// </summary>
        /// <returns>
        /// Returns converted expression or null in case of error.
        /// A local scope can be passed to evaluate the expression in that context
        /// </returns>
        /// <remarks>
        /// This method is used in two places: by tests and by debugger.
        /// The tests are relying on semantic-based resolution, but the debugging requires different approach.
        /// </remarks>
        [CanBeNull]
        Expression ConvertExpression(ICallExpression node, FunctionScope localScope, bool useSemanticNameResolution);
    }
}
