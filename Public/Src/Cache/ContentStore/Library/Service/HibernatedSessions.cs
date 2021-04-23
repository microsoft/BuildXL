// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Runtime.Serialization;

namespace BuildXL.Cache.ContentStore.Service
{
    /// <summary>
    ///     Information stored for a set of sessions that can be restored.
    /// </summary>
    [DataContract]
    public class HibernatedSessions<THibernatedSessionInfo>
    {
        private const int LatestVersion = 1;

        /// <summary>
        ///     Initializes a new instance of the <see cref="HibernatedSessions{THibernatedSessionInfo}"/> class.
        /// </summary>
        public HibernatedSessions(IList<THibernatedSessionInfo> sessions)
        {
            Version = LatestVersion;
            Sessions = sessions;
        }

        /// <summary>
        ///     Gets or sets version of the stored data.
        /// </summary>
        [DataMember]
        public int Version { get; set; }

        /// <summary>
        ///     Gets or sets the set of hibernated sessions.
        /// </summary>
        [DataMember]
        public IList<THibernatedSessionInfo> Sessions { get; set; }
    }
}
