// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.Monitor.App;
using ContentStoreTest.Test;
using Microsoft.Azure.Management.Redis.Fluent;
using Microsoft.Azure.Management.Redis.Fluent.Models;
using Xunit;
using Xunit.Abstractions;
using DayOfWeek = Microsoft.Azure.Management.Redis.Fluent.Models.DayOfWeek;

namespace BuildXL.Cache.Monitor.Test
{
    public class RedisMaintenanceSchedule : TestBase
    {
        public RedisMaintenanceSchedule(ITestOutputHelper output) : base(output)
        {
        }

        [Fact(Skip = "Manual use only")]
        public async Task SetRedisMaintenanceScheduleAsync()
        {
            LoadApplicationKey().ThrowIfFailure();

            var logger = TestGlobal.Logger;

            var tracingContext = new Context(logger);
            var context = new OperationContext(tracingContext);

            var environmentResources = await App.Monitor.CreateEnvironmentResourcesAsync(context, Constants.DefaultEnvironments);

            var watchlist = await Watchlist.CreateAsync(
                logger,
                Constants.DefaultEnvironments,
                environmentResources.ToDictionary(
                    kvp => kvp.Key,
                    kvp => kvp.Value.KustoQueryClient));


            var rings = (new string[] { "Ring_0", "Ring_1", "Ring_2", "Ring_3" }).Reverse().ToArray();
            var primaryInstancesPerRing = rings.ToDictionary(r => r, r => new List<IRedisCache>());
            var secondaryInstancesPerRing = rings.ToDictionary(r => r, r => new List<IRedisCache>());

            var resources = environmentResources[MonitorEnvironment.CloudBuildProduction];
            foreach (var stamp in watchlist.EnvStamps[MonitorEnvironment.CloudBuildProduction])
            {
                var metadata = watchlist.TryGetProperties(stamp).GetValueOrThrow();

                if (resources.RedisCaches.TryGetValue(stamp.PrimaryRedisName, out var primary))
                {
                    primaryInstancesPerRing[metadata.Ring].Add(primary);
                }

                if (resources.RedisCaches.TryGetValue(stamp.SecondaryRedisName, out var secondary))
                {
                    secondaryInstancesPerRing[metadata.Ring].Add(secondary);
                }
            }

            var primaryDayByRing = new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday };
            var secondaryDayByRing = new[] { DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday };
            var startHourUtc = 17;
            var maintenanceWindow = TimeSpan.FromHours(6);

            var exceptions = new List<Exception>();

            foreach (var (ring, position) in rings.Select((x, i) => (x, i)))
            {
                foreach (var instance in primaryInstancesPerRing[ring])
                {
                    try
                    {
                        await SetMaintenanceScheduleAsync(instance, primaryDayByRing[position], startHourUtc, maintenanceWindow);
                    }
                    catch (Exception e)
                    {
                        tracingContext.Error(e, $"Failed to set maintenance schedule for instance {instance.Name}", nameof(RedisMaintenanceSchedule));
                        exceptions.Add(e);
                    }
                }

                foreach (var instance in secondaryInstancesPerRing[ring])
                {
                    try
                    {
                        await SetMaintenanceScheduleAsync(instance, secondaryDayByRing[position], startHourUtc, maintenanceWindow);
                    }
                    catch (Exception e)
                    {
                        tracingContext.Error(e, $"Failed to set maintenance schedule for instance {instance.Name}", nameof(RedisMaintenanceSchedule));
                        exceptions.Add(e);
                    }
                }
            }

            if (exceptions.Count > 0)
            {
                throw new AggregateException(innerExceptions: exceptions);
            }
        }

        private static async Task SetMaintenanceScheduleAsync(IRedisCache instance, DayOfWeek day, int startHourUtc, TimeSpan maintenanceWindow)
        {
            await instance.AsPremium()
                .Update()
                .WithoutPatchSchedule()
                .ApplyAsync();

            var scheduleEntry = new ScheduleEntry(new ScheduleEntryInner(
                day,
                startHourUtc,
                maintenanceWindow));
            await instance.Update()
                .WithPatchSchedule(scheduleEntry)
                .ApplyAsync();
        }
    }
}
