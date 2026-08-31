namespace ManualCanDebug.Core
{
    public sealed class DutPhaseCurrent
    {
        internal DutPhaseCurrent(string name, double instantaneous, double minimum, double maximum)
        {
            Name = name;
            Instantaneous = instantaneous;
            Minimum = minimum;
            Maximum = maximum;
            Rms = (System.Math.Abs(minimum) + maximum) / 2.828;
        }

        public string Name { get; private set; }
        public double Instantaneous { get; private set; }
        public double Minimum { get; private set; }
        public double Maximum { get; private set; }
        public double Rms { get; private set; }
    }
}
