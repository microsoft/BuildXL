// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;

namespace BuildXL.Visualization.Models
{
    /// <summary>
    /// Model for ping message
    /// </summary>
    public static class PingModel
    {
        /// <summary>
        /// Time of last client ping
        /// </summary>
        public static DateTime LastClientPing { get; private set; }

        /// <summary>
        /// Client web page was closed (or navigated away from).
        /// </summary>
        private static bool IsPageClosed { get; set; }

        /// <summary>
        /// Client ping interval
        /// </summary>
        public static readonly TimeSpan PingInterval = TimeSpan.FromSeconds(2);

        /// <summary>
        /// The ping timeout duration
        /// </summary>
        public static readonly TimeSpan PingTimeoutDuration = TimeSpan.FromTicks(PingInterval.Ticks * 5);

        /// <summary>
        /// The shutdown timeout duration.
        ///
        /// Once a viewer web page has been closed (in the browser), this is how many seconds we wait before
        /// declaring that the viewer is dead (given that no ping was received in the meantime).
        ///
        /// The reason why a short timeout is sufficient here (shorter than <see cref="PingTimeoutDuration"/>)
        /// is that when e.g., a link is clicked in the viewer, it navigates away from the current page
        /// (causing <see cref="PageClosed"/> to be called), but then immediatelly opens a new page which pings
        /// the server (<see cref="PingReceived"/>, invalidating <see cref="IsPageClosed"/>.
        /// </summary>
        public static readonly TimeSpan ShutdownTimeoutDuration = PingInterval;

        /// <summary>
        /// Response to receiving a ping message: updates <see cref="LastClientPing"/> and resets <see cref="IsPageClosed"/>.
        /// </summary>
        public static void PingReceived()
        {
            LastClientPing = DateTime.UtcNow;
            IsPageClosed = false;
        }

        /// <summary>
        /// Response to user navigating away from a viewer web page: sets <see cref="IsPageClosed"/> to <code>true</code>.
        /// </summary>
        public static void PageClosed()
        {
            IsPageClosed = true;
        }

        /// <summary>
        /// Returns whether the viewer is alive.  The rule for determining if the viewer
        /// is still alive is this:
        ///   - if <see cref="IsPageClosed"/> is true:
        ///         it's alive if the time difference between now and when the last ping was received is
        ///         less than <see cref="ShutdownTimeoutDuration"/>
        ///   - else:
        ///         it's alive if the time difference between now and when the last ping was received is
        ///         less than <see cref="PingTimeoutDuration"/> (which is greater than <see cref="ShutdownTimeoutDuration"/>).
        /// </summary>
        public static bool IsViewerAlive()
        {
            var pingDelta = DateTime.UtcNow.Subtract(LastClientPing);
            var timeout = IsPageClosed ? ShutdownTimeoutDuration : PingTimeoutDuration;
            return pingDelta < timeout;
        }
    }
}
