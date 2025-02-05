using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Reactive.Threading.Tasks;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ReactiveTest
{


    // Define the messages (commands and events)
    public static class TS
    {
        public static string timestamp => DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);
    }

    public class OpenShutterCommand
    {
        public string ShutterId { get; }
        public OpenShutterCommand(string shutterId) => ShutterId = shutterId;
    }

    public class CloseShutterCommand
    {
        public string ShutterId { get; }
        public CloseShutterCommand(string shutterId) => ShutterId = shutterId;
    }

    public class ShutterStateChanged
    {
        public string ShutterId { get; }
        public string State { get; }

        public ShutterStateChanged(ShutterComponent shutter)
        {
            ShutterId = shutter.ShutterId;
            State = shutter.State;
        }
    }

    public class ShutterClosedEvent
    {
        public string ShutterId { get; }
        public ShutterClosedEvent(string shutterId) => ShutterId = shutterId;
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


    // Example: Shutter Component with dynamic command/event handling
    public class ShutterComponent
    {
        public string ShutterId { get; }
        private readonly EventAggregator _messageBus;
        private readonly ReplaySubject<string> _stateSubject = new ReplaySubject<string>(1); // Keep last state
        public string State { get; private set; }

        public ShutterComponent(string shutterId, EventAggregator messageBus)
        {
            ShutterId = shutterId;
            _messageBus = messageBus;
            State = "Closed";
            _stateSubject.OnNext(State);

            StartListening();
        }

        private void StartListening()
        {
            // Subscribe dynamically based on ShutterId
            _messageBus.GetEvent<OpenShutterCommand>()
                       .Where(cmd => cmd.ShutterId == ShutterId)
                       .Subscribe(cmd => OpenShutterAsync());

            _messageBus.GetEvent<CloseShutterCommand>()
                       .Where(cmd => cmd.ShutterId == ShutterId)
                       .Subscribe(cmd => CloseShutterAsync());
        }

        public async Task OpenShutterAsync()
        {
            Console.WriteLine($"[{TS.timestamp}] [Shutter {ShutterId}] Opening...");
            await Task.Delay(1000);
            State = "Opened";
            _stateSubject.OnNext(State);
            _messageBus.Publish(new ShutterStateChanged(this));
        }

        public async Task CloseShutterAsync()
        {
            Console.WriteLine($"[{TS.timestamp}] [Shutter {ShutterId}] Closing...");
            await Task.Delay(2000);
            State = "Closed";
            _stateSubject.OnNext(State);
            _messageBus.Publish(new ShutterStateChanged(this)); // Event is published after closing
        }

        // Wait for a specific state change
        public async Task WaitForStateAsync(string expectedState)
        {
            Console.WriteLine($"[{TS.timestamp}] [Shutter {ShutterId}] Waiting for state: {expectedState}...");

            using (Observable.Interval(TimeSpan.FromSeconds(0.5))
                             .Subscribe(_ => Console.WriteLine($"[{TS.timestamp}] [Shutter {ShutterId}] Still waiting...")))
            {
                await _stateSubject
                     .Where(state => state == expectedState)
                     .FirstAsync()
                     .ToTask();
            }

            Console.WriteLine($"[{TS.timestamp}] [Shutter {ShutterId}] Reached state: {expectedState}!");
        }
    }

    public class ShutterListener
    {
        private readonly EventAggregator _messageBus;

        public ShutterListener(EventAggregator messageBus, string shutterId = null)
        {
            _messageBus = messageBus;

            //order not guaranteed
            // _messageBus.GetEvent<ShutterStateChanged<ShutterComponent>>()
            //            .Where(evt => shutterId == null || evt.Shutter.ShutterId == shutterId)
            //            .ObserveOn(TaskPoolScheduler.Default)
            //            .Subscribe(StateChangedListener);

            _messageBus.GetEvent<ShutterStateChanged>()
                       .SelectMany(cmd => Observable.FromAsync(() => StateChangedListener(cmd))) // Fully async processing
                       .Subscribe();

        }
        private async Task StateChangedListener(ShutterStateChanged cmd)
        {
            Console.WriteLine($"[{TS.timestamp}] [Listener]-[Shutter {cmd.ShutterId}] State is: {cmd.State} ");
            await Task.Delay(TimeSpan.FromMilliseconds(5));
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

            var listener = new ShutterListener(messageBus);


            // Publish Commands
            Console.WriteLine($"[{TS.timestamp}]  Sending Open Command to Shutter {sh1} ");
            messageBus.Publish(new OpenShutterCommand(sh1));
            Console.WriteLine($"[{TS.timestamp}] [MAIN] Waiting for Shutter {sh1} to open");
            await messageBus.WaitForEvent<ShutterStateChanged>(e => e.ShutterId == sh1 && e.State == "Opened");
            Console.WriteLine($"[{TS.timestamp}] [MAIN] Shutter {sh1} is OPEN!");

            messageBus.Publish(new OpenShutterCommand(sh2));

            await shutter2.WaitForStateAsync("Opened");

            // Close shutters
            messageBus.Publish(new CloseShutterCommand(sh1));
            messageBus.Publish(new CloseShutterCommand(sh2));
            Console.WriteLine($"[{TS.timestamp}] [MAIN] Waiting for Shutter {sh2} to close");
            await messageBus.WaitForEvent<ShutterStateChanged>(e => e.ShutterId == sh2 && e.State == "Closed");
            Console.WriteLine($"[{TS.timestamp}] [MAIN] Shutter {sh2} is CLOSED!");

            await shutter1.WaitForStateAsync("Closed");

            Console.ReadKey();
        }

    }
}