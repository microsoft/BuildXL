// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using Grpc.Core;

namespace BuildXL.Cache.ContentStore.Service.Grpc
{
    internal static class GrpcConstants
    {
        /// <summary>
        /// Name of the field that has a calling machine name passed via grpc layer.
        /// </summary>
        public static string MachineMetadataFieldName { get; } = "MachineName";
    }

    internal static class GrpcMetadataExtensions
    {
        public static bool TryGetCallingMachineName(this Metadata metadata, out string? machineName)
        {
            foreach (var entry in metadata)
            {
                // Grpc sends the metadata in lower case.
                if (string.Equals(entry.Key, GrpcConstants.MachineMetadataFieldName, StringComparison.InvariantCultureIgnoreCase))
                {
                    machineName = entry.Value;
                    return true;
                }
            }

            machineName = null;
            return false;
        }
    }
}
