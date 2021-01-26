// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
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

            public TimeSpan IcmIncidentCacheTtl { get; set; } = TimeSpan.FromHours(1);

            public TimeSpan MinimumWaitTimeBetweenDownscaleSteps { get; set; } = TimeSpan.FromMinutes(20);
        }

        private readonly Configuration _configuration;

        private readonly RedisAutoscalingAgent _redisAutoscalingAgent;
        private readonly IRedisInstance _primaryRedisInstance;
        private readonly IRedisInstance _secondaryRedisInstance;

        private readonly Dictionary<string, DateTime> _lastAutoscaleTimeUtc = new Dictionary<string, DateTime>();

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

            await AttemptToScaleAsync(context, primary);
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

        private async Task<bool> AttemptToScaleAsync(RuleContext context, IRedisInstance redisInstance)
        {
            Contract.Requires(redisInstance.IsReadyToScale);
            var operationContext = context.IntoOperationContext(_configuration.Logger);

            // Fetch which cluster size we want, and start scaling operation if needed.
            var currentClusterSize = redisInstance.ClusterSize;
            var targetClusterSizeResult = await _redisAutoscalingAgent.EstimateBestClusterSizeAsync(operationContext, redisInstance);
            if (!targetClusterSizeResult.Succeeded)
            {
                Emit(context, "Autoscale", Severity.Error, $"Failed to find best plan for instance `{redisInstance.Name}` in plan `{currentClusterSize}`. Result=[{targetClusterSizeResult}]");
                return false;
            }

            var modelOutput = targetClusterSizeResult.Value;
            Contract.AssertNotNull(modelOutput);

            var targetClusterSize = modelOutput.TargetClusterSize;
            if (targetClusterSize.Equals(currentClusterSize) || modelOutput.ScalePath.Count == 0)
            {
                return false;
            }

            if (RedisScalingUtilities.IsDownScale(currentClusterSize, targetClusterSize))
            {
                // Downscales are typically about saving money rather than system health, hence, it's deprioritized.

                // Force downscales to happen during very comfortable business hours in PST, to ensure we're always
                // available if things go wrong. We disregard holidays because it's a pain to handle them.
                var nowPst = TimeZoneInfo.ConvertTimeBySystemTimeZoneId(_configuration.Clock.UtcNow, "Pacific Standard Time");
                if (!IsAutoscaleTimeAllowed(nowPst))
                {
                    Emit(context, "Autoscale", Severity.Info, $"Refused autoscale from `{currentClusterSize}` to `{targetClusterSize}` via scale path `{currentClusterSize} -> {string.Join(" -> ", modelOutput.ScalePath)}` for instance `{redisInstance.Name}` due to business hours constraints");
                    return false;
                }

                // Downscales are performed in phases instead of all at once. If the model proposes an autoscale, we'll
                // only take the first step of it in the current iteration, and force wait some amount of time until we
                // allow this instance to be downscaled again. This gives some time to evaluate the effects of the last
                // downscale (which typically takes time because migration's effects on instance memory and cpu load
                // take some time to see).
                //
                // The intent of this measure is to avoid situations where our downscale causes heightened load in the
                // remaining shards, forcing us to scale back to our original size after some time. This effect creates
                // "autoscale loops" over time.
                modelOutput.ScalePath = modelOutput.ScalePath.Take(1).ToList();
                if (_lastAutoscaleTimeUtc.TryGetValue(redisInstance.Id, out var lastAutoscaleTimeUtc))
                {
                    var now = _configuration.Clock.UtcNow;
                    if (now - lastAutoscaleTimeUtc < _configuration.MinimumWaitTimeBetweenDownscaleSteps)
                    {
                        return false;
                    }
                }
            }

            Emit(context, "Autoscale", Severity.Warning, $"Autoscaling from `{currentClusterSize}` ({currentClusterSize.MonthlyCostUsd} USD/mo) to `{targetClusterSize}` ({targetClusterSize.MonthlyCostUsd} USD/mo) via scale path `{currentClusterSize} -> {string.Join(" -> ", modelOutput.ScalePath)}` for instance `{redisInstance.Name}`. CostFunction=[{modelOutput.Cost}]");

            var scaleResult = await redisInstance.ScaleAsync(operationContext, modelOutput.ScalePath);
            _lastAutoscaleTimeUtc[redisInstance.Id] = _configuration.Clock.UtcNow;
            if (!scaleResult)
            {
                Emit(context, "Autoscale", Severity.Error, $"Autoscale attempt from `{currentClusterSize}` to `{targetClusterSize}` for instance `{redisInstance.Name}` failed. Result=[{scaleResult}]");
                scaleResult.ThrowIfFailure();
            }

            return true;
        }

        private static bool IsAutoscaleTimeAllowed(DateTime nowPst)
        {
            if (nowPst.DayOfWeek == DayOfWeek.Saturday || nowPst.DayOfWeek == DayOfWeek.Sunday)
            {
                return false;
            }

            if (nowPst.Hour < 10 || nowPst.Hour > 16)
            {
                return false;
            }

            return true;
        }
    }
}
