// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.ContractsLight;
using System.IO;
using System.Text.Json;

namespace BuildXL.Processes.VmCommandProxy
{
    /// <summary>
    /// Class for serializing/deserializing VmCommandProxy input and output.
    /// </summary>
    public static class VmSerializer
    {
        /// <summary>
        /// Serializes VmCommandProxy input.
        /// </summary>
        public static void SerializeToFile(string file, object vmObject)
        {
            Contract.RequiresNotNullOrWhiteSpace(file);
            Contract.RequiresNotNull(vmObject);

            Directory.CreateDirectory(Path.GetDirectoryName(file));
            var jsonString = JsonSerializer.Serialize(vmObject);
            File.WriteAllText(file, jsonString);
        }

        /// <summary>
        /// Deserializes VmCommandProxy object form file.
        /// </summary>
        public static T DeserializeFromFile<T>(string file)
        {
            Contract.RequiresNotNullOrWhiteSpace(file);
            return JsonSerializer.Deserialize<T>(File.ReadAllText(file));
        }
    }
}
