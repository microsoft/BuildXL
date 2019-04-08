// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using BuildXL.Pips.Operations;
using BuildXL.Utilities;
using BuildXL.FrontEnd.Script.Ambients.Exceptions;
using BuildXL.FrontEnd.Script.Runtime;
using BuildXL.FrontEnd.Script.Ambients.Map;
using BuildXL.FrontEnd.Script.Ambients.Set;
using BuildXL.FrontEnd.Script.Types;
using BuildXL.FrontEnd.Script.Values;
using BuildXL.FrontEnd.Script.Evaluator;
using Newtonsoft.Json;

namespace BuildXL.FrontEnd.Script.Ambients
{
    /// <summary>
    /// Ambient definition that provides basic json functionality.
    /// </summary>
    public sealed class AmbientJson : AmbientDefinitionBase
    {
        // The field has a triple underscore because internally the parser
        // mangles double underscore identifiers to distinguish them from
        // internally injected ones, which have double underscore as a prefix
        private const string jsonDynamicFields = @"___dynamicFields";

        /// <nodoc />
        public AmbientJson(PrimitiveTypes knownTypes)
            : base("Json", knownTypes)
        {
        }

        /// <nodoc />
        protected override AmbientNamespaceDefinition? GetNamespaceDefinition()
        {
            return new AmbientNamespaceDefinition(
                AmbientHack.GetName("Json"),
                new[]
                {
                    Function("write", Write, WriteSignature),
                });
        }

