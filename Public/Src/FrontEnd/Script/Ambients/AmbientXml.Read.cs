// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;
using BuildXL.Utilities;
using BuildXL.FrontEnd.Script.Ambients.Exceptions;
using BuildXL.FrontEnd.Script.Expressions;
using BuildXL.FrontEnd.Script.Types;
using BuildXL.FrontEnd.Script.Values;
using BuildXL.FrontEnd.Script.Evaluator;
using static BuildXL.Utilities.FormattableStringEx;

namespace BuildXL.FrontEnd.Script.Ambients
{
    /// <summary>
    /// Ambient definition that provides basic json functionality.
    /// </summary>
    public partial class AmbientXml : AmbientDefinitionBase
    {
        private CallSignature ReadSignature => CreateSignature(
            required: RequiredParameters(AmbientTypes.PathType),
            returnType: AmbientTypes.ObjectType);


        private static EvaluationResult Read(Context context, ModuleLiteral env, EvaluationStackFrame args)
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

            if (!context.FrontEndHost.Engine.TryGetFrontEndFile(file.Path, "ambient", out Stream stream))
            {
                throw new FileOperationException(
                new Exception(I($"Failed reading '{file.Path.ToString(context.PathTable)}'")));
            }
        
            try
            {
                var reader = XmlReader.Create(stream, new XmlReaderSettings
                {
                    DtdProcessing = DtdProcessing.Ignore,
                });
                var xmlContext = new XmlReadingContext(file.Path, context.PathTable, context.StringTable);
                var doc = ReadDocument(reader, xmlContext);
                return EvaluationResult.Create(doc);
            }
            catch (XmlException e)
            {
                throw new XmlReadException(file.Path.ToString(context.PathTable), e.LineNumber, e.LinePosition, e.Message, new ErrorContext(pos: 1));
            }
        }

        private static ObjectLiteral ReadDocument(XmlReader reader, in XmlReadingContext context)
        {
            var nodes = ReadNodes(reader, XmlNodeType.None, context);

            return ObjectLiteral.Create(
                new Binding(context.KindField, EvaluationResult.Create(XmlContext.DocumentKind), default),
                new Binding(context.NodesField, EvaluationResult.Create(nodes), default)
                );
        }

        private static ArrayLiteral ReadNodes(XmlReader reader, XmlNodeType endNode, in XmlReadingContext context)
        {
            var nodesList = new List<EvaluationResult>();

            while (reader.Read() && reader.NodeType != endNode)
            {
                var node = ReadNode(reader, in context);
                if (node != null)
                {
                    nodesList.Add(new EvaluationResult(node));
                }
            }

            return ArrayLiteral.CreateWithoutCopy(nodesList.ToArray(), default, context.Path);
        }

