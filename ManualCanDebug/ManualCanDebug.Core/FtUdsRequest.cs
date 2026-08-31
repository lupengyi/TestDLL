namespace ManualCanDebug.Core
{
    public sealed class FtUdsRequest
    {
        public FtUdsRequest(string request, string expected)
        {
            Request = request;
            Expected = expected;
        }

        public string Request { get; private set; }
        public string Expected { get; private set; }
    }
}
