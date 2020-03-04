// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.ContractsLight;
using System.IO;
using Newtonsoft.Json;

namespace BuildXL.Utilities.VmCommandProxy
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

            var jsonSerializer = JsonSerializer.Create(new JsonSerializerSettings
            {
                NullValueHandling = NullValueHandling.Include
            });

            Directory.CreateDirectory(Path.GetDirectoryName(file));

            using (var streamWriter = new StreamWriter(file))
            using (var jsonTextWriter = new JsonTextWriter(streamWriter))
            {
                jsonSerializer.Serialize(jsonTextWriter, vmObject);
            }
        }

        /// <summary>
        /// Deserializes VmCommandProxy object form file.
        /// </summary>
        public static T DeserializeFromFile<T>(string file)
        {
            Contract.RequiresNotNullOrWhiteSpace(file);
            return JsonConvert.DeserializeObject<T>(File.ReadAllText(file));
        }
    }
}
