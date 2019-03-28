// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

// TODO: Remove old namespace after migrating OSGTools
// https://microsoft.visualstudio.com/_git/OSGTools?path=%2Fsrc%2FMicrosoft%2FInternal%2FWindows%2FTools%2FModularBuild%2FNative2Pbxproj%2FExe%2FModels%2FTypescriptParser.cs&version=GBmaster
// to new namespace, and change ParsingOptions back to sealed.
namespace TypeScript.Net.BuildXLScript
{
    /// <summary>
    /// DScript-specific parsing options for the TypeScript parser
    /// </summary>
    public sealed class ParsingOptions : TypeScript.Net.DScript.ParsingOptions
    {
        /// <nodoc/>
        public ParsingOptions(
            bool namespacesAreAutomaticallyExported,
            bool generateWithQualifierFunctionForEveryNamespace,
            bool preserveTrivia,
            bool allowBackslashesInPathInterpolation,
            bool useSpecPublicFacadeAndAstWhenAvailable,
            bool escapeIdentifiers,
            bool failOnMissingSemicolons = false,
            bool collectImportFile = false,
            bool convertPathLikeLiteralsAtParseTime = true)
            : base(
                namespacesAreAutomaticallyExported,
                generateWithQualifierFunctionForEveryNamespace,
                preserveTrivia,
                allowBackslashesInPathInterpolation,
                useSpecPublicFacadeAndAstWhenAvailable,
                escapeIdentifiers,
                failOnMissingSemicolons,
                collectImportFile,
                convertPathLikeLiteralsAtParseTime)
        {
        }
    }
}

