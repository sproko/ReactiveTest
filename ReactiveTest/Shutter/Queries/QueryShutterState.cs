using System.Threading.Tasks;

namespace ReactiveTest.Shutter.Queries
{
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
}