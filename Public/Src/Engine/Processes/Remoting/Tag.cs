// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using BuildXL.Utilities;

#nullable enable

namespace BuildXL.Processes.Remoting
{
    /// <summary>
    /// Tagging for backward/forward-compat serialization.
    /// </summary>
    public static class Tag
    {
        /// <nodoc/>
        private const byte End = 0;

        /// <summary>
        /// Checks if tag signifies ends of serialization.
        /// </summary>
        /// <param name="tag"></param>
        /// <returns></returns>
        public static bool ReachedEnd(byte tag) => tag == End;

        /// <summary>
        /// Reads tag.
        /// </summary>
        public static byte ReadTag(BuildXLReader reader) => reader.ReadByte();

        /// <summary>
        /// Reads type.
        /// </summary>
        public static byte ReadType(BuildXLReader reader) => reader.ReadByte();

        /// <summary>
        /// Reads number.
        /// </summary>
        public static int ReadNumber(BuildXLReader reader) => reader.ReadInt32();

        /// <summary>
        /// Reads string.
        /// </summary>
        public static string? ReadString(BuildXLReader reader) => reader.ReadNullableString();

        /// <summary>
        /// Writes string.
        /// </summary>
        public static void WriteString(BuildXLWriter writer, string? value)
        {
            writer.Write(TagType.String);
            writer.WriteNullableString(value);
        }

        /// <summary>
        /// Writes a tagged string.
        /// </summary>
        public static void WriteTaggedString(BuildXLWriter writer, byte tag, string? value)
        {
            writer.Write(tag);
            WriteString(writer, value);
        }

        /// <summary>
        /// Writes a number.
        /// </summary>
        public static void WriteNumber(BuildXLWriter writer, int value)
        {
            writer.Write(TagType.Number);
            writer.Write(value);
        }

        /// <summary>
        /// Writes a tagged number.
        /// </summary>
        public static void WriteTaggedNumber(BuildXLWriter writer, byte tag, int value)
        {
            writer.Write(tag);
            WriteNumber(writer, value);
        }

        /// <summary>
        /// Writes repeated tagged items.
        /// </summary>
        public static void WriteRepeatedTaggedItems<T>(
            BuildXLWriter writer,
            byte tag,
            IEnumerable<T> values,
            Action<BuildXLWriter, T> writeItem)
        {
            foreach (var item in values)
            {
                writer.Write(tag);
                writeItem(writer, item);
            }
        }

        /// <summary>
        /// Writes map.
        /// </summary>
        public static void WriteTaggedMap<TKey, TValue>(
            BuildXLWriter writer,
            byte tag,
            IEnumerable<KeyValuePair<TKey, TValue>> values,
            Action<BuildXLWriter, TKey> writeKey,
            Action<BuildXLWriter, TValue> writeValue)
        {
            foreach (var item in values)
            {
                writer.Write(tag);
                writer.Write(TagType.Map);
                writeKey(writer, item.Key);
                writeValue(writer, item.Value);
            }
        }

        /// <summary>
        /// Writes end marker.
        /// </summary>
        public static void EndWrite(BuildXLWriter writer) => writer.Write(End);
    }

    /// <summary>
    /// Tag type for serialized entries.
    /// </summary>
    public static class TagType
    {
        /// <summary>
        /// Number type.
        /// </summary>
        public const byte Number = 0;

        /// <summary>
        /// String type.
        /// </summary>
        public const byte String = 1;

        /// <summary>
        /// Map type.
        /// </summary>
        public const byte Map = 2;
    }
}
