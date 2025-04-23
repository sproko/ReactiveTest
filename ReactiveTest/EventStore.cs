using System;
using System.Collections.Generic;
using LiteDB;

namespace ReactiveTest
{
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
}