// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
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

            public TimeSpan IcmIncidentCacheTtl { get; set; } = TimeSpan.FromHours(12);
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
            var validationOutcome = await ValidateAndScaleAsync(context, _primaryRedisInstance, _secondaryRedisInstance);
            switch (validationOutcome)
            {
                case ValidationOutcome.PrimaryUndergoingAutoscale:
                case ValidationOutcome.SecondaryUndergoingAutoscale:
                    // In these two cases, we already know that we won't be able to scale the secondary because
                    // the information is fresh and validation won't pass.
                    return;
                case ValidationOutcome.PrimaryFailed:
                case ValidationOutcome.Success:
                    // In these two cases, either we just scaled the primary or we failed to scale it but it was a
                    // recoverable error. We'll actually try to scale the secondary. Note that it is possible for the
                    // secondary to be in a failed state, in which case the following autoscale attempt will fail.
                    break;
            }

            await ValidateAndScaleAsync(context, _secondaryRedisInstance, _primaryRedisInstance);
        }

        private enum ValidationOutcome
        {
            PrimaryUndergoingAutoscale,
            PrimaryFailed,
            SecondaryUndergoingAutoscale,

            // Note that this doesn't mean that the autoscale operation itself succeeded, only that validation passed.
            Success,
        }

        private async Task<ValidationOutcome> ValidateAndScaleAsync(RuleContext context, IRedisInstance primary, IRedisInstance secondary)
        {
            // Last refresh time may be arbitrarily long, either because the rule hasn't been run for a long time, or
            // because there was an autoscale that happened before. Hence, we need to refresh what we know.
            await Task.WhenAll(primary.RefreshAsync(context.CancellationToken), secondary.RefreshAsync(context.CancellationToken)).ThrowIfFailureAsync();

            // We are willing to scale iff:
            //  1. The instance is ready to scale
            //  2. The other instance is not being scaled, but may be not ready to scale
            if (!primary.IsReadyToScale)
            {
                Emit(context, "Autoscale", Severity.Warning, $"Instance `{primary.Name}` is undergoing maintenance or autoscaling operation. State=[{primary.State}]");
                await CreateIcmForFailedStateIfNeededAsync(primary);

                if (primary.IsFailed)
                {
                    return ValidationOutcome.PrimaryFailed;
                }
                else
                {
                    return ValidationOutcome.PrimaryUndergoingAutoscale;
                }
            }

            if (!secondary.IsReadyToScale && !secondary.IsFailed)
            {
                Emit(context, "Autoscale", Severity.Warning, $"Instance `{secondary.Name}` is undergoing maintenance or autoscaling operation. State=[{secondary.State}]");
                await CreateIcmForFailedStateIfNeededAsync(secondary);

                return ValidationOutcome.SecondaryUndergoingAutoscale;
            }

            await AttemptToScaleAsync(context, primary, context.CancellationToken);
            return ValidationOutcome.Success;
        }

        private async Task CreateIcmForFailedStateIfNeededAsync(IRedisInstance instance)
        {
            if (instance.State != "Failed")
            {
                return;
            }

            try
            {
                await EmitIcmAsync(
                    severity: 3,
                    title: $"{instance.Name} is in a failed state. State=[{instance.State}]",
                    description: "Instance fell into a failed state. Please monitor it and open an IcM against the Windows Azure Cache team for support if needed.",
                    machines: null,
                    correlationIds: null,
                    cacheTimeToLive: _configuration.IcmIncidentCacheTtl);
            }
            catch (Exception e)
            {
                _configuration.Logger.Error($"Failed to emit IcM for failed instance {instance.Name}: {e}");
            }
        }

        private async Task<bool> AttemptToScaleAsync(RuleContext context, IRedisInstance redisInstance, CancellationToken cancellationToken)
        {
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
                return false;
            }

            Emit(context, "Autoscale", Severity.Warning, $"Autoscaling from `{currentClusterSize}` to `{targetClusterSize}` via scale path `{currentClusterSize} -> {string.Join(" -> ", modelOutput.ScalePath)}` for instance `{redisInstance.Name}`. Solution cost is `{modelOutput.Cost}`");

            var scaleResult = await redisInstance.ScaleAsync(modelOutput.ScalePath, cancellationToken);
            if (!scaleResult)
            {
                Emit(context, "Autoscale", Severity.Error, $"Autoscale attempt from `{currentClusterSize}` to `{targetClusterSize}` for instance `{redisInstance.Name}` failed. Result=[{scaleResult}]");
                scaleResult.ThrowIfFailure();
            }

            return true;
        }
    }
}
