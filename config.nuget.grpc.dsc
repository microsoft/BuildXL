// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

const protoVersion = "3.11.2";
const grpcVersion = "2.26.0";

export const pkgs = [
    // grpc
    { id: "Grpc.AspNetCore", version: grpcVersion},
    { id: "Grpc.AspNetCore.Server", version: grpcVersion },
    { id: "Grpc.AspNetCore.Server.ClientFactory", version: grpcVersion },
    { id: "Grpc.Net.ClientFactory", version: grpcVersion },
    { id: "Grpc.Net.Client", version: grpcVersion },
    { id: "Grpc.Net.Common", version: grpcVersion },
    { id: "Grpc.Core", version: grpcVersion, dependentPackageIdsToSkip: [ "System.Memory" ] },
    { id: "Grpc.Core.Api", version: grpcVersion, dependentPackageIdsToSkip: [ "System.Memory" ] },
    { id: "Grpc.Tools", version: grpcVersion },

    // protobuf
    { id: "Google.Protobuf", version: protoVersion, dependentPackageIdsToSkip: [ "System.Memory" ] },
    { id: "Google.Protobuf.Tools", version: protoVersion },
];
