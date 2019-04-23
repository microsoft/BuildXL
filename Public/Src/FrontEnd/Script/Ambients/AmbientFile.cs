// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.CodeAnalysis;
using BuildXL.FrontEnd.Script.Evaluator;
using BuildXL.FrontEnd.Script.Types;
using BuildXL.FrontEnd.Script.Values;
using BuildXL.Utilities;
using static BuildXL.Utilities.FormattableStringEx;

namespace BuildXL.FrontEnd.Script.Ambients
{
    /// <summary>
    ///     Ambient definition for <code>namespace File {}</code> and <code>interface File {}</code>.
    /// </summary>
    public sealed class AmbientFile : AmbientBasePathQueries<FileArtifact>
    {
        internal const string FileName = "File";
        internal const string FromPathFunctionName = "fromPath";
        internal const string ReadAllTextFunctionName = "readAllText";
        internal const string ExistsFunctionName = "exists";

        /// <nodoc />
        public AmbientFile(PrimitiveTypes knownTypes)
            : base(FileName, knownTypes)
        {
        }

        private CallSignature FromPathSignature => CreateSignature(
            required: RequiredParameters(AmbientTypes.PathType),
            returnType: AmbientTypes.FileType);

        private CallSignature ReadAllTextSignature => CreateSignature(
            required: RequiredParameters(AmbientTypes.PathType),
            optional: OptionalParameters(AmbientTypes.EnumType),
            returnType: AmbientTypes.StringType);

        private CallSignature ExistsSignature => CreateSignature(
            required: RequiredParameters(AmbientTypes.FileType),
            returnType: AmbientTypes.BooleanType);

        /// <inheritdoc />
        protected override AmbientNamespaceDefinition? GetNamespaceDefinition()
        {
            return new AmbientNamespaceDefinition(
                FileName,
                new[]
                {
                    Function(FromPathFunctionName, FromPath, FromPathSignature),
                    Function(ReadAllTextFunctionName, ReadAllText, ReadAllTextSignature),
                    Function(ExistsFunctionName, Exists, ExistsSignature),
                });
        }

        [SuppressMessage("Microsoft.Usage", "CA2201:DoNotCreateReservedExceptionTypes", Justification = "It's just used as part of a wrapper.")]
        private static EvaluationResult ReadAllText(Context context, ModuleLiteral env, EvaluationStackFrame args)
        {
            // File that is read here will be tracked by the input tracker in the same way the spec file.
            var file = Args.AsFile(args, 0);

            if (!file.IsSourceFile)
            {
                // Do not read output file.
                throw new FileOperationException(
                    new Exception(
                        I($"Failed reading '{file.Path.ToString(context.PathTable)}' because the file is not a source file")));
            }

            var possibleContent = context.FrontEndHost.Engine.GetFileContentAsync(file.Path).GetAwaiter().GetResult();
            if (possibleContent.Succeeded)
            {
                return EvaluationResult.Create(possibleContent.Result.GetContentAsString());
            }

            throw new FileOperationException(
                new Exception(I($"Failed reading '{file.Path.ToString(context.PathTable)}'"), possibleContent.Failure.Exception));
        }

        [SuppressMessage("Microsoft.Usage", "CA2201:DoNotCreateReservedExceptionTypes", Justification = "It's just used as part of a wrapper.")]
        private static EvaluationResult Exists(Context context, ModuleLiteral env, EvaluationStackFrame args)
        {
            var file = Args.AsFile(args, 0);

            if (!file.IsSourceFile)
            {
                // Do not read output file.
                throw new FileOperationException(
                    new Exception(
                        I($"Failed checking existence for '{file.Path.ToString(context.PathTable)}' because the file is not a source file")));
            }

            bool fileExists = context.FrontEndContext.FileSystem.Exists(file.Path);

            return EvaluationResult.Create(fileExists);
        }

        /// <summary>
        ///     Function for converting AbsolutePath to FileArtifact conforming to the 'InvokeAmbient' signature.
        /// </summary>
        private static EvaluationResult FromPath(Context context, ModuleLiteral env, EvaluationStackFrame args)
        {
            Args.CheckArgumentIndex(args, 0);

            // TODO:ST: this block should use ExpectsOptionalPath?!?
            return (args[0].IsUndefined)
                ? EvaluationResult.Undefined
                : EvaluationResult.Create(FileArtifact.CreateSourceFile(Converter.ExpectPath(args[0], false, new ConversionContext(pos: 1))));
        }
    }
}