namespace TypeScript.Net.DScript
{
    /// <summary>
    /// DScript-specific parsing options for the TypeScript parser
    /// </summary>
    public /*TODO: sealed */ class ParsingOptions
    {
        /// <nodoc/>
        public ParsingOptions(
            bool namespacesAreAutomaticallyExported,
            bool generateWithQualifierFunctionForEveryNamespace,
            bool preserveTrivia,
            bool allowBackslashesInPathInterpolation,
            bool useSpecPublicFacadeAndAstWhenAvailable,
            bool escapeIdentifiers,
            bool failOnMissingSemicolons = false,
            bool collectImportFile = false,
            bool convertPathLikeLiteralsAtParseTime = true)
        {
            NamespacesAreAutomaticallyExported = namespacesAreAutomaticallyExported;
            GenerateWithQualifierFunctionForEveryNamespace = generateWithQualifierFunctionForEveryNamespace;
            PreserveTrivia = preserveTrivia;
            AllowBackslashesInPathInterpolation = allowBackslashesInPathInterpolation;
            UseSpecPublicFacadeAndAstWhenAvailable = useSpecPublicFacadeAndAstWhenAvailable;
            EscapeIdentifiers = escapeIdentifiers;
            FailOnMissingSemicolons = failOnMissingSemicolons;
            CollectImportFile = collectImportFile;
            ConvertPathLikeLiteralsAtParseTime = convertPathLikeLiteralsAtParseTime;
        }

        /// <summary>
        /// If true, then such literals like p``, d``, f`` etc would be converted to internal BuildXL representation at parse time.
        /// </summary>
        public bool ConvertPathLikeLiteralsAtParseTime { get; }

        /// <summary>
        /// Temporary flag that controls whether double underscore identifiers are escaped (e.g. __test).
        /// </summary>
        /// <remarks>
        /// Escaping is the TypeScript behavior, but due to a bug we need to incrementally fix this, and therefore we need this flag.
        /// </remarks>
        public bool EscapeIdentifiers { get; }

        /// <summary>
        /// Whether public surface and serialized AST of specs should be used when available
        /// </summary>
        public bool UseSpecPublicFacadeAndAstWhenAvailable { get; }

        /// <summary>
        /// Whether namespaces are automatically exported when any of its members are exported
        /// </summary>
        public bool NamespacesAreAutomaticallyExported { get; }

        /// <summary>
        /// Whether function 'withQualifier' is generated for every namespace. This is a V2 feature.
        /// </summary>
        public bool GenerateWithQualifierFunctionForEveryNamespace { get; }

        /// <summary>
        /// Back-compat flag to keep the legacy escaping for path interpolation
        /// </summary>
        /// <remarks>
        /// This is mainly Office-specific. TODO: remove when Office is moved to V2
        /// </remarks>
        public bool AllowBackslashesInPathInterpolation { get; }

        /// <summary>
        /// Whether the scanner skips trivia (spaces, tabs, etc.)
        /// </summary>
        public bool PreserveTrivia { get; }

        /// <summary>
        /// Whether to collect specifiers inside of 'importFile' calls (in addition to collecting specifiers inside 'importFrom' calls).
        /// </summary>
        /// <remarks>
        /// Only used for parsing config files, because they are allowed to use 'importFile' without
        /// explicitly specifying all the files that are necessary for processing the config.
        /// </remarks>
        public bool CollectImportFile { get; }

        /// <summary>
        /// Whether to fail if the node does not ends with a semicolon.
        /// </summary>
        /// <remarks>
        /// Originally the validation happened in the lint-rule but was moved to the parsing phase for performance reasons.
        /// </remarks>
        public bool FailOnMissingSemicolons { get; }

        /// <nodoc />
        public ParsingOptions WithGenerateWithQualifierFunctionForEveryNamespace(bool generateWithQualifierFunctionForEveryNamespace)
        {
            return new ParsingOptions(
                namespacesAreAutomaticallyExported: NamespacesAreAutomaticallyExported,
                generateWithQualifierFunctionForEveryNamespace: generateWithQualifierFunctionForEveryNamespace,
                preserveTrivia: PreserveTrivia,
                allowBackslashesInPathInterpolation: AllowBackslashesInPathInterpolation,
                useSpecPublicFacadeAndAstWhenAvailable: UseSpecPublicFacadeAndAstWhenAvailable,
                escapeIdentifiers: EscapeIdentifiers,
                failOnMissingSemicolons: FailOnMissingSemicolons,
                collectImportFile: CollectImportFile,
                convertPathLikeLiteralsAtParseTime: ConvertPathLikeLiteralsAtParseTime);
        }

        /// <nodoc />
        public ParsingOptions WithFailOnMissingSemicolons(bool failOnMissingSemicolons)
        {
            return new ParsingOptions(
                namespacesAreAutomaticallyExported: NamespacesAreAutomaticallyExported,
                generateWithQualifierFunctionForEveryNamespace: GenerateWithQualifierFunctionForEveryNamespace,
                preserveTrivia: PreserveTrivia,
                allowBackslashesInPathInterpolation: AllowBackslashesInPathInterpolation,
                useSpecPublicFacadeAndAstWhenAvailable: UseSpecPublicFacadeAndAstWhenAvailable,
                escapeIdentifiers: EscapeIdentifiers,
                failOnMissingSemicolons: failOnMissingSemicolons,
                collectImportFile: CollectImportFile,
                convertPathLikeLiteralsAtParseTime: ConvertPathLikeLiteralsAtParseTime);
        }

        /// <nodoc />
        public ParsingOptions WithEscapeIdentifiers(bool escapeIdentifiers)
        {
            return new ParsingOptions(
                namespacesAreAutomaticallyExported: NamespacesAreAutomaticallyExported,
                generateWithQualifierFunctionForEveryNamespace: GenerateWithQualifierFunctionForEveryNamespace,
                preserveTrivia: PreserveTrivia,
                allowBackslashesInPathInterpolation: AllowBackslashesInPathInterpolation,
                useSpecPublicFacadeAndAstWhenAvailable: UseSpecPublicFacadeAndAstWhenAvailable,
                escapeIdentifiers: escapeIdentifiers,
                failOnMissingSemicolons: FailOnMissingSemicolons,
                collectImportFile: CollectImportFile,
                convertPathLikeLiteralsAtParseTime: ConvertPathLikeLiteralsAtParseTime);
        }

        /// <nodoc />
        public ParsingOptions WithAllowBackslashesInPathInterpolation(bool allowBackslashesInPathInterpolation)
        {
            return new ParsingOptions(
                namespacesAreAutomaticallyExported: NamespacesAreAutomaticallyExported,
                generateWithQualifierFunctionForEveryNamespace: GenerateWithQualifierFunctionForEveryNamespace,
                preserveTrivia: PreserveTrivia,
                allowBackslashesInPathInterpolation: allowBackslashesInPathInterpolation,
                useSpecPublicFacadeAndAstWhenAvailable: UseSpecPublicFacadeAndAstWhenAvailable,
                escapeIdentifiers: EscapeIdentifiers,
                failOnMissingSemicolons: FailOnMissingSemicolons,
                collectImportFile: CollectImportFile,
                convertPathLikeLiteralsAtParseTime: ConvertPathLikeLiteralsAtParseTime);
        }

        /// <nodoc />
        public ParsingOptions WithCollectImportFile(bool collectImportFile)
        {
            return new ParsingOptions(
                namespacesAreAutomaticallyExported: NamespacesAreAutomaticallyExported,
                generateWithQualifierFunctionForEveryNamespace: GenerateWithQualifierFunctionForEveryNamespace,
                preserveTrivia: PreserveTrivia,
                allowBackslashesInPathInterpolation: AllowBackslashesInPathInterpolation,
                useSpecPublicFacadeAndAstWhenAvailable: UseSpecPublicFacadeAndAstWhenAvailable,
                escapeIdentifiers: EscapeIdentifiers,
                failOnMissingSemicolons: FailOnMissingSemicolons,
                collectImportFile: collectImportFile,
                convertPathLikeLiteralsAtParseTime: ConvertPathLikeLiteralsAtParseTime);
        }

        /// <nodoc />
        public ParsingOptions WithTrivia(bool preserveTrivia)
        {
            return new ParsingOptions(
                namespacesAreAutomaticallyExported: NamespacesAreAutomaticallyExported,
                generateWithQualifierFunctionForEveryNamespace: GenerateWithQualifierFunctionForEveryNamespace,
                preserveTrivia: preserveTrivia,
                allowBackslashesInPathInterpolation: AllowBackslashesInPathInterpolation,
                useSpecPublicFacadeAndAstWhenAvailable: UseSpecPublicFacadeAndAstWhenAvailable,
                escapeIdentifiers: EscapeIdentifiers,
                failOnMissingSemicolons: FailOnMissingSemicolons,
                collectImportFile: CollectImportFile,
                convertPathLikeLiteralsAtParseTime: ConvertPathLikeLiteralsAtParseTime);
        }

        /// <summary>
        /// Returns parsing options required for parsing prelude.
        /// </summary>
        /// <remarks>
        /// Observe that the prelude module is always assumed to be a V1 module, qualifier wise.
        /// </remarks>
        public static ParsingOptions GetPreludeParsingOptions(bool escapeIdentifiers)
        {
            return new ParsingOptions(
                namespacesAreAutomaticallyExported: false,
                generateWithQualifierFunctionForEveryNamespace: false,
                preserveTrivia: false,
                allowBackslashesInPathInterpolation: true,
                useSpecPublicFacadeAndAstWhenAvailable: false,
                escapeIdentifiers: escapeIdentifiers,
                failOnMissingSemicolons: true,
                collectImportFile: false,
                convertPathLikeLiteralsAtParseTime: false);
        }

        /// <summary>
        /// Returns default parsing options.
        /// </summary>
        public static ParsingOptions DefaultParsingOptions { get; } =
            new ParsingOptions(
                namespacesAreAutomaticallyExported: true,
                generateWithQualifierFunctionForEveryNamespace: false,
                preserveTrivia: false,
                allowBackslashesInPathInterpolation: true,
                useSpecPublicFacadeAndAstWhenAvailable: false,
                escapeIdentifiers: true,
                failOnMissingSemicolons: false,
                collectImportFile: false,
                convertPathLikeLiteralsAtParseTime: false);

        /// <summary>
        /// Returns parsing options that matches the original TypeScript behavior.
        /// </summary>
        public static ParsingOptions TypeScriptParsingOptions { get; } =
            new ParsingOptions(
                namespacesAreAutomaticallyExported: false,
                generateWithQualifierFunctionForEveryNamespace: false,
                preserveTrivia: false,
                allowBackslashesInPathInterpolation: false,
                useSpecPublicFacadeAndAstWhenAvailable: false,
                escapeIdentifiers: true,
                failOnMissingSemicolons: false,
                collectImportFile: false,
                convertPathLikeLiteralsAtParseTime: false);

        /// <summary>
        /// Returns default parsing options + escape identifiers configuration.
        /// </summary>
        public static ParsingOptions GetDefaultParsingOptionsWithEscapeIdentifiers(bool escapeIdentifiers)
        {
            return DefaultParsingOptions.WithEscapeIdentifiers(escapeIdentifiers);
        }

        /// <summary>
        /// Returns default parsing options + backslash configuration.
        /// </summary>
        public static ParsingOptions GetDefaultParsingOptionsWithBackslashSettings(bool allowBackslashesInPathInterpolation)
        {
            return DefaultParsingOptions.WithAllowBackslashesInPathInterpolation(allowBackslashesInPathInterpolation);
        }
    }
}
