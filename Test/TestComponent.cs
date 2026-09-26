// StarMon: hardware monitoring and control
// Portions copyright © 2023-2024 Piotr Szczepański (GPL-3.0)

using System.Collections.Generic;
using StarMon.Hardware.Platform;

namespace StarMon.Test {

    // Exercises what a platform component makes of the readings it is given,
    // with a scripted source in place of a register.
    //
    // The component sits between every register and everything that reads
    // one, and it is allowed to disbelieve an answer. That is the part worth
    // testing: a component that disbelieves too much does not fail, it goes on
    // reporting the last thing it believed — a countdown frozen at the second
    // it was last seen, a stopped fan still turning.
    [TestSuite(Order = 42)]
    public static class TestComponent {

        // A component whose readings are handed to it in order
        private sealed class Scripted : PlatformComponentAbstract, IPlatformReadComponent {

            private readonly Queue<int> Answers = new Queue<int>();

            public Scripted(params int[] answers) {
                this.AccessType = PlatformData.AccessType.Read;
                this.Constraint = int.MaxValue;
                this.Size = PlatformData.DataSize.Byte;
                this.Name = "TEST";
                foreach(int answer in answers)
                    this.Answers.Enqueue(answer);
            }

            protected override int Read() {
                return this.Answers.Count > 0 ? this.Answers.Dequeue() : 0;
            }

        }

        public static void Run() {

            SelfTest.Group("Platform component");

            TestOneZeroIsHeldOff();
            TestTwoZerosAreBelieved();
            TestZeroAfterZeroIsNotHeld();
            TestHeldZeroLeavesNoTrend();

        }

        // A single zero between two readings is a glitch, and is not reported
        private static void TestOneZeroIsHeldOff() {

            Scripted component = new Scripted(120, 0, 110);

            component.Update();
            SelfTest.Check(!component.Update(),
                "a lone zero after a reading is held off");
            SelfTest.Equal(120, component.GetValue(),
                "and the value reported stays the last one believed");

            component.Update();
            SelfTest.Equal(110, component.GetValue(),
                "the next genuine reading is taken as usual");

        }

        // Two in a row is the register saying zero, and it is believed. This is
        // the failsafe countdown running out, and a fan stopping: before, the
        // second zero was refused as well, and every one after it, for ever.
        private static void TestTwoZerosAreBelieved() {

            Scripted component = new Scripted(12, 0, 0, 0);

            component.Update();
            component.Update();
            SelfTest.Check(component.Update(),
                "a second zero in a row is accepted");
            SelfTest.Equal(0, component.GetValue(),
                "so a countdown that ran out reads as run out");

            SelfTest.Check(component.Update(),
                "and zero goes on being accepted while it lasts");

        }

        // A component already at zero has nothing to protect
        private static void TestZeroAfterZeroIsNotHeld() {

            Scripted component = new Scripted(0, 0, 5);

            SelfTest.Check(component.Update(), "a first reading of zero is taken");
            SelfTest.Check(component.Update(), "and so is a second");
            component.Update();
            SelfTest.Equal(5, component.GetValue(), "a reading after them is taken too");

        }

        // The held-off zero must not show up as a movement in the trend
        private static void TestHeldZeroLeavesNoTrend() {

            Scripted component = new Scripted(40, 40, 0);

            component.Update();
            component.Update();
            component.Update();

            SelfTest.Equal(PlatformData.ValueTrend.Unchanged, component.GetValueTrend(),
                "a zero that was held off leaves the trend where it was");

        }

    }

}
