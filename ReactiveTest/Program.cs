using System;
using System.Collections.Concurrent;
using System.Diagnostics;
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
        private readonly ReplaySubject<string> _stateSubject = new ReplaySubject<string>(1); // Keep only the last state
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
                                          .Subscribe(HandleCommandOpenShutter,
                                                     ex => Console.WriteLine($"[{Ts.Timestamp}] ERROR CommandOpenShutter {ex.Message}")));
            _disposables.Add(_messageBus.GetEvent<CommandCloseShutter>()
                                          .Where(cmd => cmd.ShutterId == ShutterId)
                                          .Subscribe(HandleCommandCloseShutter,
                                                     ex => Console.WriteLine($"[{Ts.Timestamp}] ERROR CommandCloseShutter {ex.Message}")));
            _disposables.Add(_messageBus.GetEvent<QueryShutterState>()
                                          .Where(cmd => cmd.ShutterId == ShutterId)
                                          .Subscribe(HandleQueryShutterState,
                                                     ex => Console.WriteLine($"[{Ts.Timestamp}] ERROR QueryShutterState {ex.Message}")));
            _disposables.Add(_stateSubject);

            //simulate IO
            _disposables.Add(_messageBus.GetEvent<NotificationShutterCommandedStateChanged>()
                                        .Where(cmd => cmd.ShutterId == ShutterId)
                                        .Subscribe(HandleNotificationShutterCommandedStateChanged));
        }

        private async void HandleNotificationShutterCommandedStateChanged(NotificationShutterCommandedStateChanged notification)
        {
            Console.WriteLine($"[{Ts.Timestamp}] [Shutter {ShutterId}] NotificationShutterCommandedStateChanged {notification.State}");
            switch (notification.State)
            {
                case "Closed":
                    SimulateSensorFeedback("Closed", 1450);
                    break;
                case "Opened":
                    SimulateSensorFeedback("Opened", 2450);
                    break;
            }
        }
        private async Task SimulateSensorFeedback(string state, int delayMs)
        {
            await Task.Delay(delayMs);
            _messageBus.Publish(new NotificationShutterSensorChanged(ShutterId, state));
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
            State = "Opened";
            _stateSubject.OnNext(State);
            _messageBus.Publish(new NotificationShutterCommandedStateChanged(this));
            // Wait for sensor confirmation or timeout
            var confirmed = await WaitForSensorStateAsync(TimeSpan.FromSeconds(3));

            if (!confirmed)
            {
                Console.WriteLine($"[{Ts.Timestamp}] [Shutter {ShutterId}] ERROR: Sensor did not confirm 'Opened' state!");
            }
        }

        public async Task CloseShutterAsync()
        {
            Console.WriteLine($"[{Ts.Timestamp}] [Shutter {ShutterId}] Closing...");
            State = "Closed";
            _stateSubject.OnNext(State);
            _messageBus.Publish(new NotificationShutterCommandedStateChanged(this)); // Event is published after closing
            // Wait for sensor confirmation or timeout
            var confirmed = await WaitForSensorStateAsync(TimeSpan.FromSeconds(3));

            if (!confirmed)
            {
                Console.WriteLine($"[{Ts.Timestamp}] [Shutter {ShutterId}] ERROR: Sensor did not confirm 'Opened' state!");
            }
        }

        // Wait for a specific commanded state change *could add a timeout...
        public async Task WaitForStateAsync(string expectedState)
        {
            var sw = Stopwatch.StartNew();
            Console.WriteLine($"[{Ts.Timestamp}] [Shutter {ShutterId}] Waiting for commanded state: {expectedState}...");

            using (Observable.Interval(TimeSpan.FromSeconds(0.5))
                             .Subscribe(_ => Console.WriteLine($"[{Ts.Timestamp}] [Shutter {ShutterId}] Still waiting...")))
            {
                await _stateSubject
                     .Where(state => state == expectedState)
                     .FirstAsync()
                     .ToTask();
            }

            Console.WriteLine($"[{Ts.Timestamp}] [Shutter {ShutterId}] Reached state: {expectedState}! ET({sw.ElapsedMilliseconds}ms)");
        }
        public async Task<bool> WaitForSensorStateAsync(TimeSpan timeout)
        {
            var sw = Stopwatch.StartNew();
            Console.WriteLine($"[{Ts.Timestamp}] [Shutter {ShutterId}] Waiting for sensor confirmation of {State}");

            try
            {
                var sensorTask = _messageBus.WaitForEvent<NotificationShutterSensorChanged>(e =>
                                                                                    e.ShutterId == ShutterId && e.ActualState == State);
                var timeoutTask = Task.Delay(timeout);
                var completedTask = await Task.WhenAny(sensorTask, timeoutTask);

                if (completedTask == sensorTask)
                {
                    Console.WriteLine($"[{Ts.Timestamp}] [Shutter {ShutterId}] Sensor {State} confirmed! ET({sw.ElapsedMilliseconds}ms)");
                    return true;
                }

                Console.WriteLine($"[{Ts.Timestamp}] [Shutter {ShutterId}] Sensor timeout waiting for: {State}");
                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[{Ts.Timestamp}] [Shutter {ShutterId}] Wait for Sensor exception for {State}, error: {ex.Message}");
                return false;
            }
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
            var messageBus = new EventAggregator();
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
            Console.WriteLine($"[{Ts.Timestamp}] [MAIN] [Shutter {response.ShutterId}] is {response.State}");

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