using System.Net;

namespace BuildXL.Cache.Roxis.Server
{
    /// <summary>
    /// Configuration for <see cref="RoxisGrpcService"/>
    /// </summary>
    public class RoxisGrpcServiceConfiguration
    {
        public const int DefaultPort = Common.Constants.DefaultGrpcPort;

        public int ThreadPoolSize { get; set; } = 70;

        public string BindAddress { get; set; } = IPAddress.Any.ToString();

        public int Port { get; set; } = DefaultPort;

        public int RequestCallTokensPerCompletionQueue { get; set; } = 7000;
    }
}
