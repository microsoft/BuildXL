// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.CodeAnalysis;
using BuildXL.FrontEnd.Script.Evaluator;
using BuildXL.FrontEnd.Script.Types;
using BuildXL.FrontEnd.Script.Values;
using BuildXL.Utilities.Core;
using static BuildXL.Utilities.Core.FormattableStringEx;

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

            // This is a total HACK.
            // Unfortunately we are forced to use synchronous IO here and blast a hack through to the engine to ensure this file is
            // tracked for graph caching. The reason is that we use Task.Run to spawn frontend threads and for larger builds we can have
            // saturated the threadpool. So when during evaluation we perform IO there is no thread available to continue the work once the
            // OS returns the data. We have a mode where do use an ActionBlock ActionBlock to schedule the work. This can be turned on via `/enableEvaluationThrottling+`
            // but this was never made the default because measuring at the time resulted in about a 2x slowdown for the office build.
            // Since AB testing will take a long time since we need to get averages of metabuild duration now that most evaluation happens there.
            // So to unblock for now we will perform synchronous IO :(

            // There is also another small problem with this change. It is not using the IFileSystem to access the file because that doesn't have any way to get
            // file handles, so the memory filesystem of unittests won't function.

            var possibleContent = context.FrontEndHost.Engine.GetFileContentSynchronous(file.Path);
            if (possibleContent.Succeeded)
            {
                return EvaluationResult.Create(possibleContent.Result);
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
