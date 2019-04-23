// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.ContractsLight;
using System.Globalization;
using System.IO;
using System.Text;
using System.Xml;
using BuildXL.FrontEnd.Script.Ambients.Exceptions;
using BuildXL.FrontEnd.Script.Evaluator;
using BuildXL.FrontEnd.Script.Runtime;
using BuildXL.FrontEnd.Script.Types;
using BuildXL.FrontEnd.Script.Values;
using BuildXL.Pips.Operations;
using BuildXL.Utilities;

namespace BuildXL.FrontEnd.Script.Ambients
{
    /// <summary>
    /// Ambient definition that provides basic json functionality.
    /// </summary>
    public partial class AmbientXml : AmbientDefinitionBase
    {
        private CallSignature WriteSignature => CreateSignature(
            required: RequiredParameters(AmbientTypes.PathType, AmbientTypes.ObjectType),
            optional: OptionalParameters(AmbientTypes.ObjectType, new ArrayType(PrimitiveType.StringType), PrimitiveType.StringType),
            returnType: AmbientTypes.FileType);

        private static EvaluationResult Write(Context context, ModuleLiteral env, EvaluationStackFrame args)
        {
            var outputFilePath = Args.AsPath(args, 0);
            var obj = Args.AsObjectLiteral(args, 1);
            // Options don't have any settings yet, just here for future convenience
            var tags = Args.AsStringArrayOptional(args, 3);
            var description = Args.AsStringOptional(args, 4);

            using (var pipDataBuilderWrapper = context.FrontEndContext.GetPipDataBuilder())
            {
                var pipData = CreatePipData(context.StringTable, obj, pipDataBuilderWrapper.Instance);
                if (!pipData.IsValid)
                {
                    return EvaluationResult.Error;
                }

                FileArtifact result;
                if (!context.GetPipConstructionHelper().TryWriteFile(outputFilePath, pipData, WriteFileEncoding.Utf8, tags, description, out result))
                {
                    // Error has been logged
                    return EvaluationResult.Error;
                }

                return new EvaluationResult(result);
            }
        }

        /// <summary>
        /// Creates the PipData from the given object
        /// </summary>
        public static PipData CreatePipData(StringTable stringTable, ObjectLiteral obj, PipDataBuilder pipDataBuilder)
        {
            var settings = new XmlWriterSettings
            {
                Encoding = Encoding.UTF8,
                Indent = true,
            };

            using (var stringBuilderWrapper = Pools.StringBuilderPool.GetInstance())
            {
                var stringBuilder = stringBuilderWrapper.Instance;
                using (var writer = new UTF8StringWriter(stringBuilder))
                using (var xmlWriter = XmlWriter.Create(writer, settings))
                {

                    var xmlContext = new XmlWritingContext(stringTable, pipDataBuilder, stringBuilderWrapper.Instance, xmlWriter);

                    if (!WriteNode(obj, in xmlContext))
                    {
                        return PipData.Invalid;
                    }

                    FlushXmlToPipBuilder(in xmlContext);

                    return pipDataBuilder.ToPipData(string.Empty, PipDataFragmentEscaping.NoEscaping);
                }
            }
        }

        /// <summary>
        /// Wrapper class to allow the XmlWriter to write UTF-8 encoding into the stringbuilder
        /// </summary>
        private class UTF8StringWriter : StringWriter
        {
            /// <nodoc />
            public UTF8StringWriter(StringBuilder builder) : base(builder)
            {
            }

            /// <inheritdoc />
            public override Encoding Encoding
            {
                get { return Encoding.UTF8; }
            }
        }


        private static bool WriteNode(ObjectLiteral obj, in XmlWritingContext context)
        {
            var kind = Converter.ExtractString(obj, context.KindField);
            switch (kind)
            {
                case XmlContext.DocumentKind:
                    return WriteDocument(obj, in context);
                case XmlContext.ElementKind:
                    return WriteElement(obj, in context);
                case XmlContext.TextKind:
                    return WriteText(obj, in context);
                case XmlContext.CDataKind:
                    return WriteCData(obj, in context);
                case XmlContext.CommentKind:
                    return WriteComment(obj, in context);
                case XmlContext.ProcessingInstructionKind:
                    return WriteProcessingInstruction(obj, in context);
                default:
                    // Note that attributes are deliberately not here.
                    throw new XmlInvalidStructureException(kind, string.Join("|", XmlContext.AllKinds), "kind", "node", new ErrorContext(pos: 1));
            }
        }

        private static bool WriteDocument(ObjectLiteral doc, in XmlWritingContext context)
        {
            context.XmlWriter.WriteStartDocument();
            var nodes = Converter.ExtractArrayLiteral(doc, context.NodesField, allowUndefined: true);
            WriteNodes(nodes, context);
            context.XmlWriter.WriteEndDocument();
            return true;
        }

