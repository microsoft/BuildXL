// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.FrontEnd.Script.Ambients.Exceptions;
using BuildXL.FrontEnd.Script.Evaluator;
using BuildXL.FrontEnd.Script.Types;
using BuildXL.FrontEnd.Script.Values;
using BuildXL.Utilities;

namespace BuildXL.FrontEnd.Script.Ambients
{
    /// <summary>
    /// Class for unsafe ambients.
    /// </summary>
    public sealed class AmbientUnsafe : AmbientDefinitionBase
    {
        private const string UnsafeName = "Unsafe";
        private const string OutputFile = "outputFile";
        private const string ExOutputDirectory = "exOutputDirectory";

        /// <nodoc />
        public AmbientUnsafe(PrimitiveTypes knownTypes)
            : base(UnsafeName, knownTypes)
        {
        }

        /// <inheritdoc />
        protected override AmbientNamespaceDefinition? GetNamespaceDefinition()
        {
            return new AmbientNamespaceDefinition(
                UnsafeName,
                new[]
                {
                    Function(OutputFile, UnsafeOutputFile, UnsafeOutputFileSignature),
                    Function(ExOutputDirectory, UnsafeExOutputDirectory, UnsafeExOutputDirectorySignature),
                });
        }

        private EvaluationResult UnsafeOutputFile(Context context, ModuleLiteral env, EvaluationStackFrame args)
        {
            ThrowIfNotAllowed(context, OutputFile);

            var path = Args.AsPath(args, 0);
            var rewriteCount = Args.AsNumberOrEnumValueOptional(args, 1) ?? 1;

            return EvaluationResult.Create(new FileArtifact(path, rewriteCount));
        }

        private EvaluationResult UnsafeExOutputDirectory(Context context, ModuleLiteral env, EvaluationStackFrame args)
        {
            ThrowIfNotAllowed(context, ExOutputDirectory);

            var path = Args.AsPath(args, 0);
            return EvaluationResult.Create(DirectoryArtifact.CreateWithZeroPartialSealId(path));
        }

        private CallSignature UnsafeOutputFileSignature => CreateSignature(
            required: RequiredParameters(AmbientTypes.PathType),
            optional: OptionalParameters(PrimitiveType.NumberType),
            returnType: AmbientTypes.FileType);

        private CallSignature UnsafeExOutputDirectorySignature => CreateSignature(
            required: RequiredParameters(AmbientTypes.PathType),
            returnType: AmbientTypes.DirectoryType);

        private void ThrowIfNotAllowed(Context context, string methodName)
        {
            if (!context.FrontEndHost.Configuration.FrontEnd.AllowUnsafeAmbient)
            {
                throw new DisallowedUnsafeAmbientCallException(methodName);
            }
        }
    }
}
