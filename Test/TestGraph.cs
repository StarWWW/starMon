// StarMon: hardware monitoring and control
// Portions copyright © 2023-2024 Piotr Szczepański (GPL-3.0)

using System;
using System.Collections.Generic;
using StarMon.AppService;

namespace StarMon.Test {

    // Exercises the history buffer. The samples live in a ring, so the order
    // they come back out in is easy to get subtly wrong, and a wrong order in
    // an export is not obvious from looking at it.
    //
    // These used to construct the WinForms graph control, because the buffer
    // was part of it. It is its own class now, so none of this needs a window.
    [TestSuite(Order = 50)]
    public static class TestGraph {

        public static void Run() {

            SelfTest.Group("History buffer");

            TestOrderAndContents();
            TestGapsAreEmpty();
            TestWrapAroundKeepsOrder();
            TestSamplesComeOutOldestFirst();
            TestSeriesTakeSlotsInOrder();
            TestWideningKeepsHistory();
            TestNarrowingKeepsTheNewest();
            TestResizedBufferKeepsRecording();
            TestRowWithoutATipHasNoTooltip();
            TestExportCarriesTheTime();
            TestTimesFollowAResize();
            TestSummaryIgnoresTheUnfilledRing();

        }

        // Each row says when it was taken. The samples are not evenly spaced
        // — the cadence slows while the window is hidden — so a row number
        // alone could not say when anything in an exported file happened.
        private static void TestExportCarriesTheTime() {

            DateTime at = new DateTime(2026, 9, 26, 14, 30, 5);
            HistoryBuffer buffer = Build(8);
            buffer.Clock = () => at;

            buffer.Push(50, 10);
            at = at.AddSeconds(3);
            buffer.Push(60, 20);

            string[] lines = Lines(buffer.BuildCsv());

            SelfTest.Equal("Sample,Time,CPU (°),Fan (%)", lines[0],
                "the export names a time column after the sample number");
            SelfTest.Equal("1,2026-09-26T14:30:05,50,10", lines[1],
                "each row carries the moment it was recorded");
            SelfTest.Equal("2,2026-09-26T14:30:08,60,20", lines[2],
                "and the next one its own");

        }

        // A window resized under the samples keeps each one's time with it,
        // or an export after the resize would put the right values against
        // the wrong moments
        private static void TestTimesFollowAResize() {

            DateTime at = new DateTime(2026, 1, 1, 0, 0, 0);
            HistoryBuffer buffer = Build(HistoryBuffer.MinimumCapacity);
            buffer.Clock = () => at;

            for(int i = 1; i <= 10; i++) {
                buffer.Push(i * 10, i);
                at = at.AddSeconds(1);
            }

            buffer.SetCapacity(32);

            string[] lines = Lines(buffer.BuildCsv());

            SelfTest.Equal("1,2026-01-01T00:00:02,30,3", lines[1],
                "the oldest kept sample keeps its own time across a resize");
            SelfTest.Equal("8,2026-01-01T00:00:09,100,10", lines[8],
                "and so does the newest");

        }

        // The hover summary reads the samples held, not the whole ring. A ring
        // that has not filled is zeros past its end, and counting those put the
        // minimum at nought for the first ten minutes of every session.
        private static void TestSummaryIgnoresTheUnfilledRing() {

            HistoryBuffer buffer = Build(32);
            buffer.Push(50, 40);
            buffer.Push(70, 60);

            string summary = buffer.BuildSummary();

            SelfTest.Check(summary.Contains("CPU: 70°   (min 50  avg 60  max 70)"),
                "the summary's minimum and mean come from the samples actually held");
            SelfTest.Check(!summary.Contains("min 0"),
                "the unfilled part of the ring is not read as readings of zero");

        }

