namespace ReactiveTest.Shutter.Notifications
{
    public class NotificationShutterCommandedStateChanged
    {
        public string ShutterId { get; }
        public string State { get; }

        public NotificationShutterCommandedStateChanged(ShutterComponent shutter)
        {
            ShutterId = shutter.ShutterId;
            State = shutter.State;
        }
    }
}