// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

const protoVersion = "3.14.0";
const grpcVersion = "2.32.0";

export const pkgs = [
    // grpc
    { id: "Grpc.Net.Client", version: grpcVersion },
    { id: "Grpc.Net.Common", version: grpcVersion },
    { id: "Grpc.Core", version: grpcVersion, dependentPackageIdsToSkip: [ "System.Memory" ] },
    { id: "Grpc.Core.Api", version: grpcVersion, dependentPackageIdsToSkip: [ "System.Memory" ] },
    { id: "Grpc.Tools", version: grpcVersion },

    // protobuf
    { id: "Google.Protobuf", version: protoVersion, dependentPackageIdsToSkip: [ "System.Memory" ] },
    { id: "Google.Protobuf.Tools", version: protoVersion },

    // protobuf-net
    { id: "protobuf-net.Core", version: "3.0.101", dependentPackageIdsToSkip: [ "System.Memory" ] },
    { id: "protobuf-net", version: "3.0.101", dependentPackageIdsToSkip: [ "System.Memory" ] },
    { id: "protobuf-net.Grpc", version: "1.0.152", dependentPackageIdsToSkip: [ "System.Memory", "System.Threading.Channels" ] },
    { id: "protobuf-net.Grpc.Native", version: "1.0.152", dependentPackageIdsToSkip: [ "System.Memory", "System.Threading.Channels" ] },

    { id: "System.ServiceModel.Http", version: "4.7.0" },
    { id: "System.ServiceModel.Primitives", version: "4.7.0" },
    { id: "System.Private.ServiceModel", version: "4.7.0" },

];
