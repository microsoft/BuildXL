// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Globalization;
using BuildXL.FrontEnd.Script.Ambients.Map;
using BuildXL.FrontEnd.Script.Ambients.Set;
using BuildXL.FrontEnd.Script.Evaluator;
using BuildXL.FrontEnd.Script.Values;
using BuildXL.Pips;
using BuildXL.Utilities;
using TypeScript.Net.Types;
using static BuildXL.Utilities.FormattableStringEx;

namespace BuildXL.FrontEnd.Script
{
    /// <summary>
    /// Class that creates a string representation of the runtime object.
    /// </summary>
    /// <remarks>
    /// Note, that in order to get string representation for <code>object.toString()</code>
    /// this class should used! Please don't call Expression.ToString method!
    /// </remarks>
    public static class ToStringConverter
    {
        private delegate void WriteObjectFunction(ScriptWriter writer, ImmutableContextBase context, object value);

        private static readonly Dictionary<System.Type, WriteObjectFunction> s_toStringMap = InitWriteMap();

        /// <summary>
        /// Gets a string representation of an object.
        /// </summary>
        /// <remarks>This method never throws any exception.</remarks>
        public static string ObjectToString(ImmutableContextBase context, EvaluationResult value) => ObjectToString(context, value.Value);
        
        /// <summary>
        /// Gets a string representation of an object.
        /// </summary>
        /// <remarks>This method never throws any exception.</remarks>
        public static string ObjectToString(ImmutableContextBase context, object value)
        {
            // The outer most ToString is used everywhere to emit it like string interpolation.
            // So therefore this methods unwrap the outermost string but lets nested strings be printed with quotes.
            if (value is string stringValue)
            {
                return stringValue;
            }

            // TODO: We should not be able to get the string representation of a path atom as is.
            // TODO: But some of our runners (e.g, resgen) do it.
            if (value is PathAtom pathAtom)
            {
                return pathAtom.ToString(context.StringTable);
            }

            using (var writer = new ScriptWriter())
            {
                WriteObject(writer, context, value);
                return writer.ToString();
            }
        }

        /// <nodoc />
        public static void WriteObject(ScriptWriter writer, ImmutableContextBase context, object value)
        {
            if (value == null)
            {
                writer.AppendToken("undefined");
                return;
            }

            if (value is EvaluationResult result)
            {
                WriteObject(writer, context, result.Value);
                return;
            }

            // One of the types in the map is generic (ObjectLiteralLight<>).
            // To get a match from the dictionary we need to get open generic type
            // from already constructed type that would be provided by value.GetType().
            var type = value.GetType();
            if (type.IsGenericType)
            {
                type = type.GetGenericTypeDefinition();
            }

            if (s_toStringMap.TryGetValue(type, out WriteObjectFunction writeFunction))
            {
                writeFunction(writer, context, value);
            }
            else
            {
                writer.AppendLine(I($"Don't know how to get a string representation of '{type}'"));
            }
        }

        private static Dictionary<System.Type, WriteObjectFunction> InitWriteMap()
        {
            return new Dictionary<System.Type, WriteObjectFunction>
            {
                [typeof(bool)] = WriteBool,
                [typeof(int)] = WriteInt,
                [typeof(string)] = WriteString,
                [typeof(UndefinedValue)] = GetWriteLiteralFunction("undefined"),
                [typeof(PathAtom)] = WritePathAtom,
                [typeof(AbsolutePath)] = WriteAbsolutePath,
                [typeof(RelativePath)] = WriteRelativePath,
                [typeof(FileArtifact)] = WriteFileArtifact,
                [typeof(DirectoryArtifact)] = WriteDirectoryArtifact,
                [typeof(StaticDirectory)] = WriteStaticDirectory,
                [typeof(ArrayLiteral)] = WriteArrayLiteral,
                [typeof(EvaluatedArrayLiteral)] = WriteArrayLiteral,
                [typeof(ObjectLiteral0)] = GetWriteLiteralFunction("{}"),
                [typeof(ObjectLiteralSlim<>)] = WriteObjectLiteralSlim,
                [typeof(ObjectLiteralN)] = WriteObjectLiteral,
                [typeof(OrderedSet)] = WriteOrderedSet,
                [typeof(OrderedMap)] = WriteOrderedMap,
                [typeof(EnumValue)] = WriteEnumValue,
                [typeof(Closure)] = WriteFunction,
                [typeof(CallableValue)] = GetWriteLiteralFunction("<ambient>"),
                [typeof(StringId)] = WriteStringId,
            };
        }

        private static WriteObjectFunction GetWriteLiteralFunction(string literal)
        {
            return (writer, contet, value) => { writer.AppendToken(literal); };
        }

        private static void WriteBool(ScriptWriter writer, ImmutableContextBase context, object value)
        {
            writer.AppendToken((bool)value == true ? "true" : "false");
        }

