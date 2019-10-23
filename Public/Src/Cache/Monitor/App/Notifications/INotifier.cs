using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BuildXL.Cache.Monitor.App.Notifications
{
    internal interface INotifier
    {
        void Emit(Notification notification);
    }
}
