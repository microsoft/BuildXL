using System;

namespace BuildXL.Cache.Monitor.App.Rules
{
    internal interface IEnvironmentRule : IRule
    {
        Environment Environment { get; }
    }
}
