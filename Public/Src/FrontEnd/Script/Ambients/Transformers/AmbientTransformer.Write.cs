// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Text;
using BuildXL.Pips.Operations;
using BuildXL.Utilities;
using BuildXL.FrontEnd.Script.Ambients.Transformers;
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
        internal const string WriteFileFunctionName = "writeFile";
        internal const string WriteDataFunctionName = "writeData";
        internal const string WriteAllLinesFunctionName = "writeAllLines";
        internal const string WriteAllTextFunctionName = "writeAllText";

        private UnionType FileContentElementType => UnionType(AmbientTypes.PathType, AmbientTypes.RelativePathType, AmbientTypes.PathAtomType, PrimitiveType.StringType);

        private CallSignature WriteFileSignature => CreateSignature(
            required: RequiredParameters(
                AmbientTypes.PathType,
                UnionType(FileContentElementType, new ArrayType(FileContentElementType))),
            optional: OptionalParameters(new ArrayType(PrimitiveType.StringType), PrimitiveType.StringType, PrimitiveType.StringType),
            returnType: AmbientTypes.FileType);

        private CallSignature WriteDataSignature => CreateSignature(
            required: RequiredParameters(AmbientTypes.PathType, AmbientTypes.DataType),
            optional: OptionalParameters(new ArrayType(PrimitiveType.StringType), PrimitiveType.StringType, PrimitiveType.StringType),
            returnType: AmbientTypes.FileType);

        private CallSignature WriteAllLinesSignature => CreateSignature(
            required: RequiredParameters(AmbientTypes.PathType, new ArrayType(AmbientTypes.DataType)),
            optional: OptionalParameters(new ArrayType(PrimitiveType.StringType), PrimitiveType.StringType, PrimitiveType.StringType),
            returnType: AmbientTypes.FileType);

        private CallSignature WriteAllTextSignature => CreateSignature(
            required: RequiredParameters(AmbientTypes.PathType, PrimitiveType.StringType),
            optional: OptionalParameters(new ArrayType(PrimitiveType.StringType), PrimitiveType.StringType, PrimitiveType.StringType),
            returnType: AmbientTypes.FileType);

        private static EvaluationResult WriteFile(Context context, ModuleLiteral env, EvaluationStackFrame args)
        {
            return WriteFileHelper(context, env, args, WriteFileMode.WriteFile);
        }

        private static EvaluationResult WriteData(Context context, ModuleLiteral env, EvaluationStackFrame args)
        {
            return WriteFileHelper(context, env, args, WriteFileMode.WriteData);

        }

        private static EvaluationResult WriteAllLines(Context context, ModuleLiteral env, EvaluationStackFrame args)
        {
            return WriteFileHelper(context, env, args, WriteFileMode.WriteAllLines);
        }

        private static EvaluationResult WriteAllText(Context context, ModuleLiteral env, EvaluationStackFrame args)
        {
            return WriteFileHelper(context, env, args, WriteFileMode.WriteAllText);

        }

        private static EvaluationResult WriteFileHelper(Context context, ModuleLiteral env, EvaluationStackFrame args, WriteFileMode mode)
        {
            var path = Args.AsPath(args, 0, false);
            var tags = Args.AsStringArrayOptional(args, 2);
            var description = Args.AsStringOptional(args, 3);

            PipData pipData;
            switch (mode)
            {
                case WriteFileMode.WriteFile:
                    var fileContent = Args.AsIs(args, 1);
                    // WriteFile has a separator argument with default newline
                    var separator = Args.AsStringOptional(args, 3) ?? Environment.NewLine;
                    description = Args.AsStringOptional(args, 4);

                    pipData = CreatePipDataForWriteFile(context, fileContent, separator);
                    break;

                case WriteFileMode.WriteData:
                    var data = Args.AsIs(args, 1);
                    pipData = DataProcessor.ProcessData(context, context.FrontEndContext.PipDataBuilderPool, EvaluationResult.Create(data), new ConversionContext(pos: 1));
                    break;

                case WriteFileMode.WriteAllLines:
                    var lines = Args.AsArrayLiteral(args, 1);
                    var entry = context.TopStack;
                    var newData = ObjectLiteral.Create(
                        new List<Binding>
                        {
                                        new Binding(context.Names.DataSeparator, Environment.NewLine, entry.InvocationLocation),
                                        new Binding(context.Names.DataContents, lines, entry.InvocationLocation),
                        },
                        lines.Location,
                        entry.Path);

                    pipData = DataProcessor.ProcessData(context, context.FrontEndContext.PipDataBuilderPool, EvaluationResult.Create(newData), new ConversionContext(pos: 1));
                    break;

                case WriteFileMode.WriteAllText:
                    var text = Args.AsString(args, 1);
                    pipData = DataProcessor.ProcessData(context, context.FrontEndContext.PipDataBuilderPool, EvaluationResult.Create(text), new ConversionContext(pos: 1));
                    break;
                default:
                    throw Contract.AssertFailure("Unknown WriteFileMode.");
            }

            FileArtifact result;
            if (!context.GetPipConstructionHelper().TryWriteFile(path, pipData, WriteFileEncoding.Utf8, tags, description, out result))
            {
                // Error has been logged
                return EvaluationResult.Error;
            }

            return new EvaluationResult(result);
        }

        private static PipData CreatePipDataForWriteFile(Context context, object fileContent, string separator)
        {
            using (var pipDataBuilderWrapper = context.FrontEndContext.GetPipDataBuilder())
            {
                var pipDataBuilder = pipDataBuilderWrapper.Instance;
                var fileContentArray = fileContent as ArrayLiteral;
                if (fileContentArray != null)
                {
                    // fileContent is an array that needs to be joined
                    for (int i = 0; i < fileContentArray.Length; i++)
                    {
                        pipDataBuilder.Add(ConvertFileContentElement(context, fileContentArray[i], pos: i, objectContext: fileContentArray));
                    }

                    return pipDataBuilder.ToPipData(separator, PipDataFragmentEscaping.NoEscaping);
                }
                else
                {
                    // fileContent is a scalar
                    pipDataBuilder.Add(ConvertFileContentElement(context, EvaluationResult.Create(fileContent), pos: 0, objectContext: null));
                    return pipDataBuilder.ToPipData(string.Empty, PipDataFragmentEscaping.NoEscaping);
                }
            }
        }

        /// <nodoc />
        private enum WriteFileMode
        {
            /// <nodoc />
            WriteFile = 1,
            
            /// <nodoc />
            WriteData,
            
            /// <nodoc />
            WriteAllLines,

            /// <nodoc />
            WriteAllText
        }
    }
}
