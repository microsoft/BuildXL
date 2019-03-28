// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.Pips;
using BuildXL.Pips.Operations;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using BuildXL.FrontEnd.Script.Ambients.Transformers;
using BuildXL.FrontEnd.Script.Types;
using BuildXL.FrontEnd.Script.Values;
using BuildXL.FrontEnd.Script.Evaluator;
using static BuildXL.Utilities.FormattableStringEx;

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

        private static EvaluationResult SealDirectory(Context context, ModuleLiteral env, EvaluationStackFrame args)
        {
            return SealDirectoryHelper(context, env, args, SealDirectoryKind.Full);
        }

        private static EvaluationResult SealPartialDirectory(Context context, ModuleLiteral env, EvaluationStackFrame args)
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
        

        private static EvaluationResult SealSourceDirectory(Context context, ModuleLiteral env, EvaluationStackFrame args)
        {
            AbsolutePath path = Args.AsPath(args, 0, false);
            var optionAsEnumValue = Args.AsNumberOrEnumValueOptional(args, 1);
            var option = optionAsEnumValue.HasValue ? (SealSourceDirectoryOption)optionAsEnumValue.Value : SealSourceDirectoryOption.TopDirectoryOnly;
            var tags = Args.AsStringArrayOptional(args, 2);
            var description = Args.AsStringOptional(args, 3);
            var patterns = Args.AsStringArrayOptional(args, 4);

            var sealDirectoryKind = option == SealSourceDirectoryOption.AllDirectories ? SealDirectoryKind.SourceAllDirectories : SealDirectoryKind.SourceTopDirectoryOnly;

            DirectoryArtifact sealedDirectoryArtifact;
            if (!context.GetPipConstructionHelper().TrySealDirectory(path, s_emptySealContents, sealDirectoryKind, tags, description, patterns, out sealedDirectoryArtifact))
            {
                // Error has been logged
                return EvaluationResult.Error;
            }

            var result = new StaticDirectory(sealedDirectoryArtifact, sealDirectoryKind, s_emptySealContents.WithCompatibleComparer(OrdinalPathOnlyFileArtifactComparer.Instance));
            return new EvaluationResult(result);
        }

        private static EvaluationResult SealDirectoryHelper(Context context, ModuleLiteral env, EvaluationStackFrame args, SealDirectoryKind sealDirectoryKind)
        {
            AbsolutePath path = Args.AsPath(args, 0, false);
            ArrayLiteral contents = Args.AsArrayLiteral(args, 1);
            var tags = Args.AsStringArrayOptional(args, 2);
            var description = Args.AsStringOptional(args, 3);
            // Only do scrub for fully seal directory
            var scrub = sealDirectoryKind.IsFull() ? Args.AsBoolOptional(args, 4) : false;

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