        private static ObjectLiteral ReadNode(XmlReader reader, in XmlReadingContext context)
        {
            switch (reader.NodeType)
            {
                case XmlNodeType.SignificantWhitespace:
                case XmlNodeType.Whitespace:
                case XmlNodeType.XmlDeclaration:
                case XmlNodeType.DocumentType:
                    // Skip
                    return null;
                case XmlNodeType.Text:
                    return ReadText(reader, context);
                case XmlNodeType.Comment:
                    return ReadComment(reader, context);
                case XmlNodeType.CDATA:
                    return ReadCData(reader, context);
                case XmlNodeType.Element:
                    return ReadElement(reader, context);
                case XmlNodeType.ProcessingInstruction:
                    return ReadProcessingInstruction(reader, context);
                default:
                    int line = 0;
                    int column = 0;
                    var lineInfo = reader as IXmlLineInfo;
                    if (lineInfo != null)
                    {
                        line = lineInfo.LineNumber;
                        column = lineInfo.LinePosition;
                    }
                    throw new XmlReadException(context.Path.ToString(context.PathTable), line, column, "Unsupported nodeTypes: " + reader.NodeType, new ErrorContext(pos: 1));
            }
        }
        private static ObjectLiteral ReadElement(XmlReader reader, in XmlReadingContext context)
        {
            var empty = reader.IsEmptyElement;
            var name = ReadName(reader, context);

            ArrayLiteral attributes;
            if (reader.MoveToFirstAttribute())
            {
                var attributeList = new List<EvaluationResult>();
                do
                {
                    var attr = CreateAttribute(reader, context);
                    attributeList.Add(new EvaluationResult(attr));
                } while (reader.MoveToNextAttribute());

                attributes = ArrayLiteral.CreateWithoutCopy(attributeList.ToArray(), default, context.Path);
                reader.MoveToElement();
            }
            else
            {
                attributes = ArrayLiteral.Create(new Expression[0], default, context.Path);
            }

            var nodes = empty 
                ? ArrayLiteral.Create(new Expression[0], default, context.Path)
                : ReadNodes(reader, XmlNodeType.EndElement, context);

            return ObjectLiteral.Create(
                new Binding(context.KindField, EvaluationResult.Create(XmlContext.ElementKind), default),
                new Binding(context.NameField, EvaluationResult.Create(name), default),
                new Binding(context.AttributesField, EvaluationResult.Create(attributes), default),
                new Binding(context.NodesField, EvaluationResult.Create(nodes), default)
                );
        }

        private static ObjectLiteral CreateAttribute(XmlReader reader, in XmlReadingContext context)
        {
            var attrName = ReadName(reader, context);
            return ObjectLiteral.Create(
                new Binding(context.KindField, EvaluationResult.Create(XmlContext.AttributeKind), default),
                new Binding(context.NameField, EvaluationResult.Create(attrName), default),
                new Binding(context.ValueField, EvaluationResult.Create(reader.Value), default)
                );
        }

        private static ObjectLiteral ReadName(XmlReader reader, in XmlReadingContext context)
        {
            return ObjectLiteral.Create(
                new Binding(context.NamespaceField, EvaluationResult.Create(reader.NamespaceURI), default),
                new Binding(context.PrefixField, EvaluationResult.Create(reader.Prefix), default),
                new Binding(context.LocalField, EvaluationResult.Create(reader.LocalName), default)
                );
        }

        private static ObjectLiteral ReadText(XmlReader reader, in XmlReadingContext context)
        {
            return ObjectLiteral.Create(
                new Binding(context.KindField, EvaluationResult.Create(XmlContext.TextKind), default),
                new Binding(context.TextField, EvaluationResult.Create(reader.Value), default)
                );
        }

        private static ObjectLiteral ReadComment(XmlReader reader, in XmlReadingContext context)
        {
            return ObjectLiteral.Create(
                new Binding(context.KindField, EvaluationResult.Create(XmlContext.CommentKind), default),
                new Binding(context.ValueField, EvaluationResult.Create(reader.Value), default)
                );
        }

        private static ObjectLiteral ReadCData(XmlReader reader, in XmlReadingContext context)
        {
            return ObjectLiteral.Create(
                new Binding(context.KindField, EvaluationResult.Create(XmlContext.CDataKind), default),
                new Binding(context.TextField, EvaluationResult.Create(reader.Value), default)
                );
        }

        private static ObjectLiteral ReadProcessingInstruction(XmlReader reader, in XmlReadingContext context)
        {
            return ObjectLiteral.Create(
                new Binding(context.KindField, EvaluationResult.Create(XmlContext.ProcessingInstructionKind), default),
                new Binding(context.NameField, EvaluationResult.Create(reader.Name), default),
                new Binding(context.ValueField, EvaluationResult.Create(reader.Value), default)
                );
        }

        private class XmlReadingContext : XmlContext
        {
            public AbsolutePath Path { get; }

            public PathTable PathTable { get; }

            public XmlReadingContext(AbsolutePath path, PathTable pathTable, StringTable stringTable)
                : base(stringTable)
            {
                Path = path;
                PathTable = pathTable;
            }
        }
    }
}
