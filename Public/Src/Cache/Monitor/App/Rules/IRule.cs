using System.Threading.Tasks;
using BuildXL.Cache.Monitor.App.Notifications;

namespace BuildXL.Cache.Monitor.App.Rules
{
    internal interface IRule
    {
        string Name { get; }

        Task Run();
    }
}
