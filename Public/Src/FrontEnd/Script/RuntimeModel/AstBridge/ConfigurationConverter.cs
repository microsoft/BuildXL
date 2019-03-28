// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.FrontEnd.Script.Constants;
using BuildXL.FrontEnd.Script.Tracing;
using TypeScript.Net.Extensions;
using TypeScript.Net.Parsing;
using TypeScript.Net.Types;
using static BuildXL.Utilities.FormattableStringEx;

namespace BuildXL.FrontEnd.Script.RuntimeModel.AstBridge
{
    /// <summary>
    /// Helper class
    /// </summary>
    internal static class ConfigurationConverter
    {
        [Pure]
        public static bool IsConfigurationDeclaration(this IStatement statement)
        {
            Contract.Requires(statement != null);

            return statement.IsFunctionCallDeclaration(Names.ConfigurationFunctionCall);
        }

        /// <summary>
        /// Extracts object literal that was specified for configuration function.
        /// This method assumes that source was already validated and contains correct state.
        /// </summary>
        public static IObjectLiteralExpression ExtractConfigurationLiteral(ISourceFile sourceFile)
        {
            // This method should have this precondition, but to avoid additional wasteful work, this precondition is missing, but assumed.
            // Contract.Requires(ValidateRootConfiguration(sourceFile));
            Contract.Ensures(Contract.Result<IObjectLiteralExpression>() != null);

            var configurationStatement = sourceFile.Statements[0];

            var callExpression = configurationStatement.AsCallExpression();

            return callExpression.Arguments[0].Cast<IObjectLiteralExpression>();
        }

        /// <summary>
        /// Validates configuration file.
        /// </summary>
        public static bool ValidateRootConfiguration(ISourceFile sourceFile, Logger logger, LoggingContext loggingContext)
        {
            if (sourceFile.Statements.Count != 1)
            {
                logger.ReportOnlyASingleFunctionCallInConfigurationFile(loggingContext, sourceFile.LocationForLogging(sourceFile));
                return false;
            }

            var configurationStatement = sourceFile.Statements[0];

            if (!configurationStatement.IsConfigurationDeclaration())
            {
                logger.ReportUnknownStatementInConfigurationFile(loggingContext, configurationStatement.LocationForLogging(sourceFile));
                return false;
            }

            var callExpression = configurationStatement.AsCallExpression();

            if (callExpression.Arguments.Count != 1)
            {
                string message = I($"Configuration expression should take 1 argument but got {callExpression.Arguments.Count}");
                logger.ReportInvalidConfigurationFileFormat(loggingContext, callExpression.LocationForLogging(sourceFile), message);
                return false;
            }

            var objectLiteral = callExpression.Arguments[0].As<IObjectLiteralExpression>();
            if (objectLiteral == null)
            {
                string message = I($"Configuration expression should take object literal but got {callExpression.Arguments[0].Kind}");
                logger.ReportInvalidConfigurationFileFormat(loggingContext, callExpression.LocationForLogging(sourceFile), message);
                return false;
            }

            return true;
        }

        /// <summary>
        /// Extracts object literal that was specified for package configuration function.
        /// </summary>
        public static IReadOnlyList<IObjectLiteralExpression> ExtractPackageConfigurationLiterals(ISourceFile sourceFile)
        {
            // This method should have this precondition, but to avoid additional wasteful work, this precondition is missing, but assumed.
            // Contract.Requires(ValidateRootConfiguration(sourceFile));
            Contract.Ensures(Contract.Result<IReadOnlyList<IObjectLiteralExpression>>() != null);
            Contract.Ensures(Contract.Result<IReadOnlyList<IObjectLiteralExpression>>().Count != 0);

            return sourceFile.Statements.Select(
                        s => s.AsCallExpression().Arguments[0].As<IObjectLiteralExpression>())
                    .ToList();
        }

        /// <summary>
        /// Validates <paramref name="sourceFile"/> to follow package configuration structure.
        /// </summary>
        public static bool ValidatePackageConfiguration(ISourceFile sourceFile, Logger logger, LoggingContext loggingContext)
        {
            if (sourceFile.Statements.Count < 1)
            {
                logger.ReportAtLeastSingleFunctionCallInPackageConfigurationFile(loggingContext, sourceFile.LocationForLogging(sourceFile));
                return false;
            }

            var objectLiteralExpressions = new IObjectLiteralExpression[sourceFile.Statements.Count];

            bool hasErrors = false;
            for (int i = 0; i < sourceFile.Statements.Count; ++i)
            {
                var configurationStatement = sourceFile.Statements[i];
                if (!configurationStatement.IsPackageConfigurationDeclaration())
                {
                    logger.ReportUnknownStatementInPackageConfigurationFile(
                        loggingContext,
                        configurationStatement.LocationForLogging(sourceFile));
                    hasErrors = true;
                    continue;
                }

                var callExpression = configurationStatement.AsCallExpression();

                if (callExpression.Arguments.Count != 1)
                {
                    string message = I($"Package configuration should take 1 argument but got {callExpression.Arguments.Count}");
                    logger.ReportInvalidPackageConfigurationFileFormat(
                        loggingContext,
                        callExpression.LocationForLogging(sourceFile),
                        message);
                    hasErrors = true;
                    continue;
                }

                var objectLiteral = callExpression.Arguments[0].As<IObjectLiteralExpression>();
                if (objectLiteral == null)
                {
                    string message = I($"Package configuration should take object literal but got {callExpression.Arguments[0].Kind}");
                    logger.ReportInvalidPackageConfigurationFileFormat(
                        loggingContext,
                        callExpression.LocationForLogging(sourceFile),
                        message);
                    hasErrors = true;
                    continue;
                }

                objectLiteralExpressions[i] = objectLiteral;
            }

            return hasErrors;
        }
    }
}
