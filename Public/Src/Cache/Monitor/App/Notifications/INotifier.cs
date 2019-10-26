namespace BuildXL.Cache.Monitor.App.Notifications
{
    internal interface INotifier
    {
        void Emit(Notification notification);
    }
}
