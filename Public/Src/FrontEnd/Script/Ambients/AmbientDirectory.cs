// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using BuildXL.FrontEnd.Script.Evaluator;
using BuildXL.FrontEnd.Script.Types;
using BuildXL.FrontEnd.Script.Values;
using BuildXL.Utilities;

namespace BuildXL.FrontEnd.Script.Ambients
{
    /// <summary>
    /// Ambient definition for <code>namespace Directory {}</code> and <code>interface Directory {}</code>.
    /// </summary>
    public sealed class AmbientDirectory : AmbientBasePathQueries<DirectoryArtifact>
    {
        internal const string Name = "Directory";

        /// <nodoc />
        public AmbientDirectory(PrimitiveTypes knownTypes)
            : base(Name, knownTypes)
        {
        }

        /// <inheritdoc />
        protected override AmbientNamespaceDefinition? GetNamespaceDefinition()
        {
            return new AmbientNamespaceDefinition(
                Name,
                new[]
                {
                    Function("fromPath", FromPath, FromPathSignature),
                    Function("exists", Exists, ExistsSignature),
                });
        }

        /// <inheritdoc />
        protected override Dictionary<StringId, CallableMember<DirectoryArtifact>> CreateSpecificMembers()
        {
            return new Dictionary<StringId, CallableMember<DirectoryArtifact>>
            {
                { NameId("combine"), Create<DirectoryArtifact>(AmbientName, Symbol("combine"), Combine) },
                { NameId("combinePaths"), Create<DirectoryArtifact>(AmbientName, Symbol("combinePaths"), CombinePaths, rest: true) },
            };
        }

        private static EvaluationResult Combine(Context context, DirectoryArtifact receiver, EvaluationResult arg, EvaluationStackFrame captures)
        {
            return Combine(context, receiver.Path, arg, captures);
        }

        private static EvaluationResult CombinePaths(Context context, DirectoryArtifact receiver, EvaluationResult arg, EvaluationStackFrame captures)
        {
            return CombinePaths(context, receiver.Path, arg, captures);
        }

        /// <summary>
        /// Function for converting AbsolutePath to DirectoryArtifact conforming to the 'InvokeAmbient' signature.
        /// </summary>
        private static EvaluationResult FromPath(Context context, ModuleLiteral env, EvaluationStackFrame args)
        {
            Args.CheckArgumentIndex(args, 0);

            return args[0].IsUndefined
                ? EvaluationResult.Undefined
                : EvaluationResult.Create(DirectoryArtifact.CreateWithZeroPartialSealId(Converter.ExpectPath(args[0], strict: false, context: new ConversionContext(pos: 1))));
        }

        private static EvaluationResult Exists(Context context, ModuleLiteral env, EvaluationStackFrame args)
        {
            var directory = Args.AsDirectory(args, 0);
            // TODO: fail if the input directory is not on a read-only mount.
            bool exists = context.FrontEndHost.Engine.DirectoryExists(directory.Path);
            return EvaluationResult.Create(exists);
        }

        private CallSignature FromPathSignature => CreateSignature(
            required: RequiredParameters(AmbientTypes.PathType),
            returnType: AmbientTypes.DirectoryType);

        private CallSignature ExistsSignature => CreateSignature(
            required: RequiredParameters(AmbientTypes.DirectoryType),
            returnType: AmbientTypes.BooleanType);
    }
}
