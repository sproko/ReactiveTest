using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Reactive.Threading.Tasks;
using System.Threading.Tasks;
using ReactiveTest.Shutter;
using ReactiveTest.Shutter.Commands;
using ReactiveTest.Shutter.Notifications;
using ReactiveTest.Shutter.Queries;

namespace ReactiveTest
{

    public static class Ts
    {
        public static string Timestamp => DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);
    }

    public class ShutterStateDto
    {
        public string ShutterId { get; }
        public string State { get; }

        public ShutterStateDto(string shutterId, string state)
        {
            ShutterId = shutterId;
            State = state;
        }
    }

    public class EventBus
    {
        private readonly ConcurrentDictionary<Type, object> _subjects = new ConcurrentDictionary<Type, object>();

        public void Publish<T>(T eventMessage)
        {
            if (_subjects.TryGetValue(typeof(T), out var subjectObj) && subjectObj is Subject<T> subject)
            {
                subject.OnNext(eventMessage);
            }
        }

        public IObservable<T> GetEvent<T>()
        {
            var subject = (Subject<T>)_subjects.GetOrAdd(typeof(T), _ => new Subject<T>());
            return subject.AsObservable();
        }

        public async Task<T> WaitForEvent<T>(Func<T, bool> predicate = null)
        {
            if (predicate == null)
                predicate = _ => true; // Default to accepting any event of type T

            return await GetEvent<T>().Where(predicate).FirstAsync().ToTask();
        }
    }

    public class ShutterNotificationListener : IDisposable
    {
        private readonly EventBus _messageBus;
        private CompositeDisposable _subscriptions = new CompositeDisposable();
        private bool _disposed;

        public ShutterNotificationListener(EventBus messageBus, string shutterId = null)
        {
            _messageBus = messageBus;
            _subscriptions.Add(_messageBus.GetEvent<NotificationShutterCommandedStateChanged>()
                                     .SelectMany(cmd => Observable.FromAsync(() => HandleNotificationShutterStateChanged(cmd))) // Fully async processing
                                     .Subscribe());
            _subscriptions.Add(_messageBus.GetEvent<NotificationShutterSensorChanged>()
                                     .SelectMany(cmd => Observable.FromAsync(() => HandleNotificationShutterSensorChanged(cmd))) // Fully async processing
                                     .Subscribe());

        }
        private async Task HandleNotificationShutterStateChanged(NotificationShutterCommandedStateChanged cmd)
        {
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



    internal static class Program
    {
        public static async Task Main(string[] args)
        {
            var messageBus = new EventBus();
            const string sh1 = "001";
            const string sh2 = "002";
            // Dynamically add shutters
            var shutter1 = new ShutterComponent(sh1, messageBus);
            var shutter2 = new ShutterComponent(sh2, messageBus);

            var listener = new ShutterNotificationListener(messageBus);

            // Publish Commands
            Console.WriteLine($"[{Ts.Timestamp}][MAIN] Sending Open Command to Shutter {sh1} ");
            messageBus.Publish(new CommandOpenShutter(sh1));

            Console.WriteLine($"[{Ts.Timestamp}][MAIN] Sending Open Command to Shutter {sh2} ");
            messageBus.Publish(new CommandOpenShutter(sh2));

            Console.WriteLine($"[{Ts.Timestamp}][MAIN] Waiting for Shutter {sh2} to open with shutter reference");
            await shutter2.WaitForSensorStateAsync(TimeSpan.FromSeconds(3));

            // Close shutters
            Console.WriteLine($"[{Ts.Timestamp}][MAIN] Sending Close Command to Shutters");
            messageBus.Publish(new CommandCloseShutter(sh1));
            messageBus.Publish(new CommandCloseShutter(sh2));

            Console.WriteLine($"[{Ts.Timestamp}][MAIN] Waiting for Shutter {sh1} to close");
            await shutter2.WaitForSensorStateAsync(TimeSpan.FromSeconds(3));

            var query = new QueryShutterState("001");
            messageBus.Publish(query);
            // Await the response
            var response = await query.CompletionSource.Task;
            Console.WriteLine($"[{Ts.Timestamp}][MAIN] [Shutter {response.ShutterId}] is {response.State}");

            Console.ReadKey();
            shutter1.Dispose();
            shutter1 = null;

            Console.WriteLine($"[{Ts.Timestamp}][MAIN] got rid of shutter 1 {sh1} .. opening shutter {sh2} (should only see Notifications From Shutter 2)");
            await shutter2.OpenShutterAsync();
            Console.WriteLine($"[{Ts.Timestamp}][MAIN] Shutter 2 {sh2} should be opened with shutter  ");

            Console.ReadKey();
            shutter2.Dispose();
            shutter2 = null;
        }

    }
}