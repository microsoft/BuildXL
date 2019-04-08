// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Runtime.Serialization;

namespace BuildXL.Cache.ContentStore.Service
{
    /// <summary>
    ///     Information stored for a set of sessions that can be restored.
    /// </summary>
    [DataContract]
    public class HibernatedSessions
    {
        private const int LatestVersion = 1;

        /// <summary>
        ///     Initializes a new instance of the <see cref="HibernatedSessions"/> class.
        /// </summary>
        public HibernatedSessions(IList<HibernatedSessionInfo> sessions)
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
        public IList<HibernatedSessionInfo> Sessions { get; set; }
    }
}
