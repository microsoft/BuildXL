// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.ContractsLight;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.Monitor.App.Scheduling;

namespace BuildXL.Cache.Monitor.App.Rules.Autoscaling
{
    internal class RedisAutoscalingRule : KustoRuleBase
    {
        public class Configuration : KustoRuleConfiguration
        {
            public Configuration(KustoRuleConfiguration kustoRuleConfiguration)
                : base(kustoRuleConfiguration)
            {
            }
        }

        private readonly Configuration _configuration;

        private readonly RedisAutoscalingAgent _redisAutoscalingAgent;
        private readonly RedisInstance _primaryRedisInstance;
        private readonly RedisInstance _secondaryRedisInstance;

        public override string Identifier => $"{nameof(RedisAutoscalingRule)}:{_configuration.StampId}";

        public override string ConcurrencyBucket => "RedisAutoscaler";

        public RedisAutoscalingRule(Configuration configuration, RedisAutoscalingAgent redisAutoscalingAgent, RedisInstance primaryRedisInstance, RedisInstance secondaryRedisInstance)
            : base(configuration)
        {
            Contract.Assert(primaryRedisInstance != secondaryRedisInstance);
            _configuration = configuration;

            _redisAutoscalingAgent = redisAutoscalingAgent;
            _primaryRedisInstance = primaryRedisInstance;
            _secondaryRedisInstance = secondaryRedisInstance;
        }

        public override async Task Run(RuleContext context)
        {
            // Short-circuit if either instance is being scaled, as the autoscaling is either externally triggered or a
            // monitor bug.
            await Task.WhenAll(_primaryRedisInstance.RefreshAsync(context.CancellationToken), _secondaryRedisInstance.RefreshAsync(context.CancellationToken)).ThrowIfFailureAsync();

            if (!_primaryRedisInstance.IsReadyToScale)
            {
                Emit(context, "Autoscale", Severity.Info, $"Instance `{_primaryRedisInstance.Name}` is undergoing maintenance or autoscaling operation");
                return;
            }

            if (!_secondaryRedisInstance.IsReadyToScale)
            {
                Emit(context, "Autoscale", Severity.Info, $"Instance `{_secondaryRedisInstance.Name}` is undergoing maintenance or autoscaling operation");
                return;
            }

            await AttemptToScaleAsync(context, _primaryRedisInstance, context.CancellationToken);

            // A long time may have passed since the first scale, so we refresh the data and re-check before looking at
            // the secondary.
            await Task.WhenAll(_primaryRedisInstance.RefreshAsync(context.CancellationToken), _secondaryRedisInstance.RefreshAsync(context.CancellationToken)).ThrowIfFailureAsync();

            if (!_primaryRedisInstance.IsReadyToScale)
            {
                Emit(context, "Autoscale", Severity.Info, $"Instance `{_primaryRedisInstance.Name}` is undergoing maintenance or autoscaling operation");
                return;
            }

            if (!_secondaryRedisInstance.IsReadyToScale)
            {
                Emit(context, "Autoscale", Severity.Info, $"Instance `{_secondaryRedisInstance.Name}` is undergoing maintenance or autoscaling operation");
                return;
            }

            await AttemptToScaleAsync(context, _secondaryRedisInstance, context.CancellationToken);
        }

        public async Task<bool> AttemptToScaleAsync(RuleContext context, RedisInstance redisInstance, CancellationToken cancellationToken)
        {
            Contract.Requires(redisInstance == _primaryRedisInstance || redisInstance == _secondaryRedisInstance);
            Contract.Requires(redisInstance.IsReadyToScale);

            // Fetch which cluster size we want, and start scaling operation if needed.
            var currentClusterSize = redisInstance.ClusterSize;
            var targetClusterSizeResult = await _redisAutoscalingAgent.EstimateBestClusterSizeAsync(redisInstance, cancellationToken);
            if (!targetClusterSizeResult.Succeeded)
            {
                Emit(context, "Autoscale", Severity.Error, $"Failed to find best plan for instance `{redisInstance.Name}` in plan `{currentClusterSize}`. Result=[{targetClusterSizeResult}]");
                return false;
            }

            var modelOutput = targetClusterSizeResult.Value;
            Contract.AssertNotNull(modelOutput);

            var targetClusterSize = modelOutput.TargetClusterSize;
            if (targetClusterSize.Equals(redisInstance.ClusterSize) || modelOutput.ScalePath.Count == 0)
            {
                // No autoscale required
                return false;
            }

            Emit(context, "Autoscale", Severity.Warning, $"Autoscaling from `{currentClusterSize}` to `{targetClusterSize}` via scale path `{currentClusterSize} -> {string.Join(" -> ", modelOutput.ScalePath)}` for instance `{redisInstance.Name}`. Solution cost is `{modelOutput.Cost}`");

            await redisInstance.ScaleAsync(modelOutput.ScalePath, cancellationToken).ThrowIfFailureAsync();

            return true;
        }
    }
}
