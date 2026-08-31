using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace ManualCanDebug.Core
{
    public sealed class DutCurrentResult
    {
        private DutCurrentResult(IList<DutPhaseCurrent> phases, byte[] motorStatus)
        {
            Phases = new ReadOnlyCollection<DutPhaseCurrent>(phases);
            MotorStatus = motorStatus;
            MotorStatusText = HexDataParser.Format(motorStatus);
            MotorStatusInfo = MotorStatusInfo.Parse(motorStatus);
            MotorStatusDescription = MotorStatusInfo.Summary;
        }

        public IReadOnlyList<DutPhaseCurrent> Phases { get; private set; }
        public byte[] MotorStatus { get; private set; }
        public string MotorStatusText { get; private set; }
        public string MotorStatusDescription { get; private set; }
        public MotorStatusInfo MotorStatusInfo { get; private set; }

        public static DutCurrentResult Parse(byte[] currentData, byte[] motorStatus)
        {
            if (currentData == null) throw new ArgumentNullException(nameof(currentData));
            if (currentData.Length < 36) throw new ArgumentException("Product current result must contain nine floats.", nameof(currentData));
            if (motorStatus == null) throw new ArgumentNullException(nameof(motorStatus));

            float[] values = new float[9];
            for (int i = 0; i < values.Length; i++)
            {
                values[i] = BitConverter.ToSingle(currentData, i * 4);
            }

            return new DutCurrentResult(
                new List<DutPhaseCurrent>
                {
                    new DutPhaseCurrent("A", values[0], values[3], values[6]),
                    new DutPhaseCurrent("B", values[1], values[4], values[7]),
                    new DutPhaseCurrent("C", values[2], values[5], values[8])
                },
                (byte[])motorStatus.Clone());
        }

    }
}
