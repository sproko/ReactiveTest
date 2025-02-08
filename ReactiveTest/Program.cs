using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Reactive.Threading.Tasks;
using System.Threading.Tasks;

namespace ReactiveTest
{

    public static class Ts
    {
        public static string Timestamp => DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);
    }

    public class CommandOpenShutter
    {
        public string ShutterId { get; }
        public CommandOpenShutter(string shutterId) => ShutterId = shutterId;
    }

    public class CommandCloseShutter
    {
        public string ShutterId { get; }
        public CommandCloseShutter(string shutterId) => ShutterId = shutterId;
    }

    public class NotificationShutterStateChanged
    {
        public string ShutterId { get; }
        public string State { get; }

        public NotificationShutterStateChanged(ShutterComponent shutter)
        {
            ShutterId = shutter.ShutterId;
            State = shutter.State;
        }
    }

    public class QueryShutterState
    {
        public string ShutterId { get; }
        public TaskCompletionSource<ShutterStateDto> CompletionSource { get; private set; }

        public QueryShutterState(string shutterId)
        {
            ShutterId = shutterId;
            CompletionSource = new TaskCompletionSource<ShutterStateDto>();
        }
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

    public class EventAggregator
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

    public class ShutterComponent: IDisposable
    {
        public string ShutterId { get; }
        public string State { get; private set; }
        private readonly EventAggregator _messageBus;
        private readonly ReplaySubject<string> _stateSubject = new ReplaySubject<string>(1); // Keep last state
        private readonly CompositeDisposable _disposables = new CompositeDisposable();

        public ShutterComponent(string shutterId, EventAggregator messageBus)
        {
            ShutterId = shutterId;
            _messageBus = messageBus;
            State = "Closed";
            _stateSubject.OnNext(State);
            Start();

        }

        private void Start()
        {
            // Subscribe dynamically based on ShutterId
            _disposables.Add(_messageBus.GetEvent<CommandOpenShutter>()
                                          .Where(cmd => cmd.ShutterId == ShutterId)
                                          .Subscribe(HandleCommandOpenShutter));

            _disposables.Add(_messageBus.GetEvent<CommandCloseShutter>()
                                          .Where(cmd => cmd.ShutterId == ShutterId)
                                          .Subscribe(HandleCommandCloseShutter));
            _disposables.Add(_messageBus.GetEvent<QueryShutterState>()
                                          .Where(cmd => cmd.ShutterId == ShutterId)
                                          .Subscribe(HandleQueryShutterState));

            _disposables.Add(_stateSubject);
        }

        private async void HandleCommandOpenShutter(CommandOpenShutter cmd)
        {
            OpenShutterAsync();
        }
        private async void HandleCommandCloseShutter(CommandCloseShutter cmd)
        {
            CloseShutterAsync();
        }

        private void HandleQueryShutterState(QueryShutterState cmd)
        {
            var response = new ShutterStateDto(ShutterId, State);
            cmd.CompletionSource.SetResult(response);
        }

        public async Task OpenShutterAsync()
        {
            Console.WriteLine($"[{Ts.Timestamp}] [Shutter {ShutterId}] Opening...");
            await Task.Delay(1000);
            State = "Opened";
            _stateSubject.OnNext(State);
            _messageBus.Publish(new NotificationShutterStateChanged(this));
        }

        public async Task CloseShutterAsync()
        {
            Console.WriteLine($"[{Ts.Timestamp}] [Shutter {ShutterId}] Closing...");
            await Task.Delay(2000);
            State = "Closed";
            _stateSubject.OnNext(State);
            _messageBus.Publish(new NotificationShutterStateChanged(this)); // Event is published after closing
        }

        // Wait for a specific state change
        public async Task WaitForStateAsync(string expectedState)
        {
            Console.WriteLine($"[{Ts.Timestamp}] [Shutter {ShutterId}] Waiting for state: {expectedState}...");

            using (Observable.Interval(TimeSpan.FromSeconds(0.5))
                             .Subscribe(_ => Console.WriteLine($"[{Ts.Timestamp}] [Shutter {ShutterId}] Still waiting...")))
            {
                await _stateSubject
                     .Where(state => state == expectedState)
                     .FirstAsync()
                     .ToTask();
            }

            Console.WriteLine($"[{Ts.Timestamp}] [Shutter {ShutterId}] Reached state: {expectedState}!");
        }

        public void Dispose()
        {
            if (_disposables.Any()) return;

            _disposables.Dispose();
            Console.WriteLine($"[{Ts.Timestamp}] [Shutter {ShutterId}] Disposed...");
        }
    }

    public class ShutterNotificationListener : IDisposable
    {
        private readonly EventAggregator _messageBus;
        private CompositeDisposable _subscriptions = new CompositeDisposable();
        private bool _disposed;

        public ShutterNotificationListener(EventAggregator messageBus, string shutterId = null)
        {
            _messageBus = messageBus;
            _subscriptions.Add(_messageBus.GetEvent<NotificationShutterStateChanged>()
                                     .SelectMany(cmd => Observable.FromAsync(() => HandleNotificationShutterStateChanged(cmd))) // Fully async processing
                                     .Subscribe());

        }
        private async Task HandleNotificationShutterStateChanged(NotificationShutterStateChanged cmd)
        {
            Console.WriteLine($"[{Ts.Timestamp}] [Listener]-[Shutter {cmd.ShutterId}] State is: {cmd.State} ");
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
            var messageBus = new EventAggregator();
            const string sh1 = "001";
            const string sh2 = "002";
            // Dynamically add shutters
            var shutter1 = new ShutterComponent(sh1, messageBus);
            var shutter2 = new ShutterComponent(sh2, messageBus);

            var listener = new ShutterNotificationListener(messageBus);


            // Publish Commands
            Console.WriteLine($"[{Ts.Timestamp}]  Sending Open Command to Shutter {sh1} ");
            messageBus.Publish(new CommandOpenShutter(sh1));

            Console.WriteLine($"[{Ts.Timestamp}] [MAIN] Waiting for Shutter {sh1} to open");
            await messageBus.WaitForEvent<NotificationShutterStateChanged>(e => e.ShutterId == sh1 && e.State == "Opened");

            Console.WriteLine($"[{Ts.Timestamp}] [MAIN] Shutter {sh1} is OPEN!");

            Console.WriteLine($"[{Ts.Timestamp}]  Sending Open Command to Shutter {sh2} ");
            messageBus.Publish(new CommandOpenShutter(sh2));

            Console.WriteLine($"[{Ts.Timestamp}] [MAIN] Waiting for Shutter {sh2} to open with shutter reference");
            await shutter2.WaitForStateAsync("Opened");

            // Close shutters
            Console.WriteLine($"[{Ts.Timestamp}]  Sending Close Command to Shutters");
            messageBus.Publish(new CommandCloseShutter(sh1));
            messageBus.Publish(new CommandCloseShutter(sh2));

            Console.WriteLine($"[{Ts.Timestamp}] [MAIN] Waiting for Shutter {sh2} to close");
            await messageBus.WaitForEvent<NotificationShutterStateChanged>(e => e.ShutterId == sh2 && e.State == "Closed");
            Console.WriteLine($"[{Ts.Timestamp}] [MAIN] Shutter {sh2} is CLOSED!");
            await shutter1.WaitForStateAsync("Closed");

            var query = new QueryShutterState("001");
            messageBus.Publish(query);

            // Await the response
            var response = await query.CompletionSource.Task;
            Console.WriteLine($"[{Ts.Timestamp}] [MAIN] Shutter {response.ShutterId} is {response.State}");

            Console.ReadKey();
            shutter1.Dispose();
            shutter1 = null;

            Console.WriteLine($"[{Ts.Timestamp}] [MAIN] got rid of shutter 1 {sh1} .. opening shutter {sh2} (should only see Notifications From Shutter 2)");
            await shutter2.OpenShutterAsync();
            Console.WriteLine($"[{Ts.Timestamp}] [MAIN] Shutter 2 {sh2} should be opened with shutter  ");

            Console.ReadKey();
            shutter2.Dispose();
            shutter2 = null;
        }

    }
}