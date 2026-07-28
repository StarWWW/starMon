// StarMon: hardware monitoring and control
// Portions copyright © 2023-2024 Piotr Szczepański (GPL-3.0)

using System;
using StarMon.Library;

namespace StarMon.Test {

    // Exercises the conversion helpers. These sit under the command-line
    // parser and the configuration reader, so a wrong answer here shows up as
    // a setting quietly not taking effect rather than as an error.
    public static class TestConv {

        public static void Run() {

            SelfTest.Group("Conversions");

            TestBool();
            TestByte();
            TestWord();
            TestConstrained();
            TestColor();
            TestBits();

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
