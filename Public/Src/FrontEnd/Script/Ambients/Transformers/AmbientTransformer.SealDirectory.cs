// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using BuildXL.FrontEnd.Script.Evaluator;
using BuildXL.FrontEnd.Script.Types;
using BuildXL.FrontEnd.Script.Values;
using BuildXL.Pips;
using BuildXL.Pips.Operations;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;

namespace BuildXL.FrontEnd.Script.Ambients.Transformers
{
    /// <summary>
    /// Ambient definition for namespace Transformer.
    /// </summary>
    public partial class AmbientTransformerBase : AmbientDefinitionBase
    {
        private static readonly SortedReadOnlyArray<FileArtifact, OrdinalFileArtifactComparer> s_emptySealContents 
            = CollectionUtilities.EmptySortedReadOnlyArray<FileArtifact, OrdinalFileArtifactComparer>(OrdinalFileArtifactComparer.Instance);
        private static readonly SortedReadOnlyArray<DirectoryArtifact, OrdinalDirectoryArtifactComparer> s_emptyOutputDirectoryContents
            = CollectionUtilities.EmptySortedReadOnlyArray<DirectoryArtifact, OrdinalDirectoryArtifactComparer>(OrdinalDirectoryArtifactComparer.Instance);

        internal const string SealDirectoryFunctionName = "sealDirectory";
        internal const string SealSourceDirectoryFunctionName = "sealSourceDirectory";
        internal const string SealPartialDirectoryFunctionName = "sealPartialDirectory";
        internal const string ComposeSharedOpaqueDirectoriesFunctionName = "composeSharedOpaqueDirectories";

        private static readonly Dictionary<string, SealDirectoryKind> s_includeMap = new Dictionary<string, SealDirectoryKind>(StringComparer.Ordinal)
        {
            ["topDirectoryOnly"] = SealDirectoryKind.SourceTopDirectoryOnly,
            ["allDirectories"] = SealDirectoryKind.SourceAllDirectories,
        };

        private SymbolAtom m_sealRoot;
        private SymbolAtom m_sealFiles;
        private SymbolAtom m_sealTags;
        private SymbolAtom m_sealDescription;
        private SymbolAtom m_sealInclude;
        private SymbolAtom m_sealPatterns;
        private SymbolAtom m_sealScrub;
        private SymbolAtom m_sealOutputDirectories;
        private SymbolAtom m_sealDirectories;
        private SymbolAtom m_sealDirectoryContentFilter;

        private void InitializeSealDirectoryNames()
        {
            m_sealRoot = Symbol("root");
            m_sealFiles = Symbol("files");
            m_sealTags = Symbol("tags");
            m_sealDescription = Symbol("description");
            m_sealInclude = Symbol("include");
            m_sealPatterns = Symbol("patterns");
            m_sealScrub = Symbol("scrub");
            m_sealOutputDirectories = Symbol("outputDirectories");
            m_sealDirectories = Symbol("directories");
            m_sealDirectoryContentFilter = Symbol("directoryFilteringRegexExpression");
        }

        private CallSignature SealPartialDirectorySignature => SealDirectorySignature;

        private CallSignature SealDirectorySignature => CreateSignature(
            required: RequiredParameters(AmbientTypes.PathType, new ArrayType(AmbientTypes.FileType)),
            optional: OptionalParameters(new ArrayType(PrimitiveType.StringType), PrimitiveType.StringType, AmbientTypes.BooleanType, new ArrayType(AmbientTypes.StaticDirectoryType)),
            returnType: AmbientTypes.StaticDirectoryType);

        private CallSignature SealSourceDirectorySignature => CreateSignature(
            required: RequiredParameters(AmbientTypes.PathType),
            optional: OptionalParameters(PrimitiveType.NumberType, new ArrayType(PrimitiveType.StringType), PrimitiveType.StringType, new ArrayType(PrimitiveType.StringType)),
            returnType: AmbientTypes.StaticDirectoryType);

        private CallSignature ComposeSharedOpaqueDirectoriesSignature => CreateSignature(
            required: RequiredParameters(AmbientTypes.PathType, new ArrayType(AmbientTypes.ObjectType)),
            optional: OptionalParameters(PrimitiveType.StringType, new ArrayType(PrimitiveType.StringType), PrimitiveType.StringType),
            returnType: AmbientTypes.StaticDirectoryType);

