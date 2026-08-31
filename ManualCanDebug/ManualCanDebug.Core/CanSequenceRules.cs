using System;

namespace ManualCanDebug.Core
{
    public static class CanSequenceRules
    {
        public static bool RequiresPreCurrentGuide(CanSequenceStep step)
        {
            if (step == null) throw new ArgumentNullException(nameof(step));
            return step.FunctionName == "CAN_SetDUTCurrent";
        }
    }
}
