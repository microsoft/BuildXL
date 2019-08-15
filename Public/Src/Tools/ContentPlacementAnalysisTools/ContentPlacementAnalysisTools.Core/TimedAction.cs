using System;
using System.Diagnostics;

namespace ContentPlacementAnalysisTools.Core
{
    /// <summary>
    /// Represents an action inside a pipeline of actions whose execution time can be measured. For intermmediate actions
    /// (takes an input and produces an output) use PerformProducerAction, otherwise use PerformAction
    /// </summary>
    public abstract class TimedAction<IType, OType>
    {
        /// <summary>
        /// To measure the elapsed time
        /// </summary>
        protected Stopwatch Stopwatch = null;
        /// <summary>
        /// This is the entry point for one of these objects and is the method you should call when
        /// looking to process an input but not returning anything
        /// </summary>
        public void PerformAction(IType input)
        {
            InternalPerform(input);
        }
        /// <summary>
        /// This is the entry point for one of these objects and is the method you should call when
        /// looking to process an input and returning an output
        /// </summary>
        public TimedActionResult<OType> PerformActionWithResult(IType input)
        {
            return InternalPerform(input);
        }

        private TimedActionResult<OType> InternalPerform(IType input)
        {
            Stopwatch = Stopwatch.StartNew();
            try
            {
                // execute prepare first
                Setup();
                // perform now...
                var result = Perform(input);
                // and clean up
                CleanUp();
                if (result != null)
                {
                    return new TimedActionResult<OType>(result);
                }
                return new TimedActionResult<OType>(new Exception("Action returns null output..."));
            }
            catch(Exception e)
            {
                return new TimedActionResult<OType>(e);
            }
            finally
            {
                Stopwatch.Stop();
            }
        }

        /// <summary>
        /// Setup routine, called before perform
        /// </summary>
        protected abstract void Setup();
        /// <summary>
        /// Setup routine, called after perform
        /// </summary>
        protected abstract void CleanUp();
        /// <summary>
        /// Perform block that actually performs the action and returns a value
        /// </summary>
        protected abstract OType Perform(IType input);

    }

    /// <summary>
    /// Result of an action, containing an execution status (false implies error), and exception
    /// (in case of error) and a result (defual when there is an error)
    /// </summary>
    public class TimedActionResult<OType>
    {
        /// <summary>
        /// False when the execution failed
        /// </summary>
        public bool ExecutionStatus { get; }
        /// <summary>
        /// In case of failure, contains the related exception
        /// </summary>
        public Exception Exception { get; }
        /// <summary>
        /// The result of the action
        /// </summary>
        public OType Result { get; }

        /// <summary>
        /// Constructor for successful actions
        /// </summary>
        public TimedActionResult(OType result)
        {
            Result = result;
            ExecutionStatus = true;
            Exception = null;
        }

        /// <summary>
        /// Constructor for failed actions
        /// </summary>
        public TimedActionResult(Exception e)
        {
            Result = default;
            ExecutionStatus = false;
            Exception = e;
        }
    }
}
