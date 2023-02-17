// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Ipc.Interfaces;
using BuildXL.Utilities.Core;
using static BuildXL.Utilities.Core.FormattableStringEx;

namespace BuildXL.Ipc.Common
{
    internal static class Utils
    {
        private static readonly ConcurrentDictionary<int, bool> s_acquiredPorts = new();

        /// <summary>
        /// Returns a currently unused port number.
        /// 
        /// The function ensures that a unique port is returned throughout the program execution 
        /// by using global state to keep track of previously used ports.
        /// </summary>
        /// <returns>
        /// Returns a free port number. If no port is available, returns -1.
        /// </returns>
        internal static int GetUnusedPortNumber(int retryCount = 10)
        {
            retryCount += s_acquiredPorts.Count;
            while (retryCount-- > 0)
            {
                var endPoint = new IPEndPoint(IPAddress.Loopback, 0);
                using (var socket = new Socket(endPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp))
                {
                    socket.Bind(endPoint);
                    int port = ((IPEndPoint)socket.LocalEndPoint).Port;
                    // This port is currently free (i.e., no service is using it); however, we need to check
                    // that we have not returned it previously (otherwise, there might be a port collision).
                    if (s_acquiredPorts.TryAdd(port, true))
                    {
                        return port;
                    }
                }
            }

            // We could not get a free port that is not on a list of ports that we've already acquired.
            // Return an invalid port number, let the caller deal with this.
            return -1;
        }

        /// <summary>
        /// 1. deserializes an <see cref="IIpcOperation"/> from a given <paramref name="stream"/>;
        /// 2. executes the operation (via <paramref name="executor"/>);
        /// 3. serializes and sends back the result via <paramref name="stream"/>.
        ///
        /// If executing the operation (via <paramref name="executor"/>) fails, the <see cref="IIpcResult.ExitCode"/>
        /// of the result is <see cref="IpcResultStatus.ExecutionError"/>
        /// </summary>
        internal static async Task<IIpcResult> ReceiveOperationAndExecuteLocallyAsync(int id, Stream stream, IIpcOperationExecutor executor, CancellationToken token)
        {
            IIpcOperation operation = await IpcOperation.DeserializeAsync(stream, token);
            IIpcResult result = await HandleExceptionsAsync(IpcResultStatus.ExecutionError, () => executor.ExecuteAsync(id, operation));
            if (operation.ShouldWaitForServerAck)
            {
                await IpcResult.SerializeAsync(stream, result, token);
            }

            return result;
        }

        /// <summary>
        /// 1. serializes an <see cref="IIpcOperation"/> over via <paramref name="stream"/>;
        /// 2. if the operation is synchronous, reads an <see cref="IIpcResult"/> from <paramref name="stream"/>.
        /// </summary>
        internal static async Task<IIpcResult> SendOperationAndExecuteRemotelyAsync(IIpcOperation operation, Stream stream, CancellationToken token)
        {
            await IpcOperation.SerializeAsync(stream, operation, token);
            return operation.ShouldWaitForServerAck
                ? await IpcResult.DeserializeAsync(stream, token)
                : IpcResult.Success();
        }

        /// <summary>
        /// Implements a retrial logic for establishing a connection.
        /// </summary>
        /// <typeparam name="TConn">Type of the connection.</typeparam>
        /// <param name="maxRetry">Total number of attempts to establish a connection is 1 more than this value.</param>
        /// <param name="waitTimeBetweenAttempts">Time to wait between consecutive attempts.</param>
        /// <param name="connectionFactory">A user-provided function that actually creates and returns a connection.</param>
        /// <returns>
        /// A tuple containing a connection at position 1 (if a connection was established), and
        /// an error message at possition 2 from any exceptions caught while trying to reconnect.
        /// </returns>
        internal static async Task<Possible<TConn>> ConnectAsync<TConn>(int maxRetry, TimeSpan waitTimeBetweenAttempts, Func<Task<TConn>> connectionFactory)
        {
            maxRetry = maxRetry < 0 ? 0 : maxRetry;

            Exception lastException = null;
            TimeSpan currentWait = waitTimeBetweenAttempts;
            int numAttempts = 0;
            while (numAttempts <= maxRetry)
            {
                if (numAttempts > 0)
                {
                    await Task.Delay(currentWait);
                    currentWait = currentWait.Add(waitTimeBetweenAttempts);
                }

                numAttempts++;

                try
                {
                    return await connectionFactory();
                }
                catch (Exception e)
                {
                    lastException = e;
                }
            }

            return new Failure<string>(I($"Failed to connect in {numAttempts} attempts.  Last exception: {lastException}"));
        }

        /// <summary>
        /// Executes <paramref name="clientHandler"/>; if any exception happens,
        /// returns an <see cref="IIpcResult"/> with <see cref="IIpcResult.ExitCode"/>
        /// set to <paramref name="errorExitCode"/> and <see cref="IIpcResult.Payload"/>
        /// to the "ToString" value of the caught exception.
        /// </summary>
        internal static IIpcResult HandleExceptions(IpcResultStatus errorExitCode, Func<IIpcResult> clientHandler)
        {
            try
            {
                return clientHandler();
            }
            catch (Exception e)
            {
                return new IpcResult(errorExitCode, e.ToStringDemystified());
            }
        }

        /// <summary>
        /// Async version of <see cref="HandleExceptions"/>.
        /// </summary>
        internal static async Task<IIpcResult> HandleExceptionsAsync(IpcResultStatus errorExitCode, Func<Task<IIpcResult>> clientHandler)
        {
            try
            {
                return await clientHandler();
            }
            catch (Exception e)
            {
                return new IpcResult(errorExitCode, e.ToStringDemystified());
            }
        }

        private static readonly Encoding SerializationStringEncoding = Encoding.UTF8;

        internal static async Task WriteStringAsync(Stream stream, string str, CancellationToken token)
        {
            byte[] bytes = SerializationStringEncoding.GetBytes(str);
            await WriteIntAsync(stream, bytes.Length, token);
            await WriteByteArrayAsync(stream, bytes, token);
        }

        internal static async Task<string> ReadStringAsync(Stream reader, CancellationToken token)
        {
            var length = await ReadIntAsync(reader, token);
            var bytes = await ReadByteArrayAsync(reader, length, token);
            return SerializationStringEncoding.GetString(bytes);
        }

        internal static Task WriteIntAsync(Stream writer, int i, CancellationToken token)
        {
            byte[] bytes = BitConverter.GetBytes(i);
            return WriteByteArrayAsync(writer, bytes, token);
        }

        internal static Task WriteLongAsync(Stream writer, long i, CancellationToken token)
        {
            byte[] bytes = BitConverter.GetBytes(i);
            return WriteByteArrayAsync(writer, bytes, token);
        }

        internal static async Task<int> ReadIntAsync(Stream reader, CancellationToken token)
        {
            byte[] bytes = await ReadByteArrayAsync(reader, 4, token);
            return BitConverter.ToInt32(bytes, 0);
        }

        internal static async Task<long> ReadLongAsync(Stream reader, CancellationToken token)
        {
            byte[] bytes = await ReadByteArrayAsync(reader, 8, token);
            return BitConverter.ToInt64(bytes, 0);
        }

        internal static Task WriteBooleanAsync(Stream writer, bool b, CancellationToken token)
        {
            return WriteByteAsync(writer, b ? (byte)1 : (byte)0, token);
        }

        internal static async Task<bool> ReadBooleanAsync(Stream stream, CancellationToken token)
        {
            byte b = await ReadByteAsync(stream, token);
            CheckSerializationFormat(b == 0 || b == 1, "{0} failed; expected '0' or '1', got '{1}'", nameof(ReadBooleanAsync), b);
            return b == 1 ? true : false;
        }

        internal static Task WriteByteAsync(Stream writer, byte b, CancellationToken token)
        {
            return WriteByteArrayAsync(writer, new[] { b }, token);
        }

        internal static async Task<byte> ReadByteAsync(Stream reader, CancellationToken token)
        {
            byte[] buff = await ReadByteArrayAsync(reader, 1, token);
            return buff[0];
        }

        internal static Task WriteByteArrayAsync(Stream stream, byte[] buff, CancellationToken token)
        {
            return stream.WriteAsync(buff, 0, buff.Length, token);
        }

        internal static async Task<byte[]> ReadByteArrayAsync(Stream stream, int length, CancellationToken token)
        {
            var buff = new byte[length];
            int totalNumRead = 0;
            int currentPosition = 0;
            while (totalNumRead < length)
            {
                int numRead = await stream.ReadAsync(buff, currentPosition, length - totalNumRead, token);
                CheckSerializationFormat(
                    numRead > 0,
                    "Stream ended before {0} characters could be read. Total number of characters read: {1}",
                    length, totalNumRead);

                totalNumRead += numRead;
                currentPosition += numRead;
            }

            return buff;
        }

        internal static void CheckSerializationFormat(bool condition, string format, params object[] args)
        {
            if (!condition)
            {
                throw new IpcException(IpcException.IpcExceptionKind.Serialization, format, args);
            }
        }
    }
}