        private EvaluationResult SealDirectory(Context context, ModuleLiteral env, EvaluationStackFrame args)
        {
            return SealDirectoryHelper(context, env, args, SealDirectoryKind.Full);
        }

        private EvaluationResult SealPartialDirectory(Context context, ModuleLiteral env, EvaluationStackFrame args)
        {
            return SealDirectoryHelper(context, env, args, SealDirectoryKind.Partial);
        }

        private EvaluationResult ComposeSharedOpaqueDirectories(Context context, ModuleLiteral env, EvaluationStackFrame args)
        {
            AbsolutePath root;
            ArrayLiteral contents;
            Regex directoryFilteringRegex;            

            if (args.Length > 0 && args[0].Value is ObjectLiteral)
            {
                var obj = Args.AsObjectLiteral(args, 0);
                var directory = Converter.ExtractDirectory(obj, m_sealRoot, allowUndefined: false);
                root = directory.Path;
                contents = Converter.ExtractArrayLiteral(obj, m_sealDirectories, allowUndefined: false);
                directoryFilteringRegex = Converter.ExtractRegex(obj, m_sealDirectoryContentFilter, allowUndefined: true);
            }
            else
            {
                root = Args.AsPath(args, 0, false);
                contents = Args.AsArrayLiteral(args, 1);
                directoryFilteringRegex = Args.AsRegexOptional(args, 2);                
            }

            var directories = new DirectoryArtifact[contents.Length];

            for (int i = 0; i < contents.Length; ++i)
            {
                directories[i] = Converter.ExpectSharedOpaqueDirectory(contents[i], context: new ConversionContext(pos: i, objectCtx: contents)).Root;
            }

            if (!context.GetPipConstructionHelper().TryComposeSharedOpaqueDirectory(root, directories, directoryFilteringRegex?.ToString(), description: null, tags: null, out var compositeSharedOpaque))
            {
                // Error should have been logged
                return EvaluationResult.Error;
            }

            var result = new StaticDirectory(compositeSharedOpaque, SealDirectoryKind.SharedOpaque, s_emptySealContents.WithCompatibleComparer(OrdinalPathOnlyFileArtifactComparer.Instance));

            return new EvaluationResult(result);
        }

        private EvaluationResult SealSourceDirectory(Context context, ModuleLiteral env, EvaluationStackFrame args)
        {
            AbsolutePath path;
            SealDirectoryKind sealDirectoryKind;
            string[] tags;
            string description;
            string[] patterns;

            if (args.Length > 0 && args[0].Value is ObjectLiteral)
            {
                var obj = Args.AsObjectLiteral(args, 0);
                var directory = Converter.ExtractDirectory(obj, m_sealRoot, allowUndefined: false);
                path = directory.Path;

                var include = Converter.ExtractStringLiteral(obj, m_sealInclude, s_includeMap.Keys, allowUndefined: true);
                sealDirectoryKind = include != null
                    ? s_includeMap[include]
                    : SealDirectoryKind.SourceTopDirectoryOnly;
                tags = Converter.ExtractStringArray(obj, m_sealTags, allowUndefined: true);
                description = Converter.ExtractString(obj, m_sealDescription, allowUndefined: true);
                patterns = Converter.ExtractStringArray(obj, m_sealPatterns, allowUndefined: true);
            }
            else
            {
                path = Args.AsPath(args, 0, false);
                var optionAsEnumValue = Args.AsNumberOrEnumValueOptional(args, 1);
                var option = optionAsEnumValue.HasValue ? (SealSourceDirectoryOption)optionAsEnumValue.Value : SealSourceDirectoryOption.TopDirectoryOnly;
                sealDirectoryKind = option == SealSourceDirectoryOption.AllDirectories ? SealDirectoryKind.SourceAllDirectories : SealDirectoryKind.SourceTopDirectoryOnly;

                tags = Args.AsStringArrayOptional(args, 2);
                description = Args.AsStringOptional(args, 3);
                patterns = Args.AsStringArrayOptional(args, 4);
            }

            DirectoryArtifact sealedDirectoryArtifact;
            if (!context.GetPipConstructionHelper().TrySealDirectory(
                directoryRoot: path,
                contents: s_emptySealContents,
                outputDirectorycontents: s_emptyOutputDirectoryContents,
                kind: sealDirectoryKind,
                tags: tags,
                description: description,
                patterns: patterns,
                sealedDirectory: out sealedDirectoryArtifact))
            {
                // Error has been logged
                return EvaluationResult.Error;
            }

            var result = new StaticDirectory(sealedDirectoryArtifact, sealDirectoryKind, s_emptySealContents.WithCompatibleComparer(OrdinalPathOnlyFileArtifactComparer.Instance));
            return new EvaluationResult(result);
        }