        // A detail row with nothing to say must bind null, not "".
        //
        // WPF treats an empty string as a tooltip like any other, so a row
        // whose Tip was "" opened a small blank box under the pointer. Three
        // of the four pages that show these rows bound the property straight
        // through and had it; the property itself is now what refuses.
        //
        // Checked here rather than left to the views because the views cannot
        // be tested without a window, and because the next page written would
        // otherwise have to remember a trigger nobody documented.
        private static void TestRowWithoutATipHasNoTooltip() {

            SelfTest.Check(new Ui.ViewModels.DetailRowViewModel(
                "CPU", "78 °C").Tip == null,
                "a row given no tip binds null rather than an empty tooltip");

            SelfTest.Check(new Ui.ViewModels.DetailRowViewModel(
                "CPU", "78 °C", "").Tip == null,
                "an explicitly empty tip binds null too");

            SelfTest.Equal("what this is",
                new Ui.ViewModels.DetailRowViewModel(
                    "CPU", "78 °C", "what this is").Tip,
                "a row given a tip keeps it");

        }

        // Changing the chart's window must not throw the history away. It used
        // to: switching to a longer window to look at a trend cleared the
        // trend, which is the one thing the user was asking to see.
        private static void TestWideningKeepsHistory() {

            HistoryBuffer buffer = Build(HistoryBuffer.MinimumCapacity);

            for(int i = 1; i <= 5; i++)
                buffer.Push(i * 10, i);

            buffer.SetCapacity(32);

            string[] lines = Lines(buffer.BuildCsv(false));

            SelfTest.Equal(6, lines.Length,
                "widening the window keeps every sample it already had");
            SelfTest.Equal("1,10,1", lines[1],
                "the oldest sample survives being moved to a longer window");
            SelfTest.Equal("5,50,5", lines[5],
                "so does the newest");
            SelfTest.Equal(32, buffer.Capacity,
                "and the window is the length that was asked for");

        }

        // Narrowing cannot keep everything, so it keeps the newest — the end
        // of the history the user is looking at, not the start of it
        private static void TestNarrowingKeepsTheNewest() {

            HistoryBuffer buffer = Build(32);

            for(int i = 1; i <= 20; i++)
                buffer.Push(i * 10, i);

            buffer.SetCapacity(HistoryBuffer.MinimumCapacity);

            string[] lines = Lines(buffer.BuildCsv(false));

            SelfTest.Equal(HistoryBuffer.MinimumCapacity + 1, lines.Length,
                "narrowing keeps exactly as many samples as the window holds");
            SelfTest.Equal("1,130,13", lines[1],
                "the kept run starts where the new window reaches back to");
            SelfTest.Equal("8,200,20", lines[HistoryBuffer.MinimumCapacity],
                "and ends at the most recent sample");

        }

        // The ring's write position has to be moved along with the samples, or
        // the next reading lands on top of one of them
        private static void TestResizedBufferKeepsRecording() {

            HistoryBuffer buffer = Build(HistoryBuffer.MinimumCapacity);

            for(int i = 1; i <= 3; i++)
                buffer.Push(i * 10, i);

            buffer.SetCapacity(16);
            buffer.Push(40, 4);

            string[] lines = Lines(buffer.BuildCsv(false));

            SelfTest.Equal(5, lines.Length,
                "a sample recorded after a resize is added, not overwritten");
            SelfTest.Equal("3,30,3", lines[3],
                "the last sample before the resize is still there");
            SelfTest.Equal("4,40,4", lines[4],
                "and the new one follows it");

            // Filling a resized buffer past its new capacity has to wrap on
            // the new length, not the old one
            for(int i = 5; i <= 20; i++)
                buffer.Push(i * 10, i);

            SelfTest.Equal(17, Lines(buffer.BuildCsv(false)).Length,
                "and it wraps at the new capacity once it is full");

        }

        // Builds a buffer with a small window and two series
        private static HistoryBuffer Build(int capacity) {
            HistoryBuffer buffer = new HistoryBuffer();
            buffer.Begin(capacity);
            buffer.Add("CPU", 0, 100, "°");
            buffer.Add("Fan", 0, 100, "%");
            return buffer;
        }

        private static string[] Lines(string csv) {
            return csv.Split(new[] { Environment.NewLine },
                StringSplitOptions.RemoveEmptyEntries);
        }

