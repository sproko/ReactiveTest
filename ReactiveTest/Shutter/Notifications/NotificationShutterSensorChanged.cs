namespace ReactiveTest.Shutter.Notifications
{
    public class NotificationShutterSensorChanged
    {
        public string ShutterId { get; }
        public string ActualState { get; }

        public NotificationShutterSensorChanged(string shutterId, string actualState)
        {
            ShutterId = shutterId;
            ActualState = actualState;
        }
    }
}