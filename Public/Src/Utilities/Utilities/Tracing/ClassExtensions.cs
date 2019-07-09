// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.Tracing;

#if NET_FRAMEWORK_451
namespace System.Diagnostics.Tracing
{
    /// <summary>Placeholder for NET Framework 4.5.1 builds</summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false)]
    public class EventDataAttribute : Attribute
    {
        /// <nodoc />
        public EventDataAttribute() {}

        /// <nodoc />
        public string Name { get; set; }
    }
}
#endif


namespace BuildXL.Utilities.Tracing
{
    /// <summary>
    /// Various class extensions.
    /// </summary>
    public static class ClassExtensions
    {
        /// <summary>
        /// Returns the name of the event.
        /// 
        /// In .NET Framework 4.5.1 there is no "EventName" property on <see cref="EventWrittenEventArgs"/>,
        /// so in this case the event name is computed by looking up its id (<see cref="EventWrittenEventArgs.EventId"/>)
        /// in <see cref="EventId"/> enum and returning the name of the corresponding enum constant.
        /// 
        /// In all other cases the value of the 'EventName' property is returned.
        /// </summary>
        public static string GetEventName(this EventWrittenEventArgs @this)
        {
#if NET_FRAMEWORK_451
            return ((EventId)@this.EventId).ToString();
#else
            return @this.EventName;
#endif
        }
    }
}
