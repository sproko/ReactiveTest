using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using LiteDB;
using ReactiveTest.MessagesBus;
using ReactiveTest.Shutter;
using ReactiveTest.Shutter.Commands;
using ReactiveTest.Shutter.Queries;

namespace ReactiveTest
{
    public class EventLog
    {
        public ObjectId Id { get; set; } = ObjectId.NewObjectId(); // Auto-generated
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public string Component { get; set; }
        public string EventType { get; set; }
        public BsonDocument Data { get; set; } // Stores any event-specific info

        public override string ToString()
        {
            return $"[{Timestamp}] [{Component}] [{EventType}] - {Data}";
        }
    }
    public class EventStore: IDisposable
    {
        private readonly LiteDatabase _db;
        private readonly ILiteCollection<EventLog> _events;

        public EventStore(string dbPath)
        {
            _db = new LiteDatabase(dbPath);
            _events = _db.GetCollection<EventLog>("events");
            _events.EnsureIndex(x => x.Timestamp);
        }

        public void LogEvent<T>(string component, string eventType, T eventData)
        {
            var bsonData = BsonMapper.Global.ToDocument(eventData);
            if (bsonData == null)
            {
                bsonData = new BsonDocument();
                bsonData.Add(eventType, ConvertToBsonValue(eventData));
            }

            var log = new EventLog
                      {
                          Component = component,
                          EventType = eventType,
                          Data = bsonData,
                      };

            _events.Insert(log);
        }
        private BsonValue ConvertToBsonValue(object value)
        {
            switch (value)
            {
                case string s: return new BsonValue(s);
                case int i: return new BsonValue(i);
                case bool b: return new BsonValue(b);
                case double d: return new BsonValue(d);
                case DateTime dt: return new BsonValue(dt);
                // Handle other types as needed
                default: return new BsonValue(value?.ToString()); // Default conversion for unknown types
            }
        }

        public IEnumerable<EventLog> GetEvents(DateTime? from = null, DateTime? to = null)
        {
            return _events.Find(x =>
                                    (!from.HasValue || x.Timestamp >= from) &&
                                    (!to.HasValue || x.Timestamp <= to));
        }

        public void Dispose()
        {
            _db?.Dispose();
        }
    }
    internal static class Program
    {
        public static async Task Main(string[] args)
        {
            var eventStore = new EventStore("eventStore.db");
            eventStore.LogEvent("APPLICATION","Started","data or something");
            var messageBus = new MessageBus();
            const string sh1 = "001";
            const string sh2 = "002";
            // Dynamically add shutters
            var shutter1 = new ShutterComponent(sh1, messageBus, eventStore);
            var shutter2 = new ShutterComponent(sh2, messageBus, eventStore);

            var listener = new ShutterNotificationListener(messageBus, eventStore);

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

            var t = eventStore.GetEvents();
            foreach (var eventItem in t)
            {
                Console.WriteLine(eventItem);
            }

            eventStore.Dispose();
        }

    }
}