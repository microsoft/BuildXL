using System;
using System.Diagnostics.ContractsLight;
using System.Threading.Tasks;
using BuildXL.Cache.Monitor.App.Scheduling;

namespace BuildXL.Cache.Monitor.Library.Scheduling
{
    internal class LambdaRule : IRule
    {
        public string Identifier { get; }

        public string ConcurrencyBucket { get; }

        private readonly Func<RuleContext, Task> _lambda;

        public LambdaRule(string identifier, string concurrencyBucket, Func<RuleContext, Task> lambda)
        {
            Contract.RequiresNotNullOrEmpty(identifier);
            Contract.RequiresNotNullOrEmpty(concurrencyBucket);

            Identifier = identifier;
            ConcurrencyBucket = concurrencyBucket;
            _lambda = lambda;
        }

        public Task Run(RuleContext context)
        {
            return _lambda(context);
        }
    }
}
