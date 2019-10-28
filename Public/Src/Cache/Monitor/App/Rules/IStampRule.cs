namespace BuildXL.Cache.Monitor.App.Rules
{
    internal interface IStampRule : IEnvironmentRule
    {
        string Stamp { get; }
    }
}