        private static bool WriteElement(ObjectLiteral elem, in XmlWritingContext context)
        {
            GetNames(elem, context, out var prefix, out var localName, out var ns);
            context.XmlWriter.WriteStartElement(prefix, localName, ns);

            var attributes = Converter.ExtractArrayLiteral(elem, context.AttributesField, allowUndefined: true);
            WriteAttributes(attributes, context);

            var nodes = Converter.ExtractArrayLiteral(elem, context.NodesField, allowUndefined: true);
            WriteNodes(nodes, context);

            context.XmlWriter.WriteEndElement();
            return true;
        }

        private static void GetNames(ObjectLiteral node, in XmlWritingContext context, out string prefix, out string localName, out string ns)
        {
            var nameObj = Converter.ExtractObjectLiteral(node, context.NameField);

            prefix = Converter.ExtractString(nameObj, context.PrefixField, allowUndefined: true);
            localName = Converter.ExtractString(nameObj, context.LocalField);
            ns = Converter.ExtractString(nameObj, context.NamespaceField, allowUndefined: true);
        }


        private static bool WriteAttributes(ArrayLiteral attrs, in XmlWritingContext context)
        {
            if (attrs == null)
            {
                return true;
            }

            foreach (var attr in attrs)
            {
                if (attr.IsErrorValue)
                {
                    return false;
                }

                if (attr.IsUndefined)
                {
                    continue;
                }

                var attrObj = Converter.ExpectObjectLiteral(attr);

                GetNames(attrObj, context, out string prefix, out string localName, out string ns);
                context.XmlWriter.WriteStartAttribute(prefix, localName, ns);

                var value = attrObj[context.ValueField];
                if (!WriteValue(value, context))
                {
                    return false;
                }

                context.XmlWriter.WriteEndAttribute();
            }

            return true;
        }


        private static bool WriteNodes(ArrayLiteral nodes, in XmlWritingContext context)
        {
            if (nodes == null)
            {
                return true;
            }

            foreach (var node in nodes)
            {
                if (node.IsErrorValue)
                {
                    return false;
                }

                if (node.IsUndefined)
                {
                    continue;
                }

                var objectLiteral = node.Value as ObjectLiteral;
                if (objectLiteral != null)
                {
                    WriteNode(objectLiteral, context);
                }
                else
                {
                    var typeOfKind = RuntimeTypeIdExtensions.ComputeTypeOfKind(node.Value);
                    throw new XmlInvalidStructureException(typeOfKind.ToRuntimeString(), "node", "nodes[]", "Document", new ErrorContext(pos: 1));
                }
            }

            return true;
        }

        private static bool WriteText(ObjectLiteral text, in XmlWritingContext context)
        {
            return WriteValue(text[context.TextField], context);
        }

        private static bool WriteCData(ObjectLiteral text, in XmlWritingContext context)
        {
            var value = text[context.ValueField];
            if (value.IsErrorValue)
            {
                return false;
            }

            string strValue = value.Value as string;

            if (strValue != null)
            {
                context.XmlWriter.WriteCData(strValue);
                return true;
            }
            else
            {
                // There is no start and end comment, so there is no way to intercept it.
                // Therefore we write an empty one and rewind over it.
                context.XmlWriter.WriteCData(string.Empty);
                return WriteRawWithRewind(text[context.TextField], "]]>", context);
            }
        }

        private static bool WriteComment(ObjectLiteral text, in XmlWritingContext context)
        {
            var value = text[context.ValueField];
            if (value.IsErrorValue)
            {
                return false;
            }

            string strValue = value.Value as string;

            if (strValue != null)
            {
                context.XmlWriter.WriteComment(strValue);
                return true;
            }
            else
            {
                // There is no start and end comment, so there is no way to intercept it.
                // Therefore we write an empty one and rewind over it.
                context.XmlWriter.WriteComment(string.Empty);
                return WriteRawWithRewind(text[context.ValueField], "-->", context);
            }
        }

        private static bool WriteProcessingInstruction(ObjectLiteral pi, in XmlWritingContext context)
        {
            var name = Converter.ExtractString(pi, context.NameField);
            var value = Converter.ExtractString(pi, context.ValueField);

            context.XmlWriter.WriteProcessingInstruction(name, value);
            return true;
        }

        private static bool TryExtractSingleValueAsString(EvaluationResult value, out string stringValue)
        {
            if (!value.IsErrorValue && !value.IsUndefined)
            {
                stringValue = value.Value as string;
                return stringValue != null;
            }

            stringValue = null;
            return false;
        }

