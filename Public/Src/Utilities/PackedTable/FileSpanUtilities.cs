// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;
using System.Runtime.InteropServices;

namespace BuildXL.Utilities.PackedTable
{
    /// <summary>Generic Span[T] methods for saving and loading spans of unmanaged values.</summary>
    public static class FileSpanUtilities
    {
        /// <summary>
        /// Save this list of unmanaged values to the given filename in the given directory.
        /// </summary>
        public static void SaveToFile<TValue>(string directory, string name, SpannableList<TValue> values)
            where TValue : unmanaged
        {
            string path = Path.Combine(directory, name);

            using (Stream writer = File.OpenWrite(path))
            {
                // kind of a lot of work to write out an int, but whatevs
                int[] length = new int[] { values.Count };
                writer.Write(MemoryMarshal.Cast<int, byte>(new Span<int>(length)));

                writer.Write(MemoryMarshal.Cast<TValue, byte>(values.AsSpan()));
            }
        }

        /// <summary>
        /// Load a list of unmanaged values from the given filename in the given directory.
        /// </summary>
        public static void LoadFromFile<TValue>(string directory, string name, SpannableList<TValue> values)
            where TValue : unmanaged
        {
            values.Clear();

            string path = Path.Combine(directory, name);
            using (Stream reader = File.OpenRead(path))
            {
                int[] lengthBuf = new int[1];
                reader.Read(MemoryMarshal.Cast<int, byte>(new Span<int>(lengthBuf)));
                int length = lengthBuf[0];

                values.Capacity = length;
                values.Fill(length, default);

                Span<TValue> valueSpan = values.AsSpan();
                Span<byte> byteValueSpan = MemoryMarshal.Cast<TValue, byte>(valueSpan);
                reader.Read(byteValueSpan);
            }
        }
    }
}
