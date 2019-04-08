// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using BuildXL.Pips;
using BuildXL.Pips.Operations;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using BuildXL.FrontEnd.Script.Types;
using BuildXL.FrontEnd.Script.Values;
using BuildXL.FrontEnd.Script.Evaluator;

namespace BuildXL.FrontEnd.Script.Ambients.Transformers
{
    /// <summary>
    /// Ambient definition for namespace Transformer.
    /// </summary>
    public partial class AmbientTransformerBase : AmbientDefinitionBase
    {
        private static readonly SortedReadOnlyArray<FileArtifact, OrdinalFileArtifactComparer> s_emptySealContents 
            = CollectionUtilities.EmptySortedReadOnlyArray<FileArtifact, OrdinalFileArtifactComparer>(OrdinalFileArtifactComparer.Instance);

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

        private void InitializeSealDirectoryNames()
        {
            m_sealRoot = Symbol("root");
            m_sealFiles = Symbol("files");
            m_sealTags = Symbol("tags");
            m_sealDescription = Symbol("description");
            m_sealInclude = Symbol("include");
            m_sealPatterns = Symbol("patterns");
            m_sealScrub = Symbol("scrub");
        }

        private CallSignature SealPartialDirectorySignature => SealDirectorySignature;

        private CallSignature SealDirectorySignature => CreateSignature(
            required: RequiredParameters(AmbientTypes.PathType, new ArrayType(AmbientTypes.FileType)),
            optional: OptionalParameters(new ArrayType(PrimitiveType.StringType), PrimitiveType.StringType, AmbientTypes.BooleanType),
            returnType: AmbientTypes.StaticDirectoryType);

        private CallSignature SealSourceDirectorySignature => CreateSignature(
            required: RequiredParameters(AmbientTypes.PathType),
            optional: OptionalParameters(PrimitiveType.NumberType, new ArrayType(PrimitiveType.StringType), PrimitiveType.StringType, new ArrayType(PrimitiveType.StringType)),
            returnType: AmbientTypes.StaticDirectoryType);

        private CallSignature ComposeSharedOpaqueDirectoriesSignature => CreateSignature(
            required: RequiredParameters(AmbientTypes.PathType, new ArrayType(AmbientTypes.ObjectType)),
            optional: OptionalParameters(new ArrayType(PrimitiveType.StringType), PrimitiveType.StringType),
            returnType: AmbientTypes.StaticDirectoryType);

        private EvaluationResult SealDirectory(Context context, ModuleLiteral env, EvaluationStackFrame args)
        {
            return SealDirectoryHelper(context, env, args, SealDirectoryKind.Full);
        }

        private EvaluationResult SealPartialDirectory(Context context, ModuleLiteral env, EvaluationStackFrame args)
        {
            return SealDirectoryHelper(context, env, args, SealDirectoryKind.Partial);
        }

        private static EvaluationResult ComposeSharedOpaqueDirectories(Context context, ModuleLiteral env, EvaluationStackFrame args)
        {
            AbsolutePath root = Args.AsPath(args, 0, false);
            ArrayLiteral contents = Args.AsArrayLiteral(args, 1);
            var tags = Args.AsStringArrayOptional(args, 2);
            var description = Args.AsStringOptional(args, 3);

            var directories = new DirectoryArtifact[contents.Length];

            for (int i = 0; i < contents.Length; ++i)
            {
                directories[i] = Converter.ExpectSharedOpaqueDirectory(contents[i], context: new ConversionContext(pos: i, objectCtx: contents)).Root;
            }

            if (!context.GetPipConstructionHelper().TryComposeSharedOpaqueDirectory(root, directories, description, tags, out var compositeSharedOpaque))
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
            if (!context.GetPipConstructionHelper().TrySealDirectory(path, s_emptySealContents, sealDirectoryKind, tags, description, patterns, out sealedDirectoryArtifact))
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
            }
            else
            {
                path = Args.AsPath(args, 0, false);
                contents = Args.AsArrayLiteral(args, 1);
                tags = Args.AsStringArrayOptional(args, 2);
                description = Args.AsStringOptional(args, 3);
                // Only do scrub for fully seal directory
                scrub = sealDirectoryKind.IsFull() ? Args.AsBoolOptional(args, 4) : false;
            }

            var fileContents = new FileArtifact[contents.Length];

            for (int i = 0; i < contents.Length; ++i)
            {
                fileContents[i] = Converter.ExpectFile(contents[i], strict: false, context: new ConversionContext(pos: i, objectCtx: contents));
            }

            var sortedFileContents = SortedReadOnlyArray<FileArtifact, OrdinalFileArtifactComparer>.CloneAndSort(fileContents, OrdinalFileArtifactComparer.Instance);

            DirectoryArtifact sealedDirectoryArtifact;
            if (!context.GetPipConstructionHelper().TrySealDirectory(path, sortedFileContents, sealDirectoryKind, tags, description, null, out sealedDirectoryArtifact, scrub))
            {
                // Error has been logged
                return EvaluationResult.Error;
            }

            var result = new StaticDirectory(sealedDirectoryArtifact, sealDirectoryKind, sortedFileContents.WithCompatibleComparer(OrdinalPathOnlyFileArtifactComparer.Instance));

            return new EvaluationResult(result);
        }
    }
}
