// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#if NET6_0_OR_GREATER

using System;
using BuildXL.Ipc.Common;
using BuildXL.Ipc.Interfaces;
using static BuildXL.Ipc.Grpc.IpcServer;

namespace BuildXL.Ipc.GrpcBasedIpc
{
    /// <summary>
    /// Serialization from and to gRPC into <see cref="Ipc.Interfaces"/> implementations
    /// </summary>
    internal static class SerializationExtensions
    {
        /// <nodoc />
        public static IpcResult FromGrpc(this Grpc.IpcResult result) => new(result.ExitCode.FromGrpc(), result.Payload);
        
        /// <nodoc />
        public static Grpc.IpcResult AsGrpc(this IIpcResult result) => new() { ExitCode = result.ExitCode.AsGrpc(), Payload = result.Payload };

        /// <nodoc />
        public static IpcOperation FromGrpc(this Grpc.IpcOperation op) => new(op.Payload, op.IsSynchronous);

        /// <nodoc />
        public static Grpc.IpcOperation AsGrpc(this IIpcOperation op) => new() { Payload = op.Payload, IsSynchronous = op.ShouldWaitForServerAck };

        /// <nodoc />
        public static Interfaces.IpcResultStatus FromGrpc(this Grpc.IpcResultStatus grpcResultStatus) => grpcResultStatus switch
        {
            Grpc.IpcResultStatus.Success => Interfaces.IpcResultStatus.Success,
            Grpc.IpcResultStatus.GenericError => Interfaces.IpcResultStatus.GenericError,
            Grpc.IpcResultStatus.ConnectionError => Interfaces.IpcResultStatus.ConnectionError,
            Grpc.IpcResultStatus.TransmissionError => Interfaces.IpcResultStatus.TransmissionError,
            Grpc.IpcResultStatus.ExecutionError => Interfaces.IpcResultStatus.ExecutionError,
            Grpc.IpcResultStatus.InvalidInput => Interfaces.IpcResultStatus.InvalidInput,
            _ => throw new ArgumentOutOfRangeException($"Unkown enum value for Grpc.IpcResultStatus: {grpcResultStatus}")
        };

        /// <nodoc />
        public static Grpc.IpcResultStatus AsGrpc(this Interfaces.IpcResultStatus ipcResultStatus) => ipcResultStatus switch
        {
            Interfaces.IpcResultStatus.Success => Grpc.IpcResultStatus.Success,
            Interfaces.IpcResultStatus.GenericError => Grpc.IpcResultStatus.GenericError,
            Interfaces.IpcResultStatus.ConnectionError => Grpc.IpcResultStatus.ConnectionError,
            Interfaces.IpcResultStatus.TransmissionError => Grpc.IpcResultStatus.TransmissionError,
            Interfaces.IpcResultStatus.ExecutionError => Grpc.IpcResultStatus.ExecutionError,
            Interfaces.IpcResultStatus.InvalidInput => Grpc.IpcResultStatus.InvalidInput,
            _ => throw new ArgumentOutOfRangeException($"Unkown enum value for IpcResultStatus: {ipcResultStatus}")
        };
    }
}

#endif