        private EvaluationResult SealDirectoryHelper(Context context, ModuleLiteral env, EvaluationStackFrame args, SealDirectoryKind sealDirectoryKind)
        {
            AbsolutePath path;
            ArrayLiteral contents;
            ArrayLiteral outputDirectoryContents;
            string[] tags;
            string description;
            bool scrub;

            if (args.Length > 0 && args[0].Value is ObjectLiteral)
            {
                var obj = Args.AsObjectLiteral(args, 0);
                var directory = Converter.ExtractDirectory(obj, m_sealRoot, allowUndefined: false);
                path = directory.Path;
                contents = Converter.ExtractArrayLiteral(obj, m_sealFiles, allowUndefined: false);
                tags = Converter.ExtractStringArray(obj, m_sealTags, allowUndefined: true);
                description = Converter.ExtractString(obj, m_sealDescription, allowUndefined: true);
                scrub = sealDirectoryKind.IsFull() 
                    ? Converter.ExtractOptionalBoolean(obj, m_sealScrub) ?? false
                    : false;
                outputDirectoryContents = sealDirectoryKind.IsFull()
                    ? Converter.ExtractOptionalArrayLiteral(obj, m_sealOutputDirectories, allowUndefined: false)
                    : null;
            }
            else
            {
                path = Args.AsPath(args, 0, false);
                contents = Args.AsArrayLiteral(args, 1);
                tags = Args.AsStringArrayOptional(args, 2);
                description = Args.AsStringOptional(args, 3);
                // Only do scrub for fully seal directory
                scrub = sealDirectoryKind.IsFull() ? Args.AsBoolOptional(args, 4) : false;
                outputDirectoryContents = sealDirectoryKind.IsFull() ? Args.AsArrayLiteralOptional(args, 5) : null;
            }

            var fileContents = new FileArtifact[contents.Length];

            for (int i = 0; i < contents.Length; ++i)
            {
                fileContents[i] = Converter.ExpectFile(contents[i], strict: false, context: new ConversionContext(pos: i, objectCtx: contents));
            }

            var outputDirectoryArtifactContents = new DirectoryArtifact[outputDirectoryContents?.Count ?? 0];
            for (int i = 0; i < outputDirectoryArtifactContents.Length; ++i)
            {
                outputDirectoryArtifactContents[i] = Converter.ExpectStaticDirectory(outputDirectoryContents[i], context: new ConversionContext(pos: i, objectCtx: contents)).Root;
            }

            var sortedFileContents = SortedReadOnlyArray<FileArtifact, OrdinalFileArtifactComparer>.CloneAndSort(fileContents, OrdinalFileArtifactComparer.Instance);

            var sortedDirectoryContents = SortedReadOnlyArray<DirectoryArtifact, OrdinalDirectoryArtifactComparer>.CloneAndSort(
                outputDirectoryArtifactContents, 
                OrdinalDirectoryArtifactComparer.Instance);

            DirectoryArtifact sealedDirectoryArtifact;
            if (!context.GetPipConstructionHelper().TrySealDirectory(
                directoryRoot: path, contents: sortedFileContents, outputDirectorycontents: sortedDirectoryContents,
                kind: sealDirectoryKind, tags: tags, description: description, patterns: null,
                sealedDirectory: out sealedDirectoryArtifact, scrub: scrub))
            {
                // Error has been logged
                return EvaluationResult.Error;
            }

            var result = new StaticDirectory(sealedDirectoryArtifact, sealDirectoryKind, sortedFileContents.WithCompatibleComparer(OrdinalPathOnlyFileArtifactComparer.Instance));

            return new EvaluationResult(result);
        }
    }
}
