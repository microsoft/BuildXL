using System;

namespace BuildXL.Cache.Monitor.App.Rules
{
    internal class RuleContext
    {
        public Guid RunGuid { get; }

        public DateTime RunTimeUtc { get; }

        public RuleContext(Guid runGuid, DateTime runTimeUtc)
        {
            RunGuid = runGuid;
            RunTimeUtc = runTimeUtc;
        }
    }
}
