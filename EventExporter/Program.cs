using System;
using System.Globalization;
using System.IO;
using CsvHelper;
using LiteDB;

namespace EventExporter
{
    internal class Program
    {
        public static void Main(string[] args)
        {
            if (args.Length < 2)
            {
                Console.WriteLine("Usage: EventExporter <dbPath> <timespan> [outputFile]");
                Console.WriteLine("Example: EventExporter events.db 24h events.csv");
                return;
            }

            var dbPath = args[0];
            var timespanArg = args[1];
            var outputFile = args.Length > 2 ? args[2] : "events_export.csv";

            if (!TryParseTimeSpan(timespanArg, out var timespan))
            {
                Console.WriteLine("Invalid timespan format. Use <number><h|d|m> (e.g., 24h, 7d, 30m).");
                return;
            }

            var fromTime = DateTime.UtcNow - timespan;
            ExportEventsToCsv(dbPath, fromTime, outputFile);
            Console.WriteLine($"Exported {outputFile} successfully!");

        }

        private static bool TryParseTimeSpan(string input, out TimeSpan result)
        {
            result = TimeSpan.Zero;
            if (input.EndsWith("h") && int.TryParse(input.TrimEnd('h'), out int hours))
                result = TimeSpan.FromHours(hours);
            else if (input.EndsWith("d") && int.TryParse(input.TrimEnd('d'), out int days))
                result = TimeSpan.FromDays(days);
            else if (input.EndsWith("m") && int.TryParse(input.TrimEnd('m'), out int minutes))
                result = TimeSpan.FromMinutes(minutes);
            else
                return false;

            return true;
        }

        private static void ExportEventsToCsv(string dbPath, DateTime fromTime, string outputFile)
        {
            using (var db = new LiteDatabase($"Filename={dbPath};ReadOnly=true;"))
            {
                var events = db.GetCollection<EventLog>("events")
                               .Find(x => x.Timestamp >= fromTime);

                using (var writer = new StreamWriter(outputFile))
                using (var csv = new CsvWriter(writer, CultureInfo.InvariantCulture))
                {
                    // Write CSV header
                    csv.WriteField("Process Id");
                    csv.WriteField("Timestamp");
                    csv.WriteField("Component");
                    csv.WriteField("EventType");
                    csv.WriteField("Data");
                    csv.NextRecord();

                    // Write records
                    foreach (var eventLog in events)
                    {
                        // Format timestamp with milliseconds
                        var timestamp = eventLog.Timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff");

                        // Format data (you could serialize this in a more readable format if needed)
                        var data = eventLog.Data.ToString();

                        csv.WriteField(eventLog.Id.Pid);
                        csv.WriteField(timestamp);
                        csv.WriteField(eventLog.Component);
                        csv.WriteField(eventLog.EventType);
                        csv.WriteField(data);
                        csv.NextRecord();
                    }
                }
            }

        }
    }

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