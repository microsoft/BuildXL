// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;

namespace BuildXL.Utilities
{
    /// <summary>
    /// Provides throughput statistics for selected network interfaces.
    /// </summary>
    public sealed class NetworkMonitor
    {
        /// <summary>
        /// Array of all network cards.
        /// </summary>
        private readonly NetworkCardMonitor[] m_cards;

        /// <summary>
        /// List of the cards that are active.
        /// </summary>
        private List<NetworkCardMonitor> m_activeCards;

        /// <summary>
        /// Initializes a new instance of the <see cref="NetworkMonitor"/> class.
        /// </summary>
        public NetworkMonitor()
        {
            NetworkInterface[] interfaces = NetworkInterface.GetAllNetworkInterfaces();

            m_cards = new NetworkCardMonitor[interfaces.Length];
            for (int i = 0; i < interfaces.Length; i++)
            {
                m_cards[i] = new NetworkCardMonitor(interfaces[i]);
            }
        }

        /// <summary>
        /// Gets Total Bandwidth.
        /// </summary>
        public long Bandwidth => ActiveCards.Sum(card => card.Bandwidth);

        /// <summary>
        /// Gets Total Bytes.
        /// </summary>
        public long TotalBytes => ActiveCards.Sum(card => card.TotalBytes);

        /// <summary>
        /// Gets total sent bytes.
        /// </summary>
        public long TotalSentBytes => ActiveCards.Sum(card => card.TotalSentBytes);

        /// <summary>
        /// Gets total received bytes.
        /// </summary>
        public long TotalReceivedBytes => ActiveCards.Sum(card => card.TotalReceivedBytes);

        /// <summary>
        /// Gets active cards.
        /// </summary>
        private List<NetworkCardMonitor> ActiveCards
        {
            get
            {
                if (m_activeCards == null || m_activeCards.Count == 0)
                {
                    m_activeCards = new List<NetworkCardMonitor>();

                    for (int i = 0; i < m_cards.Length; i++)
                    {
                        NetworkCardMonitor card = m_cards[i];

                        if ((card.IsEthernet || card.IsWireless) && card.IsOperational && (card.TotalBytes > 0))
                        {
                            m_activeCards.Add(card);
                        }
                    }
                }

                return m_activeCards;
            }
        }

        /// <summary>
        /// Starts the measurement.
        /// </summary>
        public void StartMeasurement()
        {
            if (m_activeCards != null)
            {
                m_activeCards.Clear();
                m_activeCards = null;
            }

            for (int i = 0; i < m_cards.Length; i++)
            {
                m_cards[i].StartMeasurement();
            }
        }

        /// <summary>
        /// Stops the measurement.
        /// </summary>
        public void StopMeasurement()
        {
            for (int i = 0; i < m_cards.Length; i++)
            {
                m_cards[i].StopMeasurement();
            }

            if (m_activeCards != null)
            {
                m_activeCards.Clear();
                m_activeCards = null;
            }
        }
    }
}
