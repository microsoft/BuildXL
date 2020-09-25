// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

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
                if (entry.Key == GrpcConstants.MachineMetadataFieldName)
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
