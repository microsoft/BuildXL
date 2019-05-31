// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
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

        private static EvaluationResult ReadGraphFragment(Context context, ModuleLiteral env, EvaluationStackFrame args)
        {
            var name = Args.AsString(args, 0);

            // File that is read here will be tracked by the input tracker in the same way the spec file.
            var file = Args.AsFile(args, 1);

            var deps = Args.AsStringArray(args, 2);

            if (!file.IsSourceFile)
            {
                // Do not read output file.
                throw new FileOperationException(
                    new Exception(
                        I($"Failed adding pip graph fragment file '{file.Path.ToString(context.PathTable)}' because the file is not a source file")));
            }

            if (context.FrontEndHost.PipGraphFragmentManager != null)
            {
                var readFragmentTask = context.FrontEndHost.PipGraphFragmentManager.AddFragmentFileToGraph(name, file, deps);
            }

            var possibleContent = context.FrontEndHost.Engine.GetFileContentAsync(file.Path).GetAwaiter().GetResult();
            return EvaluationResult.Create(name);
        }
    }
}
