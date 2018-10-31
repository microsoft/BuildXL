// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Net.NetworkInformation;

namespace BuildXL.Utilities
{
    /// <summary>
    /// Provides throughput statistics for network interface
    /// </summary>
    public sealed class NetworkCardMonitor
    {
        private readonly NetworkInterface m_networkInterface;

        private long m_startSentBytes;
        private long m_startReceivedBytes;

        /// <summary>
        /// Initializes a new instance of the <see cref="NetworkCardMonitor"/> class.
        /// </summary>
        /// <param name="networkInterface">Network Interface</param>
        public NetworkCardMonitor(NetworkInterface networkInterface)
        {
            m_networkInterface = networkInterface;
        }

        /// <summary>
        /// Gets the name of the network interface.
        /// </summary>
        public string Name => m_networkInterface.Name;

        /// <summary>
        /// Gets the speed of the interface.
        /// </summary>
        public long Bandwidth => m_networkInterface.Speed;

        /// <summary>
        /// Gets the total bytes.
        /// </summary>
        public long TotalBytes { get; private set; }

        /// <summary>
        /// Gets the total sent bytes.
        /// </summary>
        public long TotalSentBytes { get; private set; }

        /// <summary>
        /// Gets the total received bytes.
        /// </summary>
        public long TotalReceivedBytes { get; private set; }

        /// <summary>
        /// Gets a value indicating whether the interface is of Ethernet type.
        /// </summary>
        public bool IsEthernet => m_networkInterface.NetworkInterfaceType == NetworkInterfaceType.Ethernet;

        /// <summary>
        /// Gets a value indicating whether the interface is of wireless type.
        /// </summary>
        public bool IsWireless => m_networkInterface.NetworkInterfaceType == NetworkInterfaceType.Wireless80211;

        /// <summary>
        /// Gets a value indicating whether the interface is active.
        /// </summary>
        public bool IsOperational => m_networkInterface.OperationalStatus == OperationalStatus.Up;

        /// <summary>
        /// Starts the measurement.
        /// </summary>
        public void StartMeasurement()
        {
            try
            {
                IPInterfaceStatistics stats = m_networkInterface.GetIPStatistics();

                m_startSentBytes = stats.BytesSent;
                m_startReceivedBytes = stats.BytesReceived;
            }
            catch (NetworkInformationException)
            {
                m_startSentBytes = 0;
                m_startReceivedBytes = 0;
            }

            TotalBytes = 0;
            TotalSentBytes = 0;
            TotalReceivedBytes = 0;
        }

        /// <summary>
        /// Stops the measurement.
        /// </summary>
        public void StopMeasurement()
        {
            try
            {
                IPInterfaceStatistics stats = m_networkInterface.GetIPStatistics();

                // Account for the possibility of the counter wrapping
                var temp = stats.BytesSent;
                if (temp < m_startSentBytes)
                {
                    temp += uint.MaxValue;
                }

                TotalSentBytes = temp - m_startSentBytes;

                // Account for the possibility of the counter wrapping
                temp = stats.BytesReceived;
                if (temp < m_startReceivedBytes)
                {
                    temp += uint.MaxValue;
                }

                TotalReceivedBytes = temp - m_startReceivedBytes;

                TotalBytes = TotalSentBytes + TotalReceivedBytes;
            }
            catch (NetworkInformationException)
            {
                TotalSentBytes = 0;
                TotalReceivedBytes = 0;
                TotalBytes = 0;
            }
        }
    }
}
