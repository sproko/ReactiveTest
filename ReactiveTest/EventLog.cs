using System;
using LiteDB;

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
}