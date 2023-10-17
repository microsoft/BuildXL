// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace BuildXL.Utilities.PackedTable
{
    /// <summary>Generic Span[T] methods for saving and loading spans of unmanaged values.</summary>
    public static class FileSpanUtilities
    {
        /// <summary>
        /// Represents the ideal slice size in bytes for span save and load operations to prevent overflow.
        /// </summary>
        /// <remarks>
        /// We need to ensure that the below save and load operations do not overflow the Int32.MaxValue.
        /// We aim to save and load data in slices of 500 MiB to avoid this risk.
        /// 
        /// This is "Ideal" because the number is reduced to the nearest multiple of the type size.
        /// 
        /// The default value is recommended, but this is internally configurable for unique cases (eg: testing).
        /// </remarks>
        internal static int IdealSliceSizeBytes = 1 << 29;

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

                Span<TValue> valuesSpan = values.AsSpan();

                int sizeOfTValue = Unsafe.SizeOf<TValue>();
                int entriesPerSlice = Math.Max(IdealSliceSizeBytes / sizeOfTValue, 1);

                for (int i = 0; i < valuesSpan.Length; i += entriesPerSlice)
                {
                    int thisSliceCount = Math.Min(entriesPerSlice, valuesSpan.Length - i);
                    Span<TValue> thisSlice = valuesSpan.Slice(i, thisSliceCount);

                    writer.Write(MemoryMarshal.Cast<TValue, byte>(thisSlice));
                }
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

                int sizeOfTValue = Unsafe.SizeOf<TValue>();
                int entriesPerSlice = Math.Max(IdealSliceSizeBytes / sizeOfTValue, 1);

                for (int i = 0; i < valueSpan.Length; i += entriesPerSlice)
                {
                    int thisSliceSize = Math.Min(entriesPerSlice, valueSpan.Length - i);
                    Span<TValue> thisSlice = valueSpan.Slice(i, thisSliceSize);
                    Span<byte> byteSlice = MemoryMarshal.Cast<TValue, byte>(thisSlice);

                    reader.Read(byteSlice);
                }
            }
        }
    }
}
