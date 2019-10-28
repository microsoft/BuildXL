using System.Threading.Tasks;
using BuildXL.Cache.Monitor.App.Notifications;

namespace BuildXL.Cache.Monitor.App.Rules
{
    /// <summary>
    /// Basic interface for a rule. Rules are run in a single-threaded fashion.
    /// </summary>
    internal interface IRule
    {
        /// <summary>
        /// Unique identifier for a given rule instance. It is expected to remain the same across program runs, and
        /// depend only on configuration parameters.
        /// </summary>
        string Identifier { get; }

        Task Run();
    }
}
