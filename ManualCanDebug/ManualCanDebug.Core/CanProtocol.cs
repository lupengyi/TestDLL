using System;

namespace ManualCanDebug.Core
{
    public static class CanProtocol
    {
        public static byte[] BuildDutCommunicationInit()
        {
            return new byte[] { 0xFF, 0xFA, 0x55, 0xA9, 0x00, 0x04, 0xFF, 0x00 };
        }

        public static byte[] BuildProductCommunicationTest()
        {
            return new byte[] { 0x02, 0x02, 0x02, 0x02, 0x02, 0x02, 0x02, 0x02 };
        }

        public static byte[] BuildWakeupFrame()
        {
            return new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF };
        }

        public static byte[] BuildAddressRead(uint address)
        {
            byte[] bytes = BitConverter.GetBytes(address);
            return new[] { bytes[3], bytes[2], bytes[1], bytes[0], (byte)0x00, (byte)0x04, (byte)0xFF, (byte)0x00 };
        }

        public static byte[] BuildTableRead(uint address, int length)
        {
            if (length < 1 || length > 255) throw new ArgumentOutOfRangeException(nameof(length));
            byte[] bytes = BitConverter.GetBytes(address);
            return new[] { bytes[3], bytes[2], bytes[1], bytes[0], (byte)0x00, (byte)length, (byte)0xFF, (byte)0x00 };
        }

        public static byte[] BuildTableWrite(uint address, byte[] data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (data.Length < 1 || data.Length > 255) throw new ArgumentOutOfRangeException(nameof(data));
            byte[] bytes = BitConverter.GetBytes(address);
            byte[] command = new byte[8 + data.Length];
            command[0] = bytes[3];
            command[1] = bytes[2];
            command[2] = bytes[1];
            command[3] = bytes[0];
            command[5] = (byte)data.Length;
            Array.Copy(data, 0, command, 8, data.Length);
            return command;
        }

        public static byte[] BuildDutCurrentWrite(uint tableAddress, float maximumRms, float stepCurrent, float holdTime, float frequency, byte newDataFlag)
        {
            byte[] command = new byte[40];
            byte[] address = BitConverter.GetBytes(tableAddress);
            command[0] = address[3];
            command[1] = address[2];
            command[2] = address[1];
            command[3] = address[0];
            command[5] = 0x20;
            Copy(BitConverter.GetBytes(maximumRms * 1.414f), command, 12);
            Copy(BitConverter.GetBytes(stepCurrent), command, 16);
            Copy(BitConverter.GetBytes(holdTime), command, 20);
            Copy(BitConverter.GetBytes(frequency), command, 24);
            command[28] = 0x04;
            command[30] = 0x32;
            command[32] = 0x10;
            command[33] = 0x27;
            command[34] = 0x01;
            command[35] = newDataFlag;
            return command;
        }

        public static byte[] BuildC96MotorControlWrite(uint tableAddress, C96MotorControlCommand settings)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            byte[] payload = new byte[39];
            Copy(BitConverter.GetBytes(settings.StartCurrentRms * 1.414f), payload, 0);
            Copy(BitConverter.GetBytes(settings.TargetCurrentRms * 1.414f), payload, 4);
            Copy(BitConverter.GetBytes(settings.StepPeakAmps), payload, 8);
            Copy(BitConverter.GetBytes(settings.HoldSeconds), payload, 12);
            Copy(BitConverter.GetBytes(settings.OutputFrequencyHz), payload, 16);
            payload[20] = settings.Mode;
            Copy(BitConverter.GetBytes(settings.RampTimeMs), payload, 22);
            Copy(BitConverter.GetBytes(settings.BaseFrequencyHz), payload, 24);
            payload[26] = settings.GateEnable ? (byte)1 : (byte)0;
            payload[27] = 0xFF;
            payload[28] = settings.ResetMotorFaults ? (byte)1 : (byte)0;
            payload[29] = settings.SpeedControlEnable ? (byte)1 : (byte)0;
            Copy(BitConverter.GetBytes(settings.SpeedSetpointRpm), payload, 30);
            payload[34] = settings.VoltageControlEnable ? (byte)1 : (byte)0;
            Copy(BitConverter.GetBytes(settings.VoltageSetpoint), payload, 35);
            return BuildTableWrite(tableAddress, payload);
        }

        public static byte[] BuildC96Tm2MotorControlPayload(C96MotorControlCommand settings)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings)); byte[] command = BuildC96MotorControlWrite(0, settings); byte[] payload = new byte[39]; Array.Copy(command, 8, payload, 0, payload.Length); return payload;
        }

        public static byte[] NormalizeClassicFrame(byte[] data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (data.Length > 8) throw new ArgumentException("A classic CAN frame cannot contain more than eight bytes.", nameof(data));
            byte[] frame = new byte[8];
            Array.Copy(data, frame, data.Length);
            return frame;
        }

        public static int ValidateResolverPolePairs(double polePairs)
        {
            if (double.IsNaN(polePairs) || double.IsInfinity(polePairs))
                throw new ArgumentException("Resolver pole pairs must be a finite integer.", nameof(polePairs));
            if (Math.Abs(polePairs - Math.Round(polePairs)) > 0.000001)
                throw new ArgumentException("Resolver pole pairs must be an integer.", nameof(polePairs));
            if (polePairs < 1 || polePairs > 255)
                throw new ArgumentOutOfRangeException(nameof(polePairs), "Resolver pole pairs must be between 1 and 255.");
            return (int)Math.Round(polePairs);
        }

        private static void Copy(byte[] source, byte[] target, int offset)
        {
            Array.Copy(source, 0, target, offset, source.Length);
        }
    }
}
