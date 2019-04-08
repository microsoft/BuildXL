using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Test.BuildXL.FrontEnd.Ninja.Infrastructure
{
    /// <summary>
    /// A Ninja spec file and its intended targets
    /// </summary>
    public readonly struct NinjaSpec
    {
        ///<nodoc/>
        public string Content { get; }
        
        ///<nodoc/>
        public string[] Targets { get; }

        ///<nodoc/>
        public NinjaSpec(string content, string[] targets)
        {
            Content = content;
            Targets = targets;
        }
    }
}
