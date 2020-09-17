using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace CopyClient
{

    public static class TaskExtensions
    {

        public static void CancelAndForget (this Task task)
        {
            task.ContinueWith(t =>
            {
                Console.WriteLine(t.Exception);
            });
        }

        public static Task<T> RunWithTimeout<T> (Func<CancellationToken, Task<T>> factory, TimeSpan time, CancellationToken ct = default(CancellationToken))
        {
            using (CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                Task<T> task = factory(cts.Token).TimeoutAfter(time, cts);
                return (task);
            }
        }

        /// <summary>
        /// Add a timeout to an existing task.
        /// </summary>
        /// <typeparam name="T">The type of the task result.</typeparam>
        /// <param name="task">The task.</param>
        /// <param name="time">The time span after which to timeout.</param>
        /// <param name="cts">A cancellation token source to cancel in the event of a timeout.</param>
        /// <returns>The result of the combined task.</returns>
        /// <remarks>
        /// <para>If the original task completes before the timeout period, the result of the original task is returned.
        /// (This is true no matter how the task completes: successfully, faulted, or cancelled.) If the original
        /// task does not complete before the timeout period, (1) a result with a <see cref="TimeoutException"/> is returned,
        /// (2) if a <paramref name="cts"/> is supplied, it is canceled (presumably to cancel the original task), and
        /// (3) the result of the orginal task is read out, so unobserved exceptions should not occur.</para>
        /// </remarks>
        public static Task<T> TimeoutAfter<T> (this Task<T> task, TimeSpan time, CancellationTokenSource cts = null)
        {
            // For infinite timeout, there isn't anything for us to do.
            if (time == Timeout.InfiniteTimeSpan || task.IsCompleted) return (task);

            // Use a task completion source to allows us to control the task result returned to the caller.
            TaskCompletionSource<T> tcs = new TaskCompletionSource<T>();

            // Create a timer that will activate after the timeout.
            // It's important that this be disposed in all cases!
            Timer timer = new Timer(_ => {

                // If the timer fires, try to ret the result to a TimeoutException.
                // If the task has already completed (see below), this won't succceed.
                if (tcs.TrySetException(new TimeoutException()))
                {
                    if (cts != null)
                    {
                        try
                        {
                            cts.Cancel();
                        }
                        catch (ObjectDisposedException) { }
                    }
                }
            }, null, time, Timeout.InfiniteTimeSpan);

            // Create an unconditional continuation to follow up on the task, however it ends.
            // The actions in the continuation are very quick and do no I/O, so we save scheduler work by executing it
            // synchronously on the completing thread.
            task.ContinueWith(t =>
            {
                // Unconditionallly dispose the timer.
                timer.Dispose();
                switch (t.Status)
                {
                    case TaskStatus.RanToCompletion:
                        tcs.TrySetResult(t.Result);
                        break;
                    case TaskStatus.Faulted:
                        tcs.TrySetException(t.Exception);
                        break;
                    case TaskStatus.Canceled:
                        tcs.TrySetCanceled();
                        break;
                }
             }, TaskContinuationOptions.ExecuteSynchronously);
            return tcs.Task;
        }

    }
}
