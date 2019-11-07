namespace BuildXL.Cache.Monitor.App.Notifications
{
    internal interface INotifier<T>
    {
        void Emit(T notification);
    }
}
