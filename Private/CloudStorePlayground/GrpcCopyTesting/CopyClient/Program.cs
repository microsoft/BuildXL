using System;
using Grpc.Core;
using System.IO;
using System.IO.Compression;
using System.Threading;
using System.Threading.Tasks;
using Helloworld;

namespace CopyClient
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("Type push <path> to copy a file to the server.");
            Console.WriteLine("Type pull <path> to copy a file from the server.");
            MainAsync(args).Wait();
        }

        static async Task MainAsync (string[] args)
        {
            Channel channel = new Channel($"localhost:{CopyConstants.PortNumber}", ChannelCredentials.Insecure);
            CopyClient client = new CopyClient(channel);

            while (true)
            {
                try
                {
                    Console.Write("*");
                    string command = Console.ReadLine();
                    string[] arguments = command.Split(" ");
                    if (arguments.Length != 2) continue;
                    string name = arguments[1];

                    using (CancellationTokenSource cts = new CancellationTokenSource())
                    {
                        CopyResult result;
                        switch (arguments[0])
                        {
                            case "push":
                                result = await client.Write(name, cts.Token);
                                break;
                            case "pull":
                                result = await client.Read(name, cts.Token);
                                break;
                            default:
                                continue;
                        }
                         Console.WriteLine(result);
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine(e);
                }
            }
        }
    }
}