        private static void TestOrderAndContents() {

            HistoryBuffer buffer = Build(8);
            buffer.Push(50, 10);
            buffer.Push(60, 20);
            buffer.Push(70, 30);

            string[] lines = Lines(buffer.BuildCsv(false));

            SelfTest.Equal(4, lines.Length,
                "the export has a header plus one row per sample");

            SelfTest.Equal("Sample,CPU (°),Fan (%)", lines[0],
                "the header names each series with its unit");

            SelfTest.Equal("1,50,10", lines[1],
                "the first row is the oldest sample");
            SelfTest.Equal("2,60,20", lines[2],
                "rows follow in the order they were recorded");
            SelfTest.Equal("3,70,30", lines[3],
                "the last row is the most recent sample");

        }

        // A sensor that was unavailable must not be exported as a zero
        // reading, which would be indistinguishable from a real one
        private static void TestGapsAreEmpty() {

            HistoryBuffer buffer = Build(8);
            buffer.Push(50, 0);   // The fan value is a gap
            buffer.Push(60, 20);

            string[] lines = Lines(buffer.BuildCsv(false));

            SelfTest.Equal("1,50,", lines[1],
                "a missing reading is exported as an empty cell, not a zero");
            SelfTest.Equal("2,60,20", lines[2],
                "a present reading alongside it is unaffected");

        }

        // Once the buffer has wrapped, the oldest sample is no longer at
        // index zero. This is where an off-by-one shows up as an export that
        // starts in the middle of the history.
        private static void TestWrapAroundKeepsOrder() {

            // The buffer refuses to hold fewer than eight samples, so that is
            // the smallest window a wrap can actually be provoked in
            const int capacity = HistoryBuffer.MinimumCapacity;
            HistoryBuffer buffer = Build(capacity);

            SelfTest.Equal(capacity, Build(4).Capacity,
                "a window smaller than the floor is raised to it");

            // Ten samples into a window of eight: the first two fall off
            for(int i = 1; i <= 10; i++)
                buffer.Push(i * 10, i);

            string[] lines = Lines(buffer.BuildCsv(false));

            SelfTest.Equal(capacity + 1, lines.Length,
                "a wrapped buffer exports exactly its capacity");

            SelfTest.Equal("1,30,3", lines[1],
                "the oldest surviving sample comes first after a wrap");
            SelfTest.Equal("8,100,10", lines[capacity],
                "the most recent sample comes last after a wrap");

        }

        // The plot walks the same sequence the export does, so it is worth
        // checking directly rather than only through the CSV: a chart drawn
        // from a wrongly-ordered ring looks like a plausible chart of
        // something else.
        private static void TestSamplesComeOutOldestFirst() {

            HistoryBuffer buffer = Build(8);
            for(int i = 1; i <= 11; i++)
                buffer.Push(i, 0);

            List<double> samples = new List<double>(buffer.Samples(buffer.Series[0]));

            SelfTest.Equal(8, samples.Count,
                "a wrapped buffer yields exactly its capacity");
            SelfTest.Equal(4d, samples[0],
                "the oldest surviving sample comes out first");
            SelfTest.Equal(11d, samples[samples.Count - 1],
                "the newest comes out last");

            List<double> gaps = new List<double>(buffer.Samples(buffer.Series[1]));

            SelfTest.Check(Double.IsNaN(gaps[0]),
                "a series that never reported yields gaps rather than zeroes");

        }

        // The palette's order is what keeps neighbouring series apart for
        // colour-blind readers, so a series takes the next slot rather than
        // choosing one
        private static void TestSeriesTakeSlotsInOrder() {

            HistoryBuffer buffer = new HistoryBuffer();
            buffer.Begin(60);

            SelfTest.Equal(0, buffer.Add("A", 0, 1, "").Slot, "the first series takes slot 0");
            SelfTest.Equal(1, buffer.Add("B", 0, 1, "").Slot, "the second takes slot 1");
            SelfTest.Equal(2, buffer.Add("C", 0, 1, "").Slot, "the third takes slot 2");

        }

    }

}
