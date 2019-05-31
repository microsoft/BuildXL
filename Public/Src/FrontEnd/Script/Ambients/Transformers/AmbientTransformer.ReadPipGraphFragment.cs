// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.IO;
using System.Linq;
using System.Threading;
using BuildXL.FrontEnd.Script.Evaluator;
using BuildXL.FrontEnd.Script.Types;
using BuildXL.FrontEnd.Script.Values;
using static BuildXL.Utilities.FormattableStringEx;

namespace BuildXL.FrontEnd.Script.Ambients.Transformers
{
    /// <summary>
    /// Ambient definition for namespace Transformer.
    /// </summary>
    public partial class AmbientTransformerBase : AmbientDefinitionBase
    {
        internal const string ReadGraphFragmentFunctionName = "readPipGraphFragment";

        private CallSignature ReadGraphFragmentSignature => CreateSignature(
            required: RequiredParameters(AmbientTypes.StringType, AmbientTypes.PathType, AmbientTypes.ArrayType),
            returnType: AmbientTypes.StringType);

        private static int uniqueFragmentId = 0;

        private static EvaluationResult ReadGraphFragment(Context context, ModuleLiteral env, EvaluationStackFrame args)
        {
            var file = Args.AsFile(args, 0);
            var deps = Args.AsArrayLiteral(args, 1).Values.Select(v => (int)v.Value).ToArray();
            var description = Args.AsString(args, 2);

            if (!file.IsSourceFile)
            {
                // Do not read output file.
                throw new FileOperationException(
                    new Exception(
                        I($"Failed adding pip graph fragment file '{file.Path.ToString(context.PathTable)}' because the file is not a source file")));
            }

            int id = Interlocked.Increment(ref uniqueFragmentId);
            context.FrontEndHost.Engine.RecordFrontEndFile(file, null);
            var readFragmentTask = context.FrontEndHost.PipGraphFragmentManager.AddFragmentFileToGraph(id, file, deps, description);
            return EvaluationResult.Create(id);
        }
    }
}