        private static EvaluationResult Write(Context context, ModuleLiteral env, EvaluationStackFrame args)
        {
            var outputFilePath = Args.AsPath(args, 0);
            var obj = Args.AsObjectLiteral(args, 1);
            var quoteChar = Args.AsStringOptional(args, 2) ?? "\'";
            var tags = Args.AsStringArrayOptional(args, 3);
            var description = Args.AsStringOptional(args, 4);

            using (var pipDataBuilderWrapper = context.FrontEndContext.GetPipDataBuilder())
            {
                var pipData = CreatePipData(context.StringTable, obj, quoteChar, pipDataBuilderWrapper.Instance);
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
        public static PipData CreatePipData(StringTable stringTable, ObjectLiteral obj, string quoteChar, PipDataBuilder pipDataBuilder)
        {
            using (var stringBuilderWrapper = Pools.StringBuilderPool.GetInstance())
            {
                var stringBuilder = stringBuilderWrapper.Instance;
                using (var textWriter = new StringWriter(stringBuilder))
                using (var jsonWriter = new JsonTextWriter(textWriter))
                {
                    // textWriter.NewLine = "\n";
                    // Note, we do NOT Force consisten newline endings across platforms.
                    // After talking with the Mac team, it seems there is no hope we'll ever have shared cache entries between them.
                    // As a feature we might at some point allow the pip author to parametrise this (as well as the path separators used) but keeping it simple for now.

                    jsonWriter.Culture = CultureInfo.InvariantCulture;
                    jsonWriter.Formatting = Formatting.Indented;
                    jsonWriter.Indentation = 2;

                    // The JSON standard mandates the double quote as the conform quotation symbol with single quotes
                    // only being used in custom implementations by consumers, allow both
                    if (quoteChar != "\'" && quoteChar != "\"")
                    {
                        return PipData.Invalid;
                    }

                    jsonWriter.IndentChar = ' ';
                    jsonWriter.QuoteChar = Convert.ToChar(quoteChar);
                    jsonWriter.AutoCompleteOnClose = false;
                    var jsonContext = new JsonWritingContext(stringTable, pipDataBuilder, stringBuilderWrapper.Instance, jsonWriter);

                    if (!WriteObject(obj, in jsonContext))
                    {
                        return PipData.Invalid;
                    }

                    FlushJsonToPipBuilder(in jsonContext);

                    return pipDataBuilder.ToPipData(string.Empty, PipDataFragmentEscaping.NoEscaping);
                }
            }
        }

        private CallSignature WriteSignature => CreateSignature(
            required: RequiredParameters(AmbientTypes.PathType, AmbientTypes.ObjectType),
            optional: OptionalParameters(PrimitiveType.StringType, new ArrayType(PrimitiveType.StringType), PrimitiveType.StringType),
            returnType: AmbientTypes.FileType);


        private static bool WriteObject(ObjectLiteral obj, in JsonWritingContext context)
        {
            context.JsonWriter.WriteStartObject();

            foreach (var kv in obj.Members)
            {
                var keyName = kv.Key.ToString(context.StringTable);

                if (string.Equals(keyName, jsonDynamicFields, System.StringComparison.Ordinal))
                {
                    if (!WriteDynamicFields(kv.Value, in context))
                    {
                        return false;
                    }
                }
                else
                {
                    context.JsonWriter.WritePropertyName(keyName);
                    if (!WriteValue(kv.Value, in context))
                    {
                        return false;
                    }
                }
            }

            context.JsonWriter.WriteEndObject();
            return true;
        }

        private static bool WriteDynamicFields(EvaluationResult value, in JsonWritingContext context)
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

            var array = value.Value as ArrayLiteral;
            if (array == null)
            {
                var typeOfKind = RuntimeTypeIdExtensions.ComputeTypeOfKind(value.Value);
                throw new JsonUnsuportedDynamicFieldsForSerializationException(typeOfKind.ToRuntimeString(), "Json.DynamicObject", new ErrorContext(pos: 1));
            }

            var jsonDynamicName = StringId.Create(context.StringTable, "name");
            var jsonDynamicValue = StringId.Create(context.StringTable, "value");


            foreach (var item in array.Values)
            {
                if (value.IsErrorValue)
                {
                    return false;
                }

                if (value.IsUndefined)
                {
                    // Eat this field
                    continue; 
                }

                var itemObj = item.Value as ObjectLiteral;
                if (itemObj != null)
                {
                    // We now expect a name and value field
                    var dynamicName = itemObj[jsonDynamicName];
                    var dynamicValue = itemObj[jsonDynamicValue];

                    if (dynamicName.IsErrorValue || dynamicValue.IsErrorValue)
                    {
                        return false;
                    }

                    var name = dynamicName.Value as string;
                    if (!string.IsNullOrEmpty(name))
                    {
                        context.JsonWriter.WritePropertyName(name);
                        if (!WriteValue(dynamicValue, in context))
                        {
                            return false;
                        }

                        // Succesfully written the field
                        continue;
                    }
                }

                // Fall through error case
                var typeOfKind = RuntimeTypeIdExtensions.ComputeTypeOfKind(itemObj);
                throw new JsonUnsuportedDynamicFieldsForSerializationException(typeOfKind.ToRuntimeString(), "Json.DynamicField", new ErrorContext(pos: 1));
            }

            return true;
        }

        private static bool WriteArray(ArrayLiteral array, in JsonWritingContext context)
        {
            context.JsonWriter.WriteStartArray();

            foreach (var item in array.Values)
            {
                if (!WriteValue(item, in context))
                {
                    return false;
                }
            }

            context.JsonWriter.WriteEndArray();
            return true;
        }

        private static bool WriteSet(OrderedSet set, in JsonWritingContext context)
        {
            context.JsonWriter.WriteStartArray();

            foreach (var item in set)
            {
                if (!WriteValue(item, in context))
                {
                    return false;
                }
            }

            context.JsonWriter.WriteEndArray();
            return true;
        }

        private static bool WriteMap(OrderedMap map, in JsonWritingContext context)
        {
            context.JsonWriter.WriteStartArray();

            foreach (var kv in map)
            {
                context.JsonWriter.WriteStartObject();

                context.JsonWriter.WritePropertyName("key");
                if (!WriteValue(kv.Key, in context))
                {
                    return false;
                }

                context.JsonWriter.WritePropertyName("value");
                if (!WriteValue(kv.Value, in context))
                {
                    return false;
                }

                context.JsonWriter.WriteEndObject();
            }

            context.JsonWriter.WriteEndArray();
            return true;
        }

        private static bool WritePath(AbsolutePath path, in JsonWritingContext context)
        {
            var quoteChar = context.JsonWriter.QuoteChar.ToString();

            // Write the raw quote to Json. We must use WriteRawValue here so that the writer ends up in a proper state for the next property or close bracket.
            context.JsonWriter.WriteRawValue(quoteChar);

            // Ensure the pipBuilder has the json written so far as a string in the pip.
            // This will reset the string builder so the next flush will continue.
            FlushJsonToPipBuilder(in context);

            // Add the Path to the pip builder. We want to always keeps Paths as Paths in the PipData for WriteFile just like we
            // do for paths in the arguments. The reason is that we can normalize hashes like if the path is c:\users\MyUser\foobar and hash it like %USER%\foobar
            // to ensure that the pips that write files to local drives with user names hash properly based on the mountpoints.
            context.PipDataBuilder.Add(path);

            // Continue the json writer with the closing quote.
            context.JsonWriter.WriteRaw(quoteChar);

            return true;
        }

        private static void FlushJsonToPipBuilder(in JsonWritingContext context)
        {
            // Flush the text
            context.JsonWriter.Flush();

            var jsonSoFar = context.StringBuilder.ToString();
            context.StringBuilder.Clear();
            context.PipDataBuilder.Add(jsonSoFar);
        }

        private static bool WriteValue(EvaluationResult result, in JsonWritingContext context)
        {
            if (result.IsErrorValue)
            {
                return false;
            }

            if (result.IsUndefined)
            {
                context.JsonWriter.WriteNull();
                return true;
            }

            var objValue = result.Value;
            switch (objValue)
            {
                case string strValue:
                    context.JsonWriter.WriteValue(strValue);
                    return true;
                case int intValue:
                    context.JsonWriter.WriteValue(intValue);
                    return true;
                case bool boolValue:
                    context.JsonWriter.WriteValue(boolValue);
                    return true;
                case ArrayLiteral arrayValue:
                    return WriteArray(arrayValue, in context);
                case OrderedMap map:
                    return WriteMap(map, in context);
                case OrderedSet set:
                    return WriteSet(set, in context);
                case ObjectLiteral objectValue:
                    return WriteObject(objectValue, in context);
                case PathAtom pathAtomValue:
                    context.JsonWriter.WriteValue(pathAtomValue.ToString(context.StringTable));
                    return true;
                case RelativePath relPathValue:
                    context.JsonWriter.WriteValue(relPathValue.ToString(context.StringTable));
                    return true;
                case AbsolutePath pathValue:
                    return WritePath(pathValue, in context);
                case FileArtifact fileValue:
                    return WritePath(fileValue.Path, in context);
                case DirectoryArtifact dirValue:
                    return WritePath(dirValue.Path, in context);

                default:
                    var typeOfKind = RuntimeTypeIdExtensions.ComputeTypeOfKind(objValue);
                    throw new JsonUnsuportedTypeForSerializationException(typeOfKind.ToRuntimeString(), new ErrorContext(pos: 1));
            }
        }

        private readonly struct JsonWritingContext
        {
            public StringTable StringTable { get; }
            public PipDataBuilder PipDataBuilder { get; }
            public StringBuilder StringBuilder { get; }
            public JsonTextWriter JsonWriter { get; }

            public JsonWritingContext(StringTable stringTable, PipDataBuilder pipDataBuilder, StringBuilder stringBuilder, JsonTextWriter jsonWriter)
            {
                StringTable = stringTable;
                PipDataBuilder = pipDataBuilder;
                StringBuilder = stringBuilder;
                JsonWriter = jsonWriter;
            }
        }
    }
}
