// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using BuildXL.Utilities;
using BuildXL.FrontEnd.Script.Values;

using static BuildXL.Utilities.FormattableStringEx;

using LineInfo = TypeScript.Net.Utilities.LineInfo;

namespace BuildXL.FrontEnd.Script.Literals
{
    /// <summary>
    /// Marker interface that represent a constant expression.
    /// </summary>
    public interface IConstantExpression
    {
        /// <summary>
        /// Returns boxed value of a constant expression.
        /// </summary>
        object Value { get; }

        /// <summary>
        /// Location of the constant expression
        /// </summary>
        LineInfo Location { get; }
    }

    /// <summary>
    /// Kind of a constant expression.
    /// </summary>
    public enum ConstExpressionKind : byte
    {
        /// <nodoc />
        Node,

        /// <nodoc />
        Number,

        /// <nodoc />
        Boolean,

        /// <nodoc />
        EnumValue,

        /// <nodoc />
        String,

        /// <nodoc />
        File,

        /// <nodoc />
        Path,

        /// <nodoc />
        RelativePath,

        /// <nodoc />
        UndefinedLiteral,

        /// <nodoc />
        UndefinedValue,
    }

    /// <summary>
    /// Helper responsible for serializing/deserializing const expressions.
    /// </summary>
    public static class ConstExpressionSerializer
    {
        /// <nodoc />
        public static object Read(DeserializationContext context)
        {
            var reader = context.Reader;
            bool isNode = reader.ReadBoolean();
            if (isNode)
            {
                return (IConstantExpression)Node.Read(context);
            }

            return ReadConstValue(reader);
        }

        /// <nodoc />
        public static object ReadConstValue(BuildXLReader reader)
        {
            var kind = (ConstExpressionKind)reader.ReadByte();
            switch (kind)
            {
                case ConstExpressionKind.Number:
                    return reader.ReadInt32Compact();
                case ConstExpressionKind.Boolean:
                    return reader.ReadBoolean();
                case ConstExpressionKind.EnumValue:
                    return new EnumValue(reader.ReadSymbolAtom(), reader.ReadInt32Compact());
                case ConstExpressionKind.String:
                    return reader.ReadString();
                case ConstExpressionKind.File:
                    return reader.ReadFileArtifact();
                case ConstExpressionKind.Path:
                    return reader.ReadAbsolutePath();
                case ConstExpressionKind.RelativePath:
                    return reader.ReadRelativePath();
                case ConstExpressionKind.UndefinedLiteral:
                    return UndefinedLiteral.Instance;
                case ConstExpressionKind.UndefinedValue:
                    return UndefinedValue.Instance;
            }

            throw new InvalidOperationException(I($"Unknown const expression kind '{kind}'."));
        }

        /// <summary>
        /// Reads the value as <see cref="EvaluationResult"/>.
        /// </summary>
        public static EvaluationResult ReadConstValueAsEvaluationResult(BuildXLReader reader)
        {
            var kind = (ConstExpressionKind)reader.ReadByte();
            switch (kind)
            {
                case ConstExpressionKind.Number:
                    return EvaluationResult.Create(reader.ReadInt32Compact());
                case ConstExpressionKind.Boolean:
                    return EvaluationResult.Create(reader.ReadBoolean());
                case ConstExpressionKind.EnumValue:
                    return EvaluationResult.Create(new EnumValue(reader.ReadSymbolAtom(), reader.ReadInt32Compact()));
                case ConstExpressionKind.String:
                    return EvaluationResult.Create(reader.ReadString());
                case ConstExpressionKind.File:
                    return EvaluationResult.Create(reader.ReadFileArtifact());
                case ConstExpressionKind.Path:
                    return EvaluationResult.Create(reader.ReadAbsolutePath());
                case ConstExpressionKind.RelativePath:
                    return EvaluationResult.Create(reader.ReadRelativePath());
                case ConstExpressionKind.UndefinedLiteral:
                case ConstExpressionKind.UndefinedValue:
                    return EvaluationResult.Undefined;
            }

            throw new InvalidOperationException(I($"Unknown const expression kind '{kind}'."));
        }

        /// <nodoc />
        public static void Write(BuildXLWriter writer, IConstantExpression constExpression)
        {
            if (constExpression is Node node)
            {
                writer.Write(true);
                node.Serialize(writer);
            }
            else
            {
                writer.Write(false);
                WriteConstValue(writer, constExpression.Value);
            }
        }

        /// <nodoc />
        public static void WriteConstValue(BuildXLWriter writer, object value)
        {
            switch (value)
            {
                case int intValue:
                    writer.Write((byte)ConstExpressionKind.Number);
                    writer.WriteCompact(intValue);
                    return;
                case bool boolValue:
                    writer.Write((byte)ConstExpressionKind.Boolean);
                    writer.Write(boolValue);
                    return;
                case AbsolutePath absolutePath:
                    writer.Write((byte)ConstExpressionKind.Path);
                    writer.Write(absolutePath);
                    return;
                case RelativePath relativePath:
                    writer.Write((byte)ConstExpressionKind.RelativePath);
                    writer.Write(relativePath);
                    return;
                case FileArtifact fileValue:
                    writer.Write((byte)ConstExpressionKind.File);
                    writer.Write(fileValue);
                    return;
                case EnumValue enumValue:
                    writer.Write((byte)ConstExpressionKind.EnumValue);
                    writer.Write(enumValue.Name);
                    writer.WriteCompact(enumValue.Value);
                    return;
                case string stringValue:
                    writer.Write((byte)ConstExpressionKind.String);
                    writer.Write(stringValue);
                    return;
                case object o when 
                        value == UndefinedLiteral.Instance ||
                        value == UndefinedValue.Instance:
                    writer.Write((byte)ConstExpressionKind.UndefinedLiteral);
                    return;
                default:
                    throw Contract.AssertFailure(I($"Can't serialize an object literal with member value of type '{value.GetType()}'."));
            }
        }
    }
}
