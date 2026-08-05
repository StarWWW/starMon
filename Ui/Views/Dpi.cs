// StarMon: hardware monitoring and control
// Portions copyright © 2023-2024 Piotr Szczepański (GPL-3.0)

using System;
using System.Windows;
using System.Windows.Media;

namespace StarMon.Ui.Views {

    // The scale the text in a drawn control has to be laid out for.
    //
    // FormattedText takes a pixelsPerDip and uses it to pick glyph hinting and
    // advance widths. Every drawn control in this project passed a constant:
    // 1.25 in the chart and the keyboard, 1.0 in the curve editor. That is the
    // scale of one display — the one they were written on — so on a machine at
    // 100 %, 150 % or 200 % every axis label, every crosshair readout and every
    // keycap legend was laid out for a display that was not there, and dragging
    // the window to a second monitor at a different scale changed nothing.
    //
    // The application's own manifest asks for PerMonitorV2, which is a promise
    // to follow the display the window is actually on. Nothing in the visual
    // tree read the DPI at all.
    internal static class Dpi {

        // What this project was written against, and the only sensible answer
        // when there is no display to ask
        private const double Fallback = 1.0;

        // The scale factor for a visual, from the display it is on.
        //
        // VisualTreeHelper.GetDpi needs a visual that has been connected to a
        // presentation source; on one that has not — during construction, or
        // in the offscreen render path that draws the design surfaces — it
        // throws. The fallback is 1.0 rather than 1.25 because an unconnected
        // visual is being measured for no particular display, and the
        // unscaled metrics are the honest answer to that.
        internal static double For(Visual visual) {

            if(visual == null)
                return Fallback;

            try {

                double scale = VisualTreeHelper.GetDpi(visual).PixelsPerDip;
                return scale > 0 ? scale : Fallback;

            } catch {
                return Fallback;
            }

        }

    }

}
