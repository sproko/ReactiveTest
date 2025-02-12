namespace ReactiveTest.Shutter.Commands
{
    public class CommandCloseShutter
    {
        public string ShutterId { get; }
        public CommandCloseShutter(string shutterId) => ShutterId = shutterId;
    }
}