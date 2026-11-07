// StarMon: hardware monitoring and control
// Portions copyright © 2023-2024 Piotr Szczepański (GPL-3.0)

using System;
using StarMon.Library;

namespace StarMon.Test {

    // Exercises the conversion helpers. These sit under the command-line
    // parser and the configuration reader, so a wrong answer here shows up as
    // a setting quietly not taking effect rather than as an error.
    [TestSuite(Order = 10)]
    public static class TestConv {

        public static void Run() {

            SelfTest.Group("Conversions");

            TestBool();
            TestByte();
            TestWord();
            TestConstrained();
            TestColor();
            TestBits();

            SelfTest.Group("Processor power registers");

            TestPowerLimitDecodes();

        }

        // -Msr is how a package held at a fixed wattage gets explained, so a
        // field read from the wrong bits would send the explanation the wrong
        // way. The register values are built from the figures they hold.
        private static void TestPowerLimitDecodes() {

            // 1/8 W, 1/16384 J and 1/1024 s: the units Intel client parts use
            StarMon.AppCli.CliOp.RaplUnits units = StarMon.AppCli.CliOp.DecodeUnits(0x000A0E03);
            SelfTest.Check(units.Watts == 0.125, "the power unit is an eighth of a watt");
            SelfTest.Check(units.Joules == 1.0 / 16384, "the energy unit is 1/16384 J");
            SelfTest.Check(units.Seconds == 1.0 / 1024, "the time unit is 1/1024 s");

            // 45 W, enabled, clamped, 28 s (2^14 × 1.75 / 1024)
            uint half = 360u | 0x8000u | 0x10000u | (14u << 17) | (3u << 22);
            StarMon.AppCli.CliOp.LimitHalf limit = StarMon.AppCli.CliOp.DecodeLimitHalf(half, units);
            SelfTest.Check(limit.Watts == 45, "360 eighths of a watt read as 45 W");
            SelfTest.Check(limit.Enabled, "the enable bit is read");
            SelfTest.Check(limit.Clamped, "the clamp bit is read");
            SelfTest.Check(Math.Abs(limit.WindowSeconds - 28) < 1e-9, "the time window reads 28 s");

            limit = StarMon.AppCli.CliOp.DecodeLimitHalf(480u, units);
            SelfTest.Check(limit.Watts == 60 && !limit.Enabled,
                "a limit without its enable bit still shows its wattage, as disabled");

            SelfTest.Equal("none", StarMon.AppCli.CliOp.DescribeLimitReasons(0),
                "no status bit names no reason");
            SelfTest.Equal("PL1 (package/platform)",
                StarMon.AppCli.CliOp.DescribeLimitReasons(1u << 10),
                "bit 10 is the PL1 limit");
            SelfTest.Equal("Thermal, Other (EDP)",
                StarMon.AppCli.CliOp.DescribeLimitReasons((1u << 1) | (1u << 8)),
                "several reasons are listed in bit order");

        }

        private static void TestBool() {

            bool value;

            SelfTest.Check(Conv.GetBool("true", out value) && value,
                "\"true\" parses as true");
            SelfTest.Check(Conv.GetBool("false", out value) && !value,
                "\"false\" parses as false");
            SelfTest.Check(Conv.GetBool("1", out value) && value,
                "\"1\" parses as true");
            SelfTest.Check(Conv.GetBool("0", out value) && !value,
                "\"0\" parses as false");
            SelfTest.Check(!Conv.GetBool("perhaps", out value),
                "a word that is neither reports failure");

            // The configuration writer emits these two spellings, so they have
            // to survive a round trip through the reader
            SelfTest.Check(Conv.GetBool("true", out value) && value,
                "the spelling the configuration writer emits for true parses");
            SelfTest.Check(Conv.GetBool("false", out value) && !value,
                "the spelling the configuration writer emits for false parses");

        }

        private static void TestByte() {

            byte value;

            SelfTest.Check(Conv.GetByte("42", out value) && value == 42,
                "a decimal byte parses");
            SelfTest.Check(Conv.GetByte("0x2A", out value) && value == 0x2A,
                "a hexadecimal byte parses");
            SelfTest.Check(Conv.GetByte("0b00101010", out value) && value == 42,
                "a binary byte parses");
            SelfTest.Check(Conv.GetByte("255", out value) && value == 255,
                "the largest byte parses");
            SelfTest.Check(!Conv.GetByte("256", out value),
                "a value past the end of the range reports failure");
            SelfTest.Check(!Conv.GetByte("", out value),
                "an empty string reports failure");

        }

        private static void TestWord() {

            ushort value;

            SelfTest.Check(Conv.GetWord("1000", out value) && value == 1000,
                "a decimal word parses");
            SelfTest.Check(Conv.GetWord("0xFFFF", out value) && value == 0xFFFF,
                "the largest word parses");
            SelfTest.Check(!Conv.GetWord("65536", out value),
                "a value past the end of the range reports failure");

            // The fan and EC settings are all stored as words, so this is the
            // path every numeric configuration value takes
            SelfTest.Check(Conv.GetWord("1000", out value) && value == 1000,
                "the stock EC mutex timeout survives a parse");
            SelfTest.Check(Conv.GetWord("56", out value) && value == 56,
                "the stock fan ceiling survives a parse");

        }

        private static void TestConstrained() {

            SelfTest.Equal(5, Conv.GetConstrained(5, 0, 10),
                "a value inside the range is left alone");
            SelfTest.Equal(0, Conv.GetConstrained(-5, 0, 10),
                "a value below the range is raised to the minimum");
            SelfTest.Equal(10, Conv.GetConstrained(50, 0, 10),
                "a value above the range is lowered to the maximum");
            SelfTest.Equal(20, Conv.GetConstrained(20, 20, 56),
                "a value on the minimum is left alone");
            SelfTest.Equal(56, Conv.GetConstrained(56, 20, 56),
                "a value on the maximum is left alone");

        }

        private static void TestColor() {

            SelfTest.Equal(0x0000FF, Conv.GetColorReverse(0xFF0000),
                "reversing a color swaps its red and blue channels");
            SelfTest.Equal(0xFF0000, Conv.GetColorReverse(0x0000FF),
                "reversing a color twice returns the original");
            SelfTest.Equal(0x123456, Conv.GetColorNoAlpha(unchecked((int) 0xFF123456)),
                "the alpha channel is stripped");

        }

        private static void TestBits() {

            SelfTest.Check(Conv.GetBit(0x01, 0),
                "bit zero of 0x01 is set");
            SelfTest.Check(!Conv.GetBit(0x01, 1),
                "bit one of 0x01 is clear");
            SelfTest.Check(Conv.GetBit(0x80, 7),
                "the top bit of 0x80 is set");

            SelfTest.Equal((byte) 0, Conv.GetBitCount(0),
                "zero has no bits set");
            SelfTest.Equal((byte) 8, Conv.GetBitCount(0xFF),
                "0xFF has eight bits set");

        }

    }

}
