// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.FrontEnd.Script.Evaluator;
using BuildXL.FrontEnd.Script.Types;
using BuildXL.FrontEnd.Script.Values;
using BuildXL.Pips.Operations;
using BuildXL.Utilities;

namespace BuildXL.FrontEnd.Script.Ambients.Transformers
{
    /// <summary>
    /// Ambient definition for namespace Transformer.
    /// </summary>
    public partial class AmbientTransformerBase : AmbientDefinitionBase
    {
        internal const string CopyFileFunctionName = "copyFile";

        private CallSignature CopyFileSignature => CreateSignature(
            required: RequiredParameters(AmbientTypes.FileType, AmbientTypes.PathType),
            optional: OptionalParameters(new ArrayType(PrimitiveType.StringType), PrimitiveType.StringType, PrimitiveType.BooleanType),
            returnType: AmbientTypes.FileType);


        private static EvaluationResult CopyFile(Context context, ModuleLiteral env, EvaluationStackFrame args)
        {
            var source = Args.AsFile(args, 0);
            var destination = Args.AsPath(args, 1);
            var tags = Args.AsStringArrayOptional(args, 2);
            var description = Args.AsStringOptional(args, 3);
            var writable = Args.AsBoolOptional(args, 4);

            CopyFile.Options options = default;
            if (writable)
            {
                options = BuildXL.Pips.Operations.CopyFile.Options.OutputsMustRemainWritable;
            }

            FileArtifact result;
            if (!context.GetPipConstructionHelper().TryCopyFile(source, destination, options, tags, description, out result))
            {
                // Error has been logged
                return EvaluationResult.Error;
            }

            return new EvaluationResult(result);
        }
    }
}