        private static bool WriteRawWithRewind(EvaluationResult value, string end, in XmlWritingContext context)
        {
            // Ensure the xml is flushed completely to the stringbulder
            context.XmlWriter.Flush();

            // Capture the state written so far
            var builder = context.StringBuilder;
            var writtenSoFar = builder.ToString();
            Contract.Assert(writtenSoFar.EndsWith(end));

            builder.Clear();
            builder.Append(writtenSoFar.Substring(0, writtenSoFar.Length - end.Length));
            
            // emit the cdata content
            if (!WriteRaw(value, context))
            {
                return false;
            }

            // And manually emit the ]]>
            context.XmlWriter.WriteRaw(end);

            return true;
        }

        private static bool WriteValue(EvaluationResult value, in XmlWritingContext context)
        {
            if (value.IsErrorValue)
            {
                return false;
            }

            if (value.IsUndefined)
            {
                // No fields, nothing to write
                return true;
            }

            switch (value.Value)
            {
                case string strValue:
                    context.XmlWriter.WriteValue(strValue);
                    return true;
                case int intValue:
                    context.XmlWriter.WriteValue(intValue);
                    return true;
                case bool boolValue:
                    context.XmlWriter.WriteValue(boolValue);
                    return true;
                case PathAtom pathAtomValue:
                    context.XmlWriter.WriteValue(pathAtomValue.ToString(context.StringTable));
                    return true;
                case RelativePath relPathValue:
                    context.XmlWriter.WriteValue(relPathValue.ToString(context.StringTable));
                    return true;

                case ArrayLiteral arrayValue:
                    foreach (var item in arrayValue.Values)
                    {
                        if (!WriteValue(item, context))
                        {
                            return false;
                        }
                    }
                    return true;
                case AbsolutePath pathValue:
                    return WritePath(pathValue, context);
                case FileArtifact fileValue:
                    return WritePath(fileValue.Path, context);
                case DirectoryArtifact dirValue:
                    return WritePath(dirValue.Path, context);

                default:
                    var typeOfKind = RuntimeTypeIdExtensions.ComputeTypeOfKind(value.Value);
                    throw new XmlUnsuportedTypeForSerializationException(typeOfKind.ToRuntimeString(), new ErrorContext(pos: 1));
            }
        }

        private static bool WriteRaw(EvaluationResult value, in XmlWritingContext context)
        {
            if (value.IsErrorValue)
            {
                return false;
            }

            if (value.IsUndefined)
            {
                // No fields, nothing to write
                return true;
            }

            switch (value.Value)
            {
                case string strValue:
                    context.XmlWriter.WriteRaw(strValue);
                    return true;
                case int intValue:
                    context.XmlWriter.WriteRaw(intValue.ToString(CultureInfo.InvariantCulture));
                    return true;
                case bool boolValue:
                    context.XmlWriter.WriteRaw(boolValue ? "true" : "false");
                    return true;
                case PathAtom pathAtomValue:
                    context.XmlWriter.WriteRaw(pathAtomValue.ToString(context.StringTable));
                    return true;
                case RelativePath relPathValue:
                    context.XmlWriter.WriteRaw(relPathValue.ToString(context.StringTable));
                    return true;

                case ArrayLiteral arrayValue:
                    foreach (var item in arrayValue.Values)
                    {
                        if (!WriteRaw(item, context))
                        {
                            return false;
                        }
                    }
                    return true;
                case AbsolutePath pathValue:
                    return WritePath(pathValue, context);
                case FileArtifact fileValue:
                    return WritePath(fileValue.Path, context);
                case DirectoryArtifact dirValue:
                    return WritePath(dirValue.Path, context);

                default:
                    var typeOfKind = RuntimeTypeIdExtensions.ComputeTypeOfKind(value.Value);
                    throw new XmlUnsuportedTypeForSerializationException(typeOfKind.ToRuntimeString(), new ErrorContext(pos: 1));
            }
        }

        private static bool WritePath(AbsolutePath path, in XmlWritingContext context)
        {
            FlushXmlToPipBuilder(context);
            context.PipDataBuilder.Add(path);
            return true;
        }

        private static void FlushXmlToPipBuilder(in XmlWritingContext context)
        {
            // Flush the text
            context.XmlWriter.Flush();

            var xmlSoFar = context.StringBuilder.ToString();
            context.StringBuilder.Clear();
            context.PipDataBuilder.Add(xmlSoFar);
        }

        private class XmlWritingContext : XmlContext
        {
            public PipDataBuilder PipDataBuilder { get; }
            public StringBuilder StringBuilder { get; }
            public XmlWriter XmlWriter{ get; }


            public XmlWritingContext(StringTable stringTable, PipDataBuilder pipDataBuilder, StringBuilder stringBuilder, XmlWriter xmlWriter)
                : base(stringTable)
            {
                PipDataBuilder = pipDataBuilder;
                StringBuilder = stringBuilder;
                XmlWriter = xmlWriter;
            }
        }
    }
}
