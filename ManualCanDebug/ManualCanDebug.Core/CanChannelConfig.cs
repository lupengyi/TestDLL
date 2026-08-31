namespace ManualCanDebug.Core
{
    public sealed class CanChannelConfig
    {
        public CanChannelConfig(string name, ushort channel, string dbcFile)
        {
            Name = name;
            Channel = channel;
            DbcFile = dbcFile;
            DeviceType = 52;
            BaudRate = 500000;
            FdBaudRate = "500000,2000000";
            UseCanFd = false;
            Ip = "192.166.6.10";
            Port = 8000;
        }

        public string Name { get; private set; }
        public ushort Channel { get; private set; }
        public uint DeviceType { get; set; }
        public uint BaudRate { get; set; }
        public string FdBaudRate { get; set; }
        public bool UseCanFd { get; set; }
        public string Ip { get; set; }
        public int Port { get; set; }
        public string DbcFile { get; private set; }

        public static CanChannelConfig ProductDefaults()
        {
            return new CanChannelConfig("Product CAN", 2, "Flywheel_900A_Z405.dbc");
        }

        public static CanChannelConfig ResolverDefaults()
        {
            return new CanChannelConfig("Resolver CAN", 1, "Resolver.dbc");
        }

        public static CanChannelConfig AuxiliaryDefaults()
        {
            return new CanChannelConfig("C95/C96 DCDC/Auxiliary CAN", 0, "C95C96Auxiliary.dbc");
        }
    }
}
