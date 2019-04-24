// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
#if !DISABLE_FEATURE_BOND_RPC

using System;
using System.Diagnostics;
using System.Diagnostics.ContractsLight;
using System.IO;
using BondTransport;
using BuildXL.Utilities.Configuration;
using Microsoft.Bond;

namespace BuildXL.Engine.Distribution.InternalBond
{
    /// <summary>
    /// Intercepts outgoing bond requests to allow injecting failures.
    /// </summary>
    internal sealed class ResiliencyTestBondTransportClient : IBondTransportClient
    {
        private readonly IBondTransportClient m_client;
        private readonly FailureScenario m_scenario;

        private ResiliencyTestBondTransportClient(IBondTransportClient client, FailureScenario scenario)
        {
            m_client = client;
            m_scenario = scenario;
        }

        /// <summary>
        /// Wraps the given bond transport client in to allow injecting failures using different patterns.
        /// </summary>
        public static IBondTransportClient TryWrapClientForTest(DistributedBuildRoles role, IBondTransportClient client)
        {
            Contract.Requires(client != null);
            Contract.Requires(role != DistributedBuildRoles.None);
            Contract.Ensures(Contract.Result<IBondTransportClient>() != null);

            var scenarioRole = Environment.GetEnvironmentVariable("BuildXL_Test_Resiliency_Role");
            var scenarioName = Environment.GetEnvironmentVariable("BuildXL_Test_Resiliency_Scenario") ?? string.Empty;

            if (!string.IsNullOrEmpty(scenarioRole) && !string.Equals(scenarioRole, role.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                return client;
            }

            scenarioName = scenarioName.ToUpperInvariant();
            FailureScenario scenario = null;
            switch (scenarioName)
            {
                case "RANDOM":
                    scenario = new RandomFailuresScenario();
                    break;
                case "RANDOMCHECKSUM":
                    scenario = new RandomChecksumFailuresScenario();
                    break;
                case "SHORT":
                    scenario = new ShortDurationFailureScenario();
                    break;
                case "LONG":
                    scenario = new LongDurationFailureScenario();
                    break;
                case "IRRECOVERABLE":
                    scenario = new IrrecoverableFailureScenario();
                    break;
            }

            return scenario == null ? client : new ResiliencyTestBondTransportClient(client, scenario);
        }

        private abstract class FailureScenario
        {
            public abstract void FailIfNecessary(string methodName);

            public virtual void HandleMessage<T>(Message<T> message)
                where T : IBondSerializable, new()
            {
            }

            protected static void Fail()
            {
                throw new IOException("Injected failure");
            }
        }

        /// <summary>
        /// Fail randomly 25% of the time
        /// </summary>
        private sealed class RandomChecksumFailuresScenario : FailureScenario
        {
            private readonly Random m_random = new Random();

            public override void HandleMessage<T>(Message<T> message)
            {
                var randomNumber = m_random.Next(0, 3);

                // Fail 25 % of the time
                if (randomNumber == 1)
                {
                    // Modify the checksum
                    message.SetChecksum(~message.GetChecksum());
                }
            }

            public override void FailIfNecessary(string methodName)
            {
            }
        }

        /// <summary>
        /// Fail randomly 25% of the time
        /// </summary>
        private sealed class RandomFailuresScenario : FailureScenario
        {
            private readonly Random m_random = new Random();

            public override void FailIfNecessary(string methodName)
            {
                // Fail 25 % of the time
                if (m_random.Next(0, 3) == 1)
                {
                    Fail();
                }
            }
        }

        /// <summary>
        /// Fail all calls over a short period of time (less than timeout interval)
        /// </summary>
        private sealed class ShortDurationFailureScenario : FailureScenario
        {
            private readonly Stopwatch m_stopwatch = Stopwatch.StartNew();
            private static readonly TimeSpan s_initialSuccessIntervalEnd = TimeSpan.FromSeconds(15);
            private readonly TimeSpan m_failureIntervalEnd = s_initialSuccessIntervalEnd + TimeSpan.FromSeconds(30);

            public override void FailIfNecessary(string methodName)
            {
                if (m_stopwatch.Elapsed < s_initialSuccessIntervalEnd)
                {
                    return;
                }

                if (m_stopwatch.Elapsed < m_failureIntervalEnd)
                {
                    Fail();
                }
            }
        }

        /// <summary>
        /// Fail all calls over a long period of time (greater than timeout interval), but recovers.
        /// </summary>
        private sealed class LongDurationFailureScenario : FailureScenario
        {
            private readonly Stopwatch m_stopwatch = Stopwatch.StartNew();
            private static readonly TimeSpan s_initialSuccessIntervalEnd = TimeSpan.FromSeconds(15);
            private readonly TimeSpan m_failureIntervalEnd;

            public LongDurationFailureScenario()
            {
                // Shorten the timeout so that failure interval is long enough to cause a timeout
                EngineEnvironmentSettings.DistributionInactiveTimeout.Value = TimeSpan.FromSeconds(30);
                m_failureIntervalEnd = s_initialSuccessIntervalEnd + EngineEnvironmentSettings.DistributionInactiveTimeout + TimeSpan.FromSeconds(5);
            }

            public override void FailIfNecessary(string methodName)
            {
                if (m_stopwatch.Elapsed < s_initialSuccessIntervalEnd)
                {
                    return;
                }

                if (m_stopwatch.Elapsed < m_failureIntervalEnd)
                {
                    Fail();
                }
            }
        }

        /// <summary>
        /// Fail all calls over a after a certain point in time and does not recover.
        /// </summary>
        private sealed class IrrecoverableFailureScenario : FailureScenario
        {
            private readonly Stopwatch m_stopwatch = Stopwatch.StartNew();
            private static readonly TimeSpan s_initialSuccessIntervalEnd = TimeSpan.FromSeconds(30);

            public override void FailIfNecessary(string methodName)
            {
                if (m_stopwatch.Elapsed < s_initialSuccessIntervalEnd)
                {
                    return;
                }

                Fail();
            }
        }

        #region IBondTransportClient Members

        public IAsyncResult BeginRequest<T>(
            string serviceName,
            string methodName,
            Message<T> input,
            IBufferAllocator allocator,
            AsyncCallback userCallback,
            object stateObject)
            where T : IBondSerializable, new()
        {
            m_scenario.FailIfNecessary(methodName);
            m_scenario.HandleMessage(input);
            return m_client.BeginRequest(serviceName, methodName, input, allocator, userCallback, stateObject);
        }

        public IAsyncResult BeginRequest<T>(string serviceName, string methodName, Message<T> input, AsyncCallback userCallback, object stateObject)
            where T : IBondSerializable, new()
        {
            m_scenario.FailIfNecessary(methodName);
            m_scenario.HandleMessage(input);
            return m_client.BeginRequest(serviceName, methodName, input, userCallback, stateObject);
        }

        public void CancelRequest(string serviceName, string methodName, IAsyncResult asyncResult)
        {
            m_client.CancelRequest(serviceName, methodName, asyncResult);
        }

        public void EnableCompression(bool enable)
        {
            m_client.EnableCompression(enable);
        }

        public Message<T> EndRequest<T>(string serviceName, string methodName, IAsyncResult asyncResult)
            where T : IBondSerializable, new()
        {
            m_scenario.FailIfNecessary(methodName);
            return m_client.EndRequest<T>(serviceName, methodName, asyncResult);
        }

        public Bonded<T> GetResponse<T>(string serviceName, string methodName, IAsyncResult ar)
            where T : IBondSerializable, new()
        {
            throw Contract.AssertFailure("Deprecated method");
        }

        public IAsyncResult SendEmptyRequest(string serviceName, string methodName, AsyncCallback callback, object context)
        {
            throw Contract.AssertFailure("Deprecated method");
        }

        public IAsyncResult SendRequest<T>(string serviceName, string methodName, T requestParam, AsyncCallback callback, object context)
            where T : IBondSerializable, new()
        {
            throw Contract.AssertFailure("Deprecated method");
        }

        #endregion
    }
}
#endif
