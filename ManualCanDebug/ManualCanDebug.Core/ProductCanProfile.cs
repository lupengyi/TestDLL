using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace ManualCanDebug.Core
{
    public sealed class ProductCanProfile
    {
        private static readonly ProductCanProfile C91Profile = new ProductCanProfile(
            ProductModel.C91,
            "C91（原 91 产品）",
            0x60,
            0x64,
            9,
            0x48,
            9,
            new List<FtUdsRequest>
            {
                new FtUdsRequest("10 03", "50 03"),
                new FtUdsRequest("27 01", "67 01"),
                new FtUdsRequest("27 02 FF FF FF FF", "67 02"),
                new FtUdsRequest("2E EE EE AA 55 AA 55", "6E EE"),
                new FtUdsRequest("11 01", "61 01")
            },
            false,
            0x70,
            0x74,
            0x01,
            new List<PreCurrentReadItem>
            {
                new PreCurrentReadItem("产品母线高压", 0x00, 184, 4, "V", "C91 FT_Analog_Inputs / HVDC"),
                new PreCurrentReadItem("Battery 电压", 0x00, 128, 4, "V", "C91 FT_Analog_Inputs / Battery"),
                new PreCurrentReadItem("PSR 电压", 0x00, 84, 4, "V", "C91 FT_Analog_Inputs / PSR"),
                new PreCurrentReadItem("HVDC_OV_FLT", 0x18, 1, 1, string.Empty, "C91 FT_Discrete_Inputs / HVDC_OV_FLT^", true),
                new PreCurrentReadItem("OV_FLT", 0x18, 19, 1, string.Empty, "C91 FT_Discrete_Inputs / OV_FLT^", true),
                new PreCurrentReadItem("产品板温", 0x00, 68, 4, "℃", "C91 FT_Analog_Inputs / Board Temp")
            });

        private static readonly ProductCanProfile C95Profile = new ProductCanProfile(
            ProductModel.C95,
            "C95（新 95 产品）",
            0x58,
            0x5C,
            8,
            0x44,
            9,
            new List<FtUdsRequest>(),
            true,
            0x6C,
            0x70,
            0xFF,
            new List<PreCurrentReadItem>
            {
                new PreCurrentReadItem("产品母线高压", 0x00, 44, 4, "V", "FT_Analog_Inputs / HVDC_SENSE_AI"),
                new PreCurrentReadItem("Battery 电压", 0x00, 80, 4, "V", "FT_Analog_Inputs / SCALED_VBATT_AI"),
                new PreCurrentReadItem("HVDC_OV_FLT", 0x18, 13, 1, string.Empty, "FT_Discrete_Inputs / HVDC_OV_FLT^", true),
                new PreCurrentReadItem("OV_FLT", 0x18, 14, 1, string.Empty, "FT_Discrete_Inputs / OV_FLT^", true),
                new PreCurrentReadItem("产品板温", 0x00, 192, 4, "℃", "FT_Analog_Inputs / BOARD_TEMP_AI")
            });

        private static readonly ProductCanProfile C92Profile = new ProductCanProfile(
            ProductModel.C92,
            "C92（双主驱产品）",
            0x68,
            0x6C,
            10,
            0x44,
            9,
            new List<FtUdsRequest>(),
            false,
            0x78,
            0x7C,
            0xFF,
            new List<PreCurrentReadItem>
            {
                new PreCurrentReadItem("产品母线高压", 0x00, 32, 4, "V", "C92 FT_Analog_Inputs / HVDC_SENSE_AI"),
                new PreCurrentReadItem("Battery 电压", 0x00, 240, 4, "V", "C92 FT_Analog_Inputs / SCALED_VBATT_AI"),
                new PreCurrentReadItem("HVDC_OV_FLT", 0x18, 0, 1, string.Empty, "C92 FT_Discrete_Inputs / HVDC_OV_FLT^", true),
                new PreCurrentReadItem("OV_FLT", 0x18, 100, 1, string.Empty, "C92 FT_Discrete_Inputs / OV_FLT^", true),
                new PreCurrentReadItem("产品板温", 0x00, 20, 4, "℃", "C92 FT_Analog_Inputs / BOARD_TEMP_AI")
            });

        private static readonly ProductCanProfile C96Profile = new ProductCanProfile(
            ProductModel.C96,
            "C96（双驱产品）",
            0x68,
            0x6C,
            10,
            0x44,
            9,
            new List<FtUdsRequest>(),
            false,
            0x78,
            0x7C,
            0xFF,
            new List<PreCurrentReadItem>
            {
                new PreCurrentReadItem("产品母线高压", 0x00, 32, 4, "V", "C96 FT_Analog_Inputs / HVDC_SENSE_AI"),
                new PreCurrentReadItem("Battery 电压", 0x00, 240, 4, "V", "C96 FT_Analog_Inputs / SCALED_VBATT_AI"),
                new PreCurrentReadItem("HVDC_OV_FLT", 0x18, 0, 1, string.Empty, "C96 FT_Discrete_Inputs / HVDC_OV_FLT^", true),
                new PreCurrentReadItem("OV_FLT", 0x18, 100, 1, string.Empty, "C96 FT_Discrete_Inputs / OV_FLT^", true),
                new PreCurrentReadItem("产品板温", 0x00, 20, 4, "℃", "C96 FT_Analog_Inputs / BOARD_TEMP_AI")
            });

        private ProductCanProfile(
            ProductModel model,
            string displayName,
            uint motorControlOffset,
            uint motorStatusOffset,
            int motorStatusLength,
            uint resolverDataOffset,
            int resolverDataLength,
            IList<FtUdsRequest> ftEntryRequests,
            bool supportsLocatorPages,
            uint currentSenseCommandOffset,
            uint currentSenseResultOffset,
            byte newDataFlag,
            IList<PreCurrentReadItem> preCurrentReadItems)
        {
            Model = model;
            DisplayName = displayName;
            MotorControlOffset = motorControlOffset;
            MotorStatusOffset = motorStatusOffset;
            MotorStatusLength = motorStatusLength;
            ResolverDataOffset = resolverDataOffset;
            ResolverDataLength = resolverDataLength;
            FtEntryRequests = new ReadOnlyCollection<FtUdsRequest>(ftEntryRequests);
            SupportsLocatorPages = supportsLocatorPages;
            CurrentSenseCommandOffset = currentSenseCommandOffset;
            CurrentSenseResultOffset = currentSenseResultOffset;
            NewDataFlag = newDataFlag;
            PreCurrentReadItems = new ReadOnlyCollection<PreCurrentReadItem>(preCurrentReadItems);
        }

        public ProductModel Model { get; private set; }
        public string DisplayName { get; private set; }
        public uint MotorControlOffset { get; private set; }
        public uint MotorStatusOffset { get; private set; }
        public int MotorStatusLength { get; private set; }
        public uint ResolverDataOffset { get; private set; }
        public int ResolverDataLength { get; private set; }
        public IReadOnlyList<FtUdsRequest> FtEntryRequests { get; private set; }
        public bool SupportsLocatorPages { get; private set; }
        public uint CurrentSenseCommandOffset { get; private set; }
        public uint CurrentSenseResultOffset { get; private set; }
        public byte NewDataFlag { get; private set; }
        public IReadOnlyList<PreCurrentReadItem> PreCurrentReadItems { get; private set; }
        public bool IsDualDrive { get { return Model == ProductModel.C92 || Model == ProductModel.C96; } }
        public bool SupportsAuxiliary { get { return Model == ProductModel.C95 || Model == ProductModel.C96; } }

        public static ProductCanProfile For(ProductModel model)
        {
            switch (model)
            {
                case ProductModel.C91: return C91Profile;
                case ProductModel.C92: return C92Profile;
                case ProductModel.C95: return C95Profile;
                case ProductModel.C96: return C96Profile;
                default: throw new ArgumentOutOfRangeException(nameof(model));
            }
        }

        public override string ToString()
        {
            return DisplayName;
        }
    }
}
