// StarMon: hardware monitoring and control
// Portions copyright © 2023-2024 Piotr Szczepański (GPL-3.0)

using System;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace StarMon.Ui.Shell {

    // Turns a WPF visual into an icon handle.
    //
    // The notification icon is drawn at runtime, because it carries the
    // current temperature. WinForms did that with GDI+ into a Bitmap and then
    // GetHicon. WPF renders any Visual to a bitmap happily enough, but the
    // shell wants an HICON and there is no framework call for the conversion:
    // it has to go through CreateIconIndirect with a colour bitmap and a mask.
    public static class IconRender {

        [StructLayout(LayoutKind.Sequential)]
        private struct ICONINFO {
            public bool fIcon;
            public int xHotspot;
            public int yHotspot;
            public IntPtr hbmMask;
            public IntPtr hbmColor;
        }

        [DllImport("user32.dll", CallingConvention = CallingConvention.Winapi)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern IntPtr CreateIconIndirect(ref ICONINFO iconInfo);

        [DllImport("gdi32.dll", CallingConvention = CallingConvention.Winapi)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern IntPtr CreateBitmap(int width, int height,
            uint planes, uint bitsPerPixel, IntPtr bits);

        [DllImport("gdi32.dll", CallingConvention = CallingConvention.Winapi)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern bool DeleteObject(IntPtr handle);

        // Renders a visual at the given pixel size and returns an icon handle.
        // The caller owns the handle and must destroy it.
        //
        // The visual is rendered at three times the nominal density and then
        // let down to the requested size: the tray icon carries two digits in
        // sixteen pixels, and the difference between rendering that directly
        // and downsampling it is the difference between legible and not.
        public static IntPtr FromVisual(Visual visual, int size) {

            RenderTargetBitmap bitmap = new RenderTargetBitmap(
                size, size, size * 3, size * 3, PixelFormats.Pbgra32);
            bitmap.Render(visual);

            return FromBitmap(bitmap, size);

        }

        public static IntPtr FromBitmap(BitmapSource source, int size) {

            // GDI stores device-independent bitmap rows bottom-up, so the rows
            // are copied out in reverse; getting this wrong produces an icon
            // that is upside down but otherwise perfectly plausible
            int stride = size * 4;
            byte[] pixels = new byte[stride * size];
            source.CopyPixels(pixels, stride, 0);

            byte[] flipped = new byte[pixels.Length];
            for(int y = 0; y < size; y++)
                Buffer.BlockCopy(pixels, y * stride,
                    flipped, (size - 1 - y) * stride, stride);

            GCHandle pin = GCHandle.Alloc(flipped, GCHandleType.Pinned);
            IntPtr colour = IntPtr.Zero, mask = IntPtr.Zero;

            try {

                colour = CreateBitmap(size, size, 1, 32, pin.AddrOfPinnedObject());

                // A 32-bit colour bitmap carries its own alpha, so the mask is
                // only here to satisfy the API: an all-zero monochrome bitmap
                // means no pixel is masked out
                mask = CreateBitmap(size, size, 1, 1, IntPtr.Zero);

                if(colour == IntPtr.Zero || mask == IntPtr.Zero)
                    return IntPtr.Zero;

                ICONINFO info = new ICONINFO {
                    fIcon = true,
                    xHotspot = 0,
                    yHotspot = 0,
                    hbmColor = colour,
                    hbmMask = mask
                };

                return CreateIconIndirect(ref info);

            } finally {

                if(colour != IntPtr.Zero) DeleteObject(colour);
                if(mask != IntPtr.Zero) DeleteObject(mask);
                pin.Free();

            }

        }

    }

}
