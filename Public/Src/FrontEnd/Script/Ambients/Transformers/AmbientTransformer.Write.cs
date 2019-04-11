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

        private SymbolAtom m_writeOutputPath;
        private SymbolAtom m_writeTags;
        private SymbolAtom m_writeDescription;
        private SymbolAtom m_writeContents;
        private SymbolAtom m_writeLines;
        private SymbolAtom m_writeText;
        private SymbolAtom m_dataSeparator;
        private SymbolAtom m_dataContents;

        private void InitializeWriteNames()
        {
            m_writeOutputPath = Symbol("outputPath");
            m_writeTags = Symbol("tags");
            m_writeDescription = Symbol("description");
            m_writeContents = Symbol("contents");
            m_writeLines = Symbol("lines");
            m_writeText = Symbol("text");
            m_dataSeparator = Symbol("separator");
            m_dataContents = Symbol("contents");
        }

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

        private EvaluationResult WriteFile(Context context, ModuleLiteral env, EvaluationStackFrame args)
        {
            return WriteFileHelper(context, env, args, WriteFileMode.WriteFile);
        }

        private EvaluationResult WriteData(Context context, ModuleLiteral env, EvaluationStackFrame args)
        {
            return WriteFileHelper(context, env, args, WriteFileMode.WriteData);

        }

        private EvaluationResult WriteAllLines(Context context, ModuleLiteral env, EvaluationStackFrame args)
        {
            return WriteFileHelper(context, env, args, WriteFileMode.WriteAllLines);
        }

        private EvaluationResult WriteAllText(Context context, ModuleLiteral env, EvaluationStackFrame args)
        {
            return WriteFileHelper(context, env, args, WriteFileMode.WriteAllText);

        }

        private EvaluationResult WriteFileHelper(Context context, ModuleLiteral env, EvaluationStackFrame args, WriteFileMode mode)
        {
            AbsolutePath path;
            string[] tags;
            string description;
            PipData pipData;

            if (args.Length > 0 && args[0].Value is ObjectLiteral)
            {
                var obj = Args.AsObjectLiteral(args, 0);
                path = Converter.ExtractPath(obj, m_writeOutputPath, allowUndefined: false);
                tags = Converter.ExtractStringArray(obj, m_writeTags, allowUndefined: true);
                description = Converter.ExtractString(obj, m_writeDescription, allowUndefined: true);
                switch (mode)
                {
                    case WriteFileMode.WriteData:
                        var data = obj[m_writeContents];
                        pipData = ProcessData(context, data, new ConversionContext(pos: 1));
                        break;
                    case WriteFileMode.WriteAllLines:
                        var lines = Converter.ExtractArrayLiteral(obj, m_writeLines);
                        var entry = context.TopStack;
                        var newData = ObjectLiteral.Create(
                            new List<Binding>
                            {
                                new Binding(m_dataSeparator, Environment.NewLine, entry.InvocationLocation),
                                new Binding(m_dataContents, lines, entry.InvocationLocation),
                            },
                            lines.Location,
                            entry.Path);

                        pipData = ProcessData(context, EvaluationResult.Create(newData), new ConversionContext(pos: 1));
                        break;
                    case WriteFileMode.WriteAllText:
                        var text = Converter.ExtractString(obj, m_writeText);
                        pipData = ProcessData(context, EvaluationResult.Create(text), new ConversionContext(pos: 1));
                        break;
                    default:
                        throw Contract.AssertFailure("Unknown WriteFileMode.");
                }
            }
            else
            { 
                path = Args.AsPath(args, 0, false);
                tags = Args.AsStringArrayOptional(args, 2);
                description = Args.AsStringOptional(args, 3);

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
                        pipData = ProcessData(context, EvaluationResult.Create(data), new ConversionContext(pos: 1));
                        break;

                    case WriteFileMode.WriteAllLines:
                        var lines = Args.AsArrayLiteral(args, 1);
                        var entry = context.TopStack;
                        var newData = ObjectLiteral.Create(
                            new List<Binding>
                            {
                                            new Binding(m_dataSeparator, Environment.NewLine, entry.InvocationLocation),
                                            new Binding(m_dataContents, lines, entry.InvocationLocation),
                            },
                            lines.Location,
                            entry.Path);

                        pipData = ProcessData(context, EvaluationResult.Create(newData), new ConversionContext(pos: 1));
                        break;

                    case WriteFileMode.WriteAllText:
                        var text = Args.AsString(args, 1);
                        pipData = ProcessData(context, EvaluationResult.Create(text), new ConversionContext(pos: 1));
                        break;
                    default:
                        throw Contract.AssertFailure("Unknown WriteFileMode.");
                }
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

        private PipData ProcessData(Context context, EvaluationResult data, ConversionContext conversionContext)
        {
            return DataProcessor.ProcessData(context.StringTable, m_dataSeparator, m_dataContents, context.FrontEndContext.PipDataBuilderPool, data, conversionContext);
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
