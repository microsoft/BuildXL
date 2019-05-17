// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Runtime.Serialization;
using System.Security.Permissions;

namespace BuildXL.Engine.Visualization
{
    /// <summary>
    /// Exception thrown by viewer when a required BuildXL component is not yet available
    /// </summary>
    [Serializable]
    public sealed class ValueNotAvailableException : Exception
    {
        /// <summary>
        /// The state the value was in when accessed
        /// </summary>
        /// <remarks>
        /// This is usefull when reporting error messages. One can inform the user if the value might be availalbe in a little bit,
        /// or will never be available.
        /// </remarks>
        public readonly VisualizationValueState State;

        /// <nodoc />
        public ValueNotAvailableException()
            : base()
        {
        }

        /// <nodoc />
        public ValueNotAvailableException(VisualizationValueState state)
        {
            State = state;
        }

        /// <nodoc />
        public ValueNotAvailableException(string message)
            : base(message)
        {
        }

        /// <nodoc />
        public ValueNotAvailableException(string message, Exception innerException)
            : base(message, innerException)
        {
        }

        /// <nodoc />
        private ValueNotAvailableException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
            State = (VisualizationValueState)info.GetInt32("State");
        }

        /// <summary>
        /// ISerializable method which we must override since Exception implements this interface
        /// If we ever add new members to this class, we'll need to update this.
        /// </summary>
        /// <param name="info">Serialization info</param>
        /// <param name="context">Streaming context</param>
        [SecurityPermission(SecurityAction.Demand, SerializationFormatter = true)]
        public override void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            base.GetObjectData(info, context);

            info.AddValue("State", (int)State);
        }
    }
}
