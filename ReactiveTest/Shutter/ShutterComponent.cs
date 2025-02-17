using System;
using System.Diagnostics;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Reactive.Threading.Tasks;
using System.Threading.Tasks;
using ReactiveTest.DTO.Shutter;
using ReactiveTest.MessagesBus;
using ReactiveTest.Shutter.Commands;
using ReactiveTest.Shutter.Notifications;
using ReactiveTest.Shutter.Queries;

namespace ReactiveTest.Shutter
{
    public class ShutterComponent: IDisposable
    {
        private string Name => $"Shutter_{ShutterId}";
        public string ShutterId { get; }
        public string State { get; private set; }
        private readonly MessagesBus.MessageBus _messageBus;
        private readonly EventStore _eventStore;
        private readonly ReplaySubject<string> _stateSubject = new ReplaySubject<string>(1); // Keep only the last state
        private readonly CompositeDisposable _disposables = new CompositeDisposable();

        public ShutterComponent(string shutterId, MessageBus messageBus, EventStore eventStore)
        {
            ShutterId = shutterId;
            _messageBus = messageBus;
            _eventStore = eventStore;
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
            _eventStore.LogEvent(Name, cmd.GetType().Name, cmd);
            OpenShutterAsync();
        }
        private async void HandleCommandCloseShutter(CommandCloseShutter cmd)
        {
            _eventStore.LogEvent(Name, cmd.GetType().Name, cmd);
            CloseShutterAsync();
        }

        private void HandleQueryShutterState(QueryShutterState cmd)
        {
            var response = new ShutterStateDto(ShutterId, State);
            cmd.CompletionSource.SetResult(response);
        }

        public async Task OpenShutterAsync()
        {
            var sw = Stopwatch.StartNew();
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
            _eventStore.LogEvent(Name, nameof(OpenShutterAsync), sw.Elapsed);
        }

        public async Task CloseShutterAsync()
        {
            var sw = Stopwatch.StartNew();
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
            _eventStore.LogEvent(Name, nameof(CloseShutterAsync), sw.Elapsed);

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
}