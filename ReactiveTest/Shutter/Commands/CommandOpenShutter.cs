namespace ReactiveTest.Shutter.Commands
{
    public class CommandOpenShutter
    {
        public string ShutterId { get; }
        public CommandOpenShutter(string shutterId) => ShutterId = shutterId;
    }
}