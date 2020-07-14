using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using BuildXL.Cache.Host.Configuration;
using static BuildXL.Cache.Host.Configuration.DeploymentManifest;

namespace BuildXL.Launcher.Server
{
    public class DeploymentParameters
    {
        public string Environment { get; set; } = "Default";
        public string Stamp { get; set; } = "Default";
        public string Ring { get; set; } = "Default";
        public string Machine { get; set; } = "Default";
        public string Region { get; set; } = "Default";
        public string MachineFunction { get; set; } = "Default";
    }
}
