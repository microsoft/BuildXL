// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

// Ideally these are same version, but ProtoBuf has a newer patch version.
const protoVersion = "3.19.4";
const protoToolsVersion = "3.19.4";

const grpcNetVersion = "2.47.0";
const grpcCoreVersion = "2.47.0";

export const pkgs = [
    // grpc
    { id: "Grpc.Net.Client", version: grpcNetVersion}, 
    { id: "Grpc.Net.Client.Web", version: grpcNetVersion},  
    { id: "Grpc.Net.ClientFactory", version: grpcNetVersion},  
    { id: "Grpc.Net.Common", version: grpcNetVersion},
    { id: "Grpc.AspNetCore.Server.ClientFactory", version: grpcNetVersion},
    { id: "Grpc.AspNetCore.Server", version: grpcNetVersion},
    { id: "Grpc.AspNetCore", version: grpcNetVersion},  

    { id: "Grpc.Core", version: "2.46.3", dependentPackageIdsToSkip: [ "System.Memory" ] },
    { id: "Grpc.Core.Api", version: grpcCoreVersion, dependentPackageIdsToSkip: [ "System.Memory" ] },
    { id: "Grpc.Tools", version: grpcCoreVersion },

    // protobuf
    { id: "Google.Protobuf", version: protoVersion, dependentPackageIdsToSkip: [ "System.Memory" ] },
    { id: "Google.Protobuf.Tools", version: protoToolsVersion },

    // protobuf-net
    { id: "protobuf-net.Core", version: "3.0.101",
        dependentPackageIdsToSkip: [ "System.Memory" ] },
    { id: "protobuf-net", version: "3.0.101",
        dependentPackageIdsToSkip: [ "System.Memory" ] },
    { id: "protobuf-net.Grpc", version: "1.0.152",
        dependentPackageIdsToSkip: [ "System.Memory", "System.Threading.Channels", "Grpc.Core.Api" ] },
    { id: "protobuf-net.Grpc.Native", version: "1.0.152",
        dependentPackageIdsToSkip: [ "System.Memory", "System.Threading.Channels", "Grpc.Core" ] },
    { id: "protobuf-net.Grpc.AspNetCore", version: "1.0.152",
        dependentPackageIdsToSkip: [ "System.Memory", "System.Threading.Channels", "Grpc.Core" ] },

    { id: "System.ServiceModel.Http", version: "4.7.0" },
    { id: "System.ServiceModel.Primitives", version: "4.7.0" },
    { id: "System.Private.ServiceModel", version: "4.7.0" },

    { id: "Microsoft.Extensions.Hosting.Abstractions", version: "3.0.3" },
    { id: "Microsoft.Extensions.FileProviders.Abstractions", version: "3.0.3" },
];
