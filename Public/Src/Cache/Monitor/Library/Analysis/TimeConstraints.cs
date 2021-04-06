// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Text;

namespace BuildXL.Cache.Monitor.Library.Analysis
{
    public interface ITimeConstraint
    {
        public bool Satisfied(DateTime now);
    }

    public class BusinessHoursConstraint : ITimeConstraint
    {
        public readonly static BusinessHoursConstraint Instance = new BusinessHoursConstraint();

        private BusinessHoursConstraint()
        {
        }

        public bool Satisfied(DateTime now)
        {
            if (now.DayOfWeek == DayOfWeek.Saturday || now.DayOfWeek == DayOfWeek.Sunday)
            {
                return false;
            }

            if (now.Hour < 9 || now.Hour > 17)
            {
                return false;
            }

            return true;
        }
    }

    public static class TimeConstraints
    {
        public readonly static ITimeConstraint BusinessHours = BusinessHoursConstraint.Instance;

        public static bool SatisfiedPST(this ITimeConstraint constraint, DateTime utcNow)
        {
            var pstNow = TimeZoneInfo.ConvertTimeBySystemTimeZoneId(
                utcNow,
                "Pacific Standard Time");
            return constraint.Satisfied(pstNow);
        }
    }
}