        private static void WriteFunction(ScriptWriter writer, ImmutableContextBase context, object value)
        {
            var lambda = ((Closure)value).Function;

            writer.AppendToken("<function:");
            if (lambda.Name.IsValid)
            {
                writer.AppendToken(lambda.Name.ToString(context.StringTable));
                writer.AppendToken(lambda.CallSignature.ToDebugString());
            }
            else
            {
                writer.AppendToken(lambda.CallSignature.ToDebugString());
                writer.AppendToken(" => ...");
            }

            writer.AppendToken(">");
        }

        private static void WriteInt(ScriptWriter writer, ImmutableContextBase context, object value)
        {
            writer.AppendToken(((int)value).ToString(CultureInfo.InvariantCulture));
        }

        private static void WriteString(ScriptWriter writer, ImmutableContextBase context, object value)
        {
            writer.AppendQuotedString((string)value, false);
        }

        private static void WritePathAtom(ScriptWriter writer, ImmutableContextBase context, object value)
        {
            var atom = (PathAtom)value;
            writer.AppendToken(Constants.Names.PathAtomInterpolationFactory.ToString());
            writer.AppendQuotedString(atom.ToString(context.StringTable), false, '`');
        }

        private static void WriteAbsolutePath(ScriptWriter writer, ImmutableContextBase context, object value)
        {
            var path = (AbsolutePath)value;
            writer.AppendToken(Constants.Names.PathInterpolationFactory.ToString());
            writer.AppendQuotedString(path.ToString(context.PathTable, PathFormat.Script), true, '`');
        }

        private static void WriteRelativePath(ScriptWriter writer, ImmutableContextBase context, object value)
        {
            var relativePath = (RelativePath)value;
            writer.AppendToken(Constants.Names.RelativePathInterpolationFactory.ToString());
            writer.AppendQuotedString(relativePath.ToString(context.StringTable, PathFormat.Script), true, '`');
        }

        private static void WriteFileArtifact(ScriptWriter writer, ImmutableContextBase context, object value)
        {
            var file = (FileArtifact)value;
            writer.AppendToken(Constants.Names.FileInterpolationFactory.ToString());
            writer.AppendQuotedString(file.Path.ToString(context.PathTable, PathFormat.Script), true, '`');
        }

        private static void WriteDirectoryArtifact(ScriptWriter writer, ImmutableContextBase context, object value)
        {
            var directory = (DirectoryArtifact)value;
            writer.AppendToken(Constants.Names.DirectoryInterpolationFactory.ToString());
            writer.AppendQuotedString(directory.Path.ToString(context.PathTable, PathFormat.Script), true, '`');
        }

        private static void WriteStaticDirectory(ScriptWriter writer, ImmutableContextBase context, object value)
        {
            var directory = (StaticDirectory)value;
            writer.AppendToken(Constants.Names.DirectoryInterpolationFactory.ToString());
            writer.AppendQuotedString(directory.Path.ToString(context.PathTable, PathFormat.Script), true, '`');
        }

        private static void WriteArrayLiteral(ScriptWriter writer, ImmutableContextBase context, object value)
        {
            var arrayLiteral = (ArrayLiteral)value;

            writer.AppendItems(arrayLiteral.Values, "[", "]", ",",
                item => WriteObject(writer, context, item.Value));
        }

        private static void WriteObjectLiteralSlim(ScriptWriter writer, ImmutableContextBase context, object value)
        {
            var literal = (ObjectLiteral)value;
            WriteObjectLiteral(writer, context, literal);
        }

        private static void WriteObjectLiteral(ScriptWriter writer, ImmutableContextBase context, object value)
        {
            var objLiteral = (ObjectLiteral)value;

            writer.AppendItems(objLiteral.Members, "{", "}", ",",
                member =>
                {
                    writer.AppendToken(member.Key.ToString(context.StringTable));
                    writer.AppendToken(": ");
                    WriteObject(writer, context, member.Value.Value);
                });
        }

        private static void WriteOrderedSet(ScriptWriter writer, ImmutableContextBase context, object value)
        {
            var set = (OrderedSet)value;
            writer.AppendItems(set, "<Set>[", "]", ",",
                item => WriteObject(writer, context, item.Value));
        }

        private static void WriteOrderedMap(ScriptWriter writer, ImmutableContextBase context, object value)
        {
            var objLiteralN = (OrderedMap)value;

            writer.AppendItems(objLiteralN, "<Map>[", "]", ",",
                item =>
                {
                    writer.AppendToken("{ key: ");
                    WriteObject(writer, context, item.Key.Value);
                    writer.AppendToken(", value: ");
                    WriteObject(writer, context, item.Value.Value);
                    writer.AppendToken("}");
                });
        }

        private static void WriteEnumValue(ScriptWriter writer, ImmutableContextBase context, object value)
        {
            var enumValue = (EnumValue)value;
            writer.AppendToken(enumValue.Name.ToString(context.StringTable));
        }

        private static void WriteStringId(ScriptWriter writer, ImmutableContextBase context, object value)
        {
            var stringId = (StringId)value;
            writer.AppendToken(stringId.IsValid ? stringId.ToString(context.StringTable) : "invalid");
        }
    }
}
