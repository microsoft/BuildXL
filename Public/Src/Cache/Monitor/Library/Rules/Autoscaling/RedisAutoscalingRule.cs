// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.ContractsLight;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.Monitor.App.Scheduling;
using BuildXL.Cache.Monitor.Library.Rules;
using BuildXL.Cache.Monitor.Library.Rules.Autoscaling;

namespace BuildXL.Cache.Monitor.App.Rules.Autoscaling
{
    internal class RedisAutoscalingRule : SingleStampRuleBase
    {
        public class Configuration : SingleStampRuleConfiguration
        {
            public Configuration(SingleStampRuleConfiguration kustoRuleConfiguration)
                : base(kustoRuleConfiguration)
            {
            }
        }

        private readonly Configuration _configuration;

        private readonly RedisAutoscalingAgent _redisAutoscalingAgent;
        private readonly IRedisInstance _primaryRedisInstance;
        private readonly IRedisInstance _secondaryRedisInstance;

        /// <inheritdoc />
        public override string Identifier => $"{nameof(RedisAutoscalingRule)}:{_configuration.StampId}";

        public override string ConcurrencyBucket => "RedisAutoscaler";

        public RedisAutoscalingRule(Configuration configuration, RedisAutoscalingAgent redisAutoscalingAgent, IRedisInstance primaryRedisInstance, IRedisInstance secondaryRedisInstance)
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
                Emit(context, "Autoscale", Severity.Warning, $"Instance `{_primaryRedisInstance.Name}` is undergoing maintenance or autoscaling operation. State=[{_primaryRedisInstance.State}]");
                return;
            }

            if (!_secondaryRedisInstance.IsReadyToScale)
            {
                Emit(context, "Autoscale", Severity.Warning, $"Instance `{_secondaryRedisInstance.Name}` is undergoing maintenance or autoscaling operation. State=[{_secondaryRedisInstance.State}]");
                return;
            }

            await AttemptToScaleAsync(context, _primaryRedisInstance, context.CancellationToken);

            // A long time may have passed since the first scale, so we refresh the data and re-check before looking at
            // the secondary.
            await Task.WhenAll(_primaryRedisInstance.RefreshAsync(context.CancellationToken), _secondaryRedisInstance.RefreshAsync(context.CancellationToken)).ThrowIfFailureAsync();

            if (!_primaryRedisInstance.IsReadyToScale)
            {
                Emit(context, "Autoscale", Severity.Warning, $"Instance `{_primaryRedisInstance.Name}` is undergoing maintenance or autoscaling operation. State=[{_primaryRedisInstance.State}]");
                return;
            }

            if (!_secondaryRedisInstance.IsReadyToScale)
            {
                Emit(context, "Autoscale", Severity.Warning, $"Instance `{_secondaryRedisInstance.Name}` is undergoing maintenance or autoscaling operation. State=[{_secondaryRedisInstance.State}]");
                return;
            }

            await AttemptToScaleAsync(context, _secondaryRedisInstance, context.CancellationToken);
        }

        public async Task<bool> AttemptToScaleAsync(RuleContext context, IRedisInstance redisInstance, CancellationToken cancellationToken)
        {
            Contract.Requires(redisInstance == _primaryRedisInstance || redisInstance == _secondaryRedisInstance);
            Contract.Requires(redisInstance.IsReadyToScale);

            // Fetch which cluster size we want, and start scaling operation if needed.
            var currentClusterSize = redisInstance.ClusterSize;
            var targetClusterSizeResult = await _redisAutoscalingAgent.EstimateBestClusterSizeAsync(context.IntoOperationContext(_configuration.Logger), redisInstance);
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

            var scaleResult = await redisInstance.ScaleAsync(modelOutput.ScalePath, cancellationToken);
            if (!scaleResult)
            {
                Emit(context, "Autoscale", Severity.Error, $"Autoscale attempt from `{currentClusterSize}` to `{targetClusterSize}` for instance `{redisInstance.Name}` failed. Result=[{scaleResult}]");
            }

            scaleResult.ThrowIfFailure();

            return true;
        }
    }
}
