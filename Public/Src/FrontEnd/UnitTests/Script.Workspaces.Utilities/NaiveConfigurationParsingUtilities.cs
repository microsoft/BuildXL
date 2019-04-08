// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using BuildXL.Utilities;
using BuildXL.FrontEnd.Script.Constants;
using BuildXL.FrontEnd.Workspaces.Core;
using BuildXL.FrontEnd.Sdk.FileSystem;
using TypeScript.Net.Parsing;
using TypeScript.Net.Reformatter;
using TypeScript.Net.Types;
using static BuildXL.Utilities.FormattableStringEx;

namespace Test.DScript.Workspaces.Utilities
{
    internal static class NaiveConfigurationParsingUtilities
    {
        #region Source resolver field names

        public const string RootFieldName = "root";

        // TODO: this should be eventually renamed to "modules"
        public const string PackagesFieldName = "packages";

        #endregion

        #region NuGet resolver field names

        public const string ConfigurationFieldName = "configuration";
        public const string CredentialProvidersFieldName = "credentialProviders";
        public const string ToolUrlFieldName = "toolUrl";
        public const string HashFieldName = "hash";

        public const string RepositoriesFieldName = "repositories";

        // public const string PackagesFieldName = "packages";
        public const string IdFieldName = "id";
        public const string VersionFieldName = "version";
        public const string AliasFieldName = "alias";

        public const string DoNotEnforceDependencyVersionsFieldName = "doNotEnforceDependencyVersions";

        #endregion

        #region Module configuration field names

        public const string ModuleFileName = "package" + Names.DotDscExtension;
        public const string NameFieldName = "name";
        public const string NameResolutionSemanticsFieldName = "nameResolutionSemantics";
        public const string ImplicitProjectReferences = "NameResolutionSemantics.implicitProjectReferences";
        public const string MainFieldName = "main";
        public const string ProjectsFieldName = "projects";

        #endregion

        #region Primary configuration field names

        public const string ConfigFileName = Names.ConfigDsc;
        public const string ResolversFieldName = "resolvers";

        #endregion

        #region Prelude constants

        /// <summary>
        /// Name of the designated module that acts as the prelude
        /// </summary>
        public const string PreludeModuleName = "Sdk.Prelude";

        #endregion

        /// <nodoc/>
        public static readonly string[] AbsolutePathLiteralPrefixes = { "d", "f", "p" };

        /// <summary>
        /// Extracts the resolver initializer expression that was specified for a configuration function.
        /// </summary>
        public static bool TryExtractResolversPropertyFromConfiguration(
            this ISourceFile sourceFile,
            out IExpression resolvers, out string failureReason)
        {
            resolvers = null;

            IObjectLiteralExpression literal;
            if (!sourceFile.TryExtractConfigurationLiteral(out literal, out failureReason))
            {
                return false;
            }

            if (
                !literal.TryFindAssignmentPropertyInitializer(
                    ResolversFieldName,
                    out resolvers))
            {
                failureReason = I($"There must be a field '{ResolversFieldName}' initialized with a collection of resolvers.");
                return false;
            }

            return true;
        }

        /// <summary>
        /// Attempts to create an AbsolutePath by combining the base and relative paths
        /// </summary>
        /// <param name="pathTable">The path table to operate with.</param>
        /// <param name="basePath">The base absolute path to use.</param>
        /// <param name="scriptPathLiteral">The path string literal. This must start with DScript-style f`, d`, p` delimiters</param>
        /// <param name="absolutePath">The resulting path</param>
        /// <remarks>
        /// The format expected of <paramref name="scriptPathLiteral"/> is how
        /// DScript full paths are specified: as fpd`relative-path`.
        /// The relative-path is then combined with the <paramref name="basePath"/> to construct the AbsolutePath.
        ///
        /// WARNING: Without an evaluator, this function will be always be brittle, so use it sparingly
        /// and only for well-defined cases.
        /// </remarks>
        public static bool TryCreateAbsolutePath(PathTable pathTable, AbsolutePath basePath, string scriptPathLiteral, out AbsolutePath absolutePath)
        {
            Contract.Requires(scriptPathLiteral.EndsWith("`", StringComparison.Ordinal));
            Contract.Requires(AbsolutePathLiteralPrefixes.Any(prefix => scriptPathLiteral.StartsWith(prefix + "`", StringComparison.Ordinal)));

            absolutePath = AbsolutePath.Invalid;

            // Remove d``, f``, p``
            string relativePathString = scriptPathLiteral.Substring(2, scriptPathLiteral.Length - 3);

            // Create RelativePath from relativePathString
            RelativePath relativePath = RelativePath.Invalid;
            if (!RelativePath.TryCreate(pathTable.StringTable, relativePathString, out relativePath))
            {
                return false;
            }

            // Make it absolute wrt the base path
            absolutePath = basePath.Combine(pathTable, relativePath);
            return true;
        }

        /// <summary>
        /// Tries to parse the input call expression <param name="callExpression"/> into a GlobDescriptor object.
        /// </summary>
        /// <remarks>
        /// The <param name="basePath"/> and <param name="pathTable"/> must be provided to compute the absolute path of the
        /// 'root' parameter in the call expression.
        /// </remarks>
        public static Possible<GlobDescriptor> TryCreateGlobDescriptorFromCallExpression(
            ICallExpression callExpression,
            AbsolutePath basePath,
            PathTable pathTable)
        {
            var functionName = callExpression.Expression.Cast<IIdentifier>();
            if (!functionName.IsGlob())
            {
                return new MalformedGlobExpressionFailure(I($"Expecting glob/globR function but got '{functionName.GetText()}'."));
            }

            if ((callExpression.Arguments?.Count ?? 0) == 0)
            {
                return new MalformedGlobExpressionFailure("Glob function should take at least one argument but got 0.");
            }

            // Parse the 'root' param
            var root = AbsolutePath.Invalid;
            var pathExpressionString = callExpression.Arguments[0].GetText();

            if (!TryCreateAbsolutePath(
                    pathTable,
                    basePath,
                    pathExpressionString,
                    out root))
            {
                return new MalformedGlobExpressionFailure(I($"Malformed path expression '{pathExpressionString}'."));
            }

            // Parse the 'pattern' param. If none is provided, it is defaulted to "*"
            var pattern = callExpression.Arguments.Count > 1 ? callExpression.Arguments[1].Cast<IStringLiteral>().Text : "*";

            // Determine if glob is recursive
            var recursive = functionName.IsGlobRecursive();

            return new GlobDescriptor(root, pattern, recursive);
        }


    }
}
