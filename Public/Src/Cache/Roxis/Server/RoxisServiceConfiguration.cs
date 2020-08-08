namespace BuildXL.Cache.Roxis.Server
{
    /// <summary>
    /// Configuration for <see cref="RoxisService"/>
    /// </summary>
    public class RoxisServiceConfiguration
    {
        public RoxisGrpcServiceConfiguration Grpc { get; set; } = new RoxisGrpcServiceConfiguration();

        public RoxisDatabaseConfiguration Database { get; set; } = new RoxisDatabaseConfiguration();
    }
}
