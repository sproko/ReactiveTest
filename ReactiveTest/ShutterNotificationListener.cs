using System;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading.Tasks;
using ReactiveTest.MessagesBus;
using ReactiveTest.Shutter.Notifications;

namespace ReactiveTest
{
    public class ShutterNotificationListener : IDisposable
    {
        private readonly MessagesBus.MessageBus _messageBus;
        private readonly EventStore _eventStore;
        private CompositeDisposable _subscriptions = new CompositeDisposable();
        private bool _disposed;

        public ShutterNotificationListener(MessageBus messageBus, EventStore eventStore, string shutterId = null)
        {
            _messageBus = messageBus;
            _eventStore = eventStore;
            _subscriptions.Add(_messageBus.GetEvent<NotificationShutterCommandedStateChanged>()
                                          .SelectMany(cmd => Observable.FromAsync(() => HandleNotificationShutterStateChanged(cmd))) // Fully async processing
                                          .Subscribe());
            _subscriptions.Add(_messageBus.GetEvent<NotificationShutterSensorChanged>()
                                          .SelectMany(cmd => Observable.FromAsync(() => HandleNotificationShutterSensorChanged(cmd))) // Fully async processing
                                          .Subscribe());

        }
        private async Task HandleNotificationShutterStateChanged(NotificationShutterCommandedStateChanged cmd)
        {
            _eventStore.LogEvent($"Shutter_{cmd.ShutterId}",cmd.GetType().Name, cmd);
            Console.WriteLine($"[{Ts.Timestamp}] [Listener]-[Shutter {cmd.ShutterId}] Commanded State is: {cmd.State} ");
        }
        private async Task HandleNotificationShutterSensorChanged(NotificationShutterSensorChanged cmd)
        {
            Console.WriteLine($"[{Ts.Timestamp}] [Listener]-[Shutter {cmd.ShutterId}] Sensor is: {cmd.ActualState} ");
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _subscriptions.Dispose();
        }
    }
}