// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Threading.Tasks;

namespace BuildXL.Cache.Monitor.Library.IcM
{
    public class MockIcmClient : IIcmClient
    {
        public List<IcmIncident> Incidents = new List<IcmIncident>();

        public Task EmitIncidentAsync(IcmIncident incident)
        {
            Incidents.Add(incident);
            return Task.CompletedTask;
        }
    }
}
