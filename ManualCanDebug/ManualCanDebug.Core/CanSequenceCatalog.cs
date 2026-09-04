using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace ManualCanDebug.Core
{
    public static class CanSequenceCatalog
    {
        private static readonly ReadOnlyCollection<CanSequenceStep> Steps = new List<CanSequenceStep>
        {
            new CanSequenceStep("Enter FT Mode", "CAN_APP2FT"),
            new CanSequenceStep("Exit FT Mode", "CAN_FT2APP"),
            new CanSequenceStep("DUT Communication Init", "DUT_ComucationInit"),
            new CanSequenceStep("CAN Communication", "Test_CANCommunication"),
            new CanSequenceStep("Set Speed 700 RPM", "Resolver_SetSpeed", 700),
            new CanSequenceStep("Read Speed 700 RPM", "CAN_ReadSignalValue", 700),
            new CanSequenceStep("Set Speed 3500 RPM", "Resolver_SetSpeed", 3500),
            new CanSequenceStep("Read Speed 3500 RPM", "CAN_ReadSignalValue", 3500),
            new CanSequenceStep("Set Speed 7000 RPM", "Resolver_SetSpeed", 7000),
            new CanSequenceStep("Read Speed 7000 RPM", "CAN_ReadSignalValue", 7000),
            new CanSequenceStep("Set Position 225", "Resolver_SetPosition", 225),
            new CanSequenceStep("Read Position 225", "CAN_ReadSignalValue", 225),
            new CanSequenceStep("Set Position 315", "Resolver_SetPosition", 315),
            new CanSequenceStep("Read Position 315", "CAN_ReadSignalValue", 315),
            new CanSequenceStep("Set DUT Current 0 A", "CAN_SetDUTCurrent", 0.01, 0.01),
            new CanSequenceStep("Read DUT Current 0 A", "CAN_ReadDutCurrent", 0.01),
            new CanSequenceStep("Set DUT Current 100 A", "CAN_SetDUTCurrent", 100),
            new CanSequenceStep("Read DUT Current 100 A", "CAN_ReadDutCurrent", 100),
            new CanSequenceStep("Set DUT Current 200 A", "CAN_SetDUTCurrent", 200),
            new CanSequenceStep("Read DUT Current 200 A", "CAN_ReadDutCurrent", 200),
            new CanSequenceStep("Set DUT Current 300 A", "CAN_SetDUTCurrent", 300),
            new CanSequenceStep("Read DUT Current 300 A", "CAN_ReadDutCurrent", 300),
            new CanSequenceStep("Set DUT Current 400 A", "CAN_SetDUTCurrent", 400),
            new CanSequenceStep("Read DUT Current 400 A", "CAN_ReadDutCurrent", 400),
            new CanSequenceStep("Set DUT Current 500 A", "CAN_SetDUTCurrent", 500),
            new CanSequenceStep("Read DUT Current 500 A", "CAN_ReadDutCurrent", 500),
            new CanSequenceStep("Set DUT Current 600 A", "CAN_SetDUTCurrent", 600),
            new CanSequenceStep("Read DUT Current 600 A", "CAN_ReadDutCurrent", 600),
            new CanSequenceStep("Set DUT Current 700 A", "CAN_SetDUTCurrent", 700),
            new CanSequenceStep("Read DUT Current 700 A", "CAN_ReadDutCurrent", 700),
            new CanSequenceStep("Set DUT Current 800 A", "CAN_SetDUTCurrent", 800),
            new CanSequenceStep("Read DUT Current 800 A", "CAN_ReadDutCurrent", 800),
            new CanSequenceStep("Set DUT Current 900 A", "CAN_SetDUTCurrent", 900),
            new CanSequenceStep("Read DUT Current 900 A", "CAN_ReadDutCurrent", 900)
        }.AsReadOnly();

        public static IReadOnlyList<CanSequenceStep> OrderedSteps
        {
            get { return Steps; }
        }
    }
}
