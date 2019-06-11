// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

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
            Contract.Requires(!string.IsNullOrWhiteSpace(file));
            Contract.Requires(vmObject != null);

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
            Contract.Requires(!string.IsNullOrWhiteSpace(file));
            return JsonConvert.DeserializeObject<T>(File.ReadAllText(file));
        }
    }
}
