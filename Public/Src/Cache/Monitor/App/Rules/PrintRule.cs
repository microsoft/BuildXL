using System;
using System.Threading.Tasks;

namespace BuildXL.Cache.Monitor.App.Rules
{
    internal class PrintRule : IRule
    {
        public string Name => "Print Rule";

        public async Task Run()
        {
            await Task.Delay(1);
            Console.WriteLine("Yey FUN!");
        }
    }
}
