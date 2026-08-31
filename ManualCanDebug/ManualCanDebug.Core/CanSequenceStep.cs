namespace ManualCanDebug.Core
{
    public sealed class CanSequenceStep
    {
        public CanSequenceStep(string name, string functionName, double value = 0, double stepCurrent = 20, double holdTime = 10, double frequency = 60)
        {
            Name = name;
            FunctionName = functionName;
            Value = value;
            StepCurrent = stepCurrent;
            HoldTime = holdTime;
            Frequency = frequency;
        }

        public string Name { get; private set; }
        public string FunctionName { get; private set; }
        public double Value { get; private set; }
        public double StepCurrent { get; private set; }
        public double HoldTime { get; private set; }
        public double Frequency { get; private set; }
    }
}
