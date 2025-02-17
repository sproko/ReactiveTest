namespace ReactiveTest.DTO.Shutter
{
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
}