// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading.Tasks;
using BuildXL.AdoBuildRunner.Vsts;

namespace BuildXL.AdoBuildRunner.Build
{
    /// <summary>
    /// An executor to diagnose connectivity between hosted agents. 
    /// This executor will perform a ping and try to establish TCP connections
    /// from the orchestrator to the workers, both to their IPV4 and IPV6 addresses
    /// </summary>
    public class PingExecutor : BuildExecutorBase, IBuildExecutor
    {
        private const int ListeningPort = 45678;
        private TcpListener m_server = null;

        private IApi m_vstsApi;

        /// <nodoc />
        public PingExecutor(ILogger logger, IApi vstsApi) : base(logger) { m_vstsApi = vstsApi; }

        /// <inheritdoc />
        public void PrepareBuildEnvironment(BuildContext buildContext)
        {
            if (buildContext == null)
            {
                throw new ArgumentNullException(nameof(buildContext));
            }
        }

        /// <inherit />
        public int ExecuteSingleMachineBuild(BuildContext buildContext, string[] buildArguments)
        {
            throw new InvalidOperationException("Single machine ping test");
        }

        /// <inherit />
        public int ExecuteDistributedBuildAsOrchestrator(BuildContext buildContext, string[] buildArguments, int _)
        {
            // The ping executor does need the informations of all the workers
            m_vstsApi.WaitForOtherWorkersToBeReady().GetAwaiter().GetResult();
            var machines = m_vstsApi.GetWorkerAddressInformationAsync().GetAwaiter().GetResult().ToList();

            Logger.Info($@"Launching ping test as orchestrator");

            var usingV6 = buildArguments.Any(opt => opt == "ipv6");
            var ip = BuildManager.GetAgentIPAddress(usingV6);

            var tasks = new Task<bool>[machines.Count];
            for (int i = 0; i < tasks.Length; i++)
            {
                var workerIp = machines[i][usingV6 ? Constants.MachineIpV6Address : Constants.MachineIpV4Address];
                tasks[i] = SendMessageToWorker(ip, machines[0][Constants.MachineHostName], workerIp, usingV6);
            }

            Task.WhenAll(tasks).GetAwaiter().GetResult();

            if (!tasks.All(t => t.GetAwaiter().GetResult()))
            {
                // Some task failed
                return 1;
            }

            return 0;
        }

        /// <inherit />
        public int ExecuteDistributedBuildAsWorker(BuildContext buildContext, string[] buildArguments, IDictionary<string, string> orchestratorInfo)
        {
            Logger.Info($@"Launching ping & connectivity test as worker");
            WaitMessageFromOrchestrator().GetAwaiter().GetResult();
            return 0;
        }

        private async Task WaitMessageFromOrchestrator()
        {
            try
            {
                // Buffer for reading data
                var bytes = new byte[256];
                string data = null;

                // Enter the listening loop.
                Logger.Info("Waiting for a connection... ");
                TcpClient client = await m_server.AcceptTcpClientAsync();
                Logger.Info("Connected!");

                data = null;

                // Get a stream object for reading and writing
                NetworkStream stream = client.GetStream();

                int i;

                // Loop to receive all the data sent by the client.
                while ((i = await stream.ReadAsync(bytes, 0, bytes.Length)) != 0)
                {
                    // Translate data bytes to a ASCII string.
                    data = System.Text.Encoding.ASCII.GetString(bytes, 0, i);
                    Logger.Info($"Received: {data}");

                    // Process the data sent by the client.
                    data = data.ToUpper();

                    byte[] msg = System.Text.Encoding.ASCII.GetBytes(data);

                    // Send back a response.
                    await stream.WriteAsync(msg, 0, msg.Length);
                    Logger.Info($"Sent: {data}");
                }

                // Shutdown and end connection
                client.Close();
            }
            catch (SocketException e)
            {
                Logger.Info($"SocketException: {e}");
                throw;
            }
            finally
            {
                // Stop listening for new clients.
                m_server.Stop();
            }
        }

        private Task<bool> SendMessageToWorker(string myIp, string workerHostname, string workerIp, bool ipv6)
        {
            // Ping
            try
            {
                for (var i = 0; i < 2; i++)
                {
                    using var pinger = new Ping();
                    PingReply reply = pinger.Send(workerIp, timeout: i*1000);
                    Logger.Info($"Ping to IP {workerIp} response: {reply.Status} - {reply.RoundtripTime}ms");

                    reply = pinger.Send(workerHostname, timeout: i*1000);
                    Logger.Info($"Ping to hostname {workerHostname} response: {reply.Status} - {reply.RoundtripTime}ms");
                }
            }
            catch (PingException e)
            {
                Logger.Info(e.Message);
            }

            // TCP message
            Logger.Info($"Sending a message from {myIp} to {workerIp}:{ListeningPort}");
            return SendTcpMessageToWorker(workerIp, ListeningPort, ipv6 ? AddressFamily.InterNetworkV6 : AddressFamily.InterNetwork);
        }

        private async Task<bool> SendTcpMessageToWorker(string workerIp, int port, AddressFamily addressFamily)
        {
            int attempts = 0;
            TcpClient client = null;

            while (++attempts <= 3)
            {
                Logger.Info($"[-> {workerIp}] Attempt {attempts} of 3");
                try
                {
                    client = new TcpClient(addressFamily);
                    await client.ConnectAsync(IPAddress.Parse(workerIp), port);
                }
                catch (Exception e)
                {
                    Logger.Info($"[-> {workerIp}] Connect exception: {e}.");
                }

                if (client.Connected)
                {
                    break;
                }

                Logger.Info($"[-> {workerIp}] Waiting for 20s before retrying...");
                await Task.Delay(20_000);
            }

            if (!client.Connected)
            {
                Logger.Error($"[-> {workerIp}] Couldn't connect after 3 attempts");
                return false;
            }

            // Translate the passed message into ASCII and store it as a Byte array.
            byte[] data = System.Text.Encoding.ASCII.GetBytes("Hello, world");

            // Get a client stream for reading and writing.
            NetworkStream stream = client.GetStream();

            // Send the message to the connected TcpServer.
            await stream.WriteAsync(data, 0, data.Length);
            await stream.FlushAsync();

            Logger.Info($"[-> {workerIp}] Sent: Hello, world");

            // Receive the TcpServer.response.

            // Buffer to store the response bytes.
            data = new byte[256];

            // String to store the response ASCII representation.
            string responseData = string.Empty;

            // Read the first batch of the TcpServer response bytes.
            int bytes = await stream.ReadAsync(data, 0, data.Length);
            responseData = System.Text.Encoding.ASCII.GetString(data, 0, bytes);
            Logger.Info($"[-> {workerIp}] Received: {responseData}");

            // Close everything.
            stream.Close();
            client.Close();
            return true;
        }

        /// <inheritdoc />
        public void InitializeAsWorker(BuildContext buildContext, string[] buildArguments)
        {
            // Start listening for client requests.
            m_server = TcpListener.Create(ListeningPort);
            m_server.Start();
            Logger.Info($"Started server on port {ListeningPort}");
        }
    }
}
