// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using BuildXL.Cache.ContentStore.Interfaces.Stores;

namespace BuildXL.Cache.ContentStore.Service
{
    /// <summary>
    ///     Information stored about a session that can be later restored.
    /// </summary>
    [DataContract]
    public class HibernatedSessionInfo
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="HibernatedSessionInfo"/> class.
        /// </summary>
        public HibernatedSessionInfo(int id, string sessionName, ImplicitPin implicitPin, string cacheName, IList<string> pins, long expirationUtcTicks, Capabilities capabilities)
        {
            Id = id;
            Session = sessionName;
            Pin = implicitPin;
            Cache = cacheName;
            Pins = pins;
            ExpirationUtcTicks = expirationUtcTicks;
            Capabilities = capabilities;
        }

        /// <summary>
        ///     Gets identification number expected by client.
        /// </summary>
        [DataMember]
        public int Id { get; private set; }

        /// <summary>
        ///     Gets session name assigned at original creation.
        /// </summary>
        [DataMember]
        public string Session { get; private set; }

        /// <summary>
        ///     Gets implicit pinning option assigned at original creation.
        /// </summary>
        [DataMember]
        public ImplicitPin Pin { get; private set; }

        /// <summary>
        ///     Gets name of cache backing the session.
        /// </summary>
        [DataMember]
        public string Cache { get; private set; }

        /// <summary>
        ///     Gets set of content hashes that are to be pinned in the session.
        /// </summary>
        [DataMember]
        public IList<string> Pins { get; private set; }

        /// <summary>
        ///     Gets a value indicating whether or not the session is sensitive.
        /// </summary>
        [Obsolete("Left for backward compatibility.")]
        [DataMember]
        public Sensitivity Sensitivity { get; private set; } = 0;

        /// <summary>
        ///     Gets a value indicating the expiration time for the session, in ticks.
        /// </summary>
        [DataMember]
        public long ExpirationUtcTicks { get; private set; }

        /// <summary>
        ///     Gets a value indicating the capabilities of the session.
        /// </summary>
        [DataMember]
        public Capabilities Capabilities { get; private set; }
    }
}
