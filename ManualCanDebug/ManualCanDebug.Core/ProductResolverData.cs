using System;
using System.Globalization;
using System.Linq;

namespace ManualCanDebug.Core
{
    public sealed class ProductResolverData
    {
        private ProductResolverData() { }

        public ProductModel Model { get; private set; }
        public uint FirstAddress { get; private set; }
        public uint AddressOffset { get; private set; }
        public int DataLength { get; private set; }
        public uint TableAddress { get; private set; }
        public float PositionDegrees { get; private set; }
        public float VelocityFrequency { get; private set; }
        public bool HasFaultStatus { get; private set; }
        public byte FaultCode { get; private set; }
        public string FaultDescription { get; private set; }
        public string AddressRequestText { get; private set; }
        public string PointerResponseText { get; private set; }
        public string DataRequestText { get; private set; }
        public string RawDataText { get; private set; }
        public string TableAddressText { get { return "0x" + TableAddress.ToString("X8", CultureInfo.InvariantCulture); } }

        public static ProductResolverData Parse(uint firstAddress, uint tableAddress, byte[] addressRequest, byte[] pointerResponse, byte[] dataRequest, byte[] data)
        {
            return Parse(ProductCanProfile.For(ProductModel.C95), firstAddress, tableAddress, addressRequest, pointerResponse, dataRequest, data);
        }

        public static ProductResolverData Parse(ProductCanProfile profile, uint firstAddress, uint tableAddress, byte[] addressRequest, byte[] pointerResponse, byte[] dataRequest, byte[] data)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            if (addressRequest == null) throw new ArgumentNullException(nameof(addressRequest));
            if (pointerResponse == null || pointerResponse.Length < 4) throw new ArgumentException("旋变表指针返回不足4字节。", nameof(pointerResponse));
            if (dataRequest == null) throw new ArgumentNullException(nameof(dataRequest));
            if (data == null || data.Length < profile.ResolverDataLength) throw new ArgumentException("产品旋变数据返回长度不足。", nameof(data));

            bool hasFault = profile.ResolverDataLength > 8;
            byte fault = hasFault ? data[8] : (byte)0;
            return new ProductResolverData
            {
                Model = profile.Model,
                FirstAddress = firstAddress,
                AddressOffset = profile.ResolverDataOffset,
                DataLength = profile.ResolverDataLength,
                TableAddress = tableAddress,
                PositionDegrees = BitConverter.ToSingle(data, 0),
                VelocityFrequency = BitConverter.ToSingle(data, 4),
                HasFaultStatus = hasFault,
                FaultCode = fault,
                FaultDescription = hasFault ? DescribeFault(fault) : "C91表未定义故障状态字节",
                AddressRequestText = HexDataParser.Format(addressRequest),
                PointerResponseText = HexDataParser.Format(pointerResponse.Take(4).ToArray()),
                DataRequestText = HexDataParser.Format(dataRequest),
                RawDataText = HexDataParser.Format(data.Take(profile.ResolverDataLength).ToArray())
            };
        }

        private static string DescribeFault(byte fault)
        {
            switch (fault)
            {
                case 0: return "旋变信号丢失（Loss of Signal）";
                case 1: return "旋变信号降级（Degradation of Signal）";
                case 2: return "旋变跟踪丢失（Loss of Tracking）";
                case 3: return "无故障（No Fault）";
                default: return "未知状态 " + fault.ToString(CultureInfo.InvariantCulture);
            }
        }
    }
}
