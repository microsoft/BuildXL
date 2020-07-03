// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;
using System.Resources;

namespace ResGen.Lite
{
    /// <summary>
    /// Writes .resources file
    /// </summary>
    public static class ResourcesWriter
    {
        /// <summary>
        /// Writes a resourcesData structure into .resources file format
        /// </summary>
        public static void Write(string filePath, ResourceData data)
        {
            try
            {
                using (var writer = new ResourceWriter(filePath))
                {
                    foreach (var stringValue in data.StringValues)
                    {
                        writer.AddResource(stringValue.Name, stringValue.Value);
                    }
                }
            }
            catch (Exception e) when
                (e is IOException || e is UnauthorizedAccessException)
            {
                throw new ResGenLiteException(
                    $"{filePath}: Error: Error writing resources file: {e.Message}");
            }
        }
    }
}
