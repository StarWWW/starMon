// StarMon: hardware monitoring and control
// Portions copyright © 2023-2024 Piotr Szczepański (GPL-3.0)

using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using StarMon.Ui.ViewModels;

namespace StarMon.Ui.Views {

    // The keyboard, drawn as the machine's own.
    //
    // The shape is the Omen/Victus deck: a continuous function row with
    // Delete at its end, the main block, the arrow cluster tucked into the
    // bottom row with its half-height up/down pair, and — where the machine
    // has one — the four-column numeric pad. Each cap carries its legend, so
    // the picture reads as this keyboard rather than a diagram of one.
    //
    // Three things about it are the machine's rather than assumed. The
    // numeric pad is drawn only when the firmware does not report a ten-key-
    // less deck, which is what the 16-inch models ship. The legends and the
    // main block's key widths follow the layout the user actually types on:
    // ISO for the Turkish-Q, German and French decks, ANSI for the US one,
    // with the letters, the enter key and the left shift each taking their
    // proper shape. And a single-zone board lights the whole deck one colour
    // while a four-zone one splits into the left, centre, right and WASD
    // regions the firmware really addresses.
    //
    // Drawn rather than assembled from controls for the reason the chart is:
    // a hundred keys redrawn on every colour change is far cheaper as
    // geometry than as a hundred framework elements, and naming a type from
    // this assembly in markup forces a compilation pass the project cannot
    // run.
    public sealed class KeyboardMap : FrameworkElement {

        private KeyboardViewModel ModelValue;

        // The clickable region of each zone, for selecting one by clicking the
        // part of the board it lights
        private readonly List<Rect> ZoneBounds = new List<Rect>();

        private Brush Deck, DeckEdge;
        private Pen DeckPen;

        // The model name, shown small on the deck. Set from the reading so an
        // Omen reads as an Omen and a Victus as a Victus.
        private string BrandValue = "";

        public int Selected { get; private set; }
        public event Action<int> ZonePicked;

        public KeyboardMap() {
            this.Cursor = Cursors.Hand;
            this.Loaded += (s, e) => ResolveTheme();
        }

        public KeyboardViewModel Model {
            get { return this.ModelValue; }
            set {
                Detach();
                this.ModelValue = value;
                Attach();
                this.Selected = 0;
                InvalidateVisual();
            }
        }

        public string Brand {
            get { return this.BrandValue; }
            set {
                this.BrandValue = value ?? "";
                InvalidateVisual();
            }
        }

        private bool HasNumPadValue = true;

        // Whether this deck has the numeric pad. The 15- and 17-inch machines
        // do; the 16-inch ones are ten-key-less, and drawing them four columns
        // they do not have is the sort of detail that tells a user the picture
        // is of somebody else's laptop. Set from the firmware's keyboard type.
        public bool HasNumPad {
            get { return this.HasNumPadValue; }
            set {
                if(this.HasNumPadValue == value)
                    return;
                this.HasNumPadValue = value;
                InvalidateVisual();
            }
        }

        private bool? IsIsoBodyValue;

        // Whether the deck is an ISO body rather than an ANSI one, as the
        // firmware describes the board it was built with. Null when it does
        // not say, and the typing layout is then all there is to go on.
        public bool? IsIsoBody {
            get { return this.IsIsoBodyValue; }
            set {
                if(this.IsIsoBodyValue == value)
                    return;
                this.IsIsoBodyValue = value;
                this.Keys = null;
                InvalidateVisual();
            }
        }

        private void Attach() {
            if(this.ModelValue == null) return;
            this.ModelValue.PropertyChanged += OnChanged;
            this.ModelValue.Zones.CollectionChanged += OnZonesChanged;
            foreach(ZoneViewModel zone in this.ModelValue.Zones)
                zone.PropertyChanged += OnChanged;
        }

        private void Detach() {
            if(this.ModelValue == null) return;
            this.ModelValue.PropertyChanged -= OnChanged;
            this.ModelValue.Zones.CollectionChanged -= OnZonesChanged;
            foreach(ZoneViewModel zone in this.ModelValue.Zones)
                zone.PropertyChanged -= OnChanged;
        }

        private void OnChanged(object sender, PropertyChangedEventArgs e) {
            InvalidateVisual();
        }

        private void OnZonesChanged(object sender, NotifyCollectionChangedEventArgs e) {
            InvalidateVisual();
        }

        private void ResolveTheme() {

            // The deck is darker than the surrounding card, like the recessed
            // tray a laptop's keys actually sit in
            this.Deck = new SolidColorBrush(Color.FromRgb(0x0D, 0x0F, 0x13));
            ((SolidColorBrush) this.Deck).Freeze();

            this.DeckEdge = Find("CardBorder") ?? Brushes.Gray;
            this.DeckPen = new Pen(this.DeckEdge, 1);
            this.DeckPen.Freeze();

        }

        private Brush Find(string key) {
            object brush = Application.Current != null
                ? Application.Current.TryFindResource(key) : null;
            return brush as Brush;
        }

        // A main block: the key widths row by row, and what each cap says.
        // A width at or below zero is a gap, not a key. The bottom row stops
        // short — its last 3.4 units are the arrow cluster, drawn separately
        // because the stacked up/down pair does not fit a model that only
        // knows widths.
        private sealed class Layout {
            public double[][] Widths;
            public string[][] Legends;
        }

        // The rows every layout shares: the function row across the top, and
        // the modifier row along the bottom
        private static readonly double[] FunctionWidths =
            { 1.07,1.07,1.07,1.07,1.07,1.07,1.07,1.07,1.07,1.07,1.07,1.07,1.07,1.09 };
        private static readonly string[] FunctionLegends =
            { "ESC","F1","F2","F3","F4","F5","F6","F7","F8","F9","F10","F11","F12","DEL" };

        private static readonly double[] ModifierWidths =
            { 1.2,1.2,1.2,1.2, 4.4, 1.2,1.2, -3.4 };
        private static readonly string[] ModifierLegends =
            { "CTRL","FN","WIN","ALT","","ALTGR","CTRL", null };

        // ISO widths: the tall Enter takes a slot on both letter rows (the
        // upper one carries no legend of its own), and the left Shift is
        // narrow with the extra key beside it
        private static readonly double[] IsoNumberWidths  = { 1.0,1,1,1,1,1,1,1,1,1,1,1,1, 2.0 };
        private static readonly double[] IsoUpperWidths   = { 1.5, 1,1,1,1,1,1,1,1,1,1,1,1, 1.5 };
        private static readonly double[] IsoHomeWidths    = { 1.75, 1,1,1,1,1,1,1,1,1,1,1,1, 1.25 };
        private static readonly double[] IsoBottomWidths  = { 1.25, 1, 1,1,1,1,1,1,1,1,1,1, 2.75 };

        // ANSI widths: one fewer key on each of the lower two rows, the width
        // going into a wide Enter and a wide left Shift instead
        private static readonly double[] AnsiNumberWidths = { 1.0,1,1,1,1,1,1,1,1,1,1,1,1, 2.0 };
        private static readonly double[] AnsiUpperWidths  = { 1.5, 1,1,1,1,1,1,1,1,1,1,1,1, 1.5 };
        private static readonly double[] AnsiHomeWidths   = { 1.75, 1,1,1,1,1,1,1,1,1,1, 2.25 };
        private static readonly double[] AnsiBottomWidths = { 2.25, 1,1,1,1,1,1,1,1,1,1, 2.75 };

        // Turkish Q, which is what the machine this was written on ships with
        private static readonly Layout TurkishQ = new Layout {
            Widths = new[] { FunctionWidths, IsoNumberWidths, IsoUpperWidths,
                             IsoHomeWidths, IsoBottomWidths, ModifierWidths },
            Legends = new[] {
                FunctionLegends,
                new[] { "\"","1","2","3","4","5","6","7","8","9","0","*","-","BKSP" },
                new[] { "TAB","Q","W","E","R","T","Y","U","I","O","P","Ğ","Ü","" },
                new[] { "CAPS","A","S","D","F","G","H","J","K","L","Ş","İ",",","ENTER" },
                new[] { "SHIFT","<","Z","X","C","V","B","N","M","Ö","Ç",".","SHIFT" },
                ModifierLegends }
        };

        // German QWERTZ
        private static readonly Layout Qwertz = new Layout {
            Widths = TurkishQ.Widths,
            Legends = new[] {
                FunctionLegends,
                new[] { "^","1","2","3","4","5","6","7","8","9","0","ß","´","BKSP" },
                new[] { "TAB","Q","W","E","R","T","Z","U","I","O","P","Ü","+","" },
                new[] { "CAPS","A","S","D","F","G","H","J","K","L","Ö","Ä","#","ENTER" },
                new[] { "SHIFT","<","Y","X","C","V","B","N","M",",",".","-","SHIFT" },
                ModifierLegends }
        };

        // French AZERTY
        private static readonly Layout Azerty = new Layout {
            Widths = TurkishQ.Widths,
            Legends = new[] {
                FunctionLegends,
                new[] { "²","&","é","\"","'","(","-","è","_","ç","à",")","=","BKSP" },
                new[] { "TAB","A","Z","E","R","T","Y","U","I","O","P","^","$","" },
                new[] { "CAPS","Q","S","D","F","G","H","J","K","L","M","ù","*","ENTER" },
                new[] { "SHIFT","<","W","X","C","V","B","N",",",";",":","!","SHIFT" },
                ModifierLegends }
        };

        // The ISO British and Nordic decks: QWERTY letters on an ISO body
        private static readonly Layout IsoQwerty = new Layout {
            Widths = TurkishQ.Widths,
            Legends = new[] {
                FunctionLegends,
                new[] { "`","1","2","3","4","5","6","7","8","9","0","-","=","BKSP" },
                new[] { "TAB","Q","W","E","R","T","Y","U","I","O","P","[","]","" },
                new[] { "CAPS","A","S","D","F","G","H","J","K","L",";","'","#","ENTER" },
                new[] { "SHIFT","\\","Z","X","C","V","B","N","M",",",".","/","SHIFT" },
                ModifierLegends }
        };

        // The US ANSI deck: wide Enter, wide left Shift, no key beside it
        private static readonly Layout AnsiQwerty = new Layout {
            Widths = new[] { FunctionWidths, AnsiNumberWidths, AnsiUpperWidths,
                             AnsiHomeWidths, AnsiBottomWidths, ModifierWidths },
            Legends = new[] {
                FunctionLegends,
                new[] { "~","1","2","3","4","5","6","7","8","9","0","-","=","BKSP" },
                new[] { "TAB","Q","W","E","R","T","Y","U","I","O","P","[","]","\\" },
                new[] { "CAPS","A","S","D","F","G","H","J","K","L",";","'","ENTER" },
                new[] { "SHIFT","Z","X","C","V","B","N","M",",",".","/","SHIFT" },
                ModifierLegends }
        };

        // The numeric pad, four columns wide, one row below the function row
        // the way the real deck sets it
        private static readonly double[][] PadRows = {
            new[] { 1.0,1,1,1 },
            new[] { 1.0,1,1,1 },
            new[] { 1.0,1,1,1 },
            new[] { 1.0,1,1,1 },
            new[] { 2.0,1,1 }
        };

        private static readonly string[][] PadLegends = {
            new[] { "NL","/","*","-" },
            new[] { "7","8","9","+" },
            new[] { "4","5","6","+" },
            new[] { "1","2","3","↵" },
            new[] { "0",".","↵" }
        };

        // The layout the user actually types on, resolved once. Windows can
        // change the input language while the application runs, so this is
        // re-read whenever the keyboard panel is rebuilt rather than baked in
        // at startup — but not on every frame, which would ask the input
        // manager a hundred times a redraw for an answer that rarely moves.
        private Layout Keys;

        // Picks the layout, from two sources that answer two different
        // questions.
        //
        // The *legends* — what is printed on the caps — follow the layout
        // being typed on, because that is what the user reads. The *body* —
        // whether Enter is tall and the left Shift narrow, or Enter wide and
        // the Shift full — is a property of the physical board, and the
        // firmware knows it: HP's BIOS setup publishes "Keyboard Type", which
        // reads "US5 (Europe KB)" on an ISO deck. Inferring the body from the
        // typing layout got a US user with a European keyboard wrong, and a
        // European user typing US wrong the other way.
        private static Layout LayoutFor(string culture, bool? isoFromFirmware) {

            Layout chosen = LegendsFor(culture);

            // Firmware silent: the typing layout is the only evidence there is
            if(!isoFromFirmware.HasValue)
                return chosen;

            bool haveIso = chosen != AnsiQwerty;
            if(isoFromFirmware.Value == haveIso)
                return chosen;

            // The two disagree. Whole layouts are swapped rather than a body
            // being bolted to the other's legends: the rows differ in how many
            // keys they hold, so mixing them puts Enter's legend on the key
            // beside Enter. An ANSI deck is a US deck, and a European body
            // typed on in US gets the ISO QWERTY set.
            return isoFromFirmware.Value ? IsoQwerty : AnsiQwerty;

        }

        // Which set of legends the caps carry. Everything the application has
        // no drawing for falls back to the ISO QWERTY body, which is the shape
        // of most of the decks HP ships outside the US.
        private static Layout LegendsFor(string culture) {

            if(string.IsNullOrEmpty(culture))
                return IsoQwerty;

            string name = culture.ToLowerInvariant();

            if(name.StartsWith("tr") || name.StartsWith("az"))
                return TurkishQ;
            if(name.StartsWith("de") || name.StartsWith("cs")
                || name.StartsWith("hu") || name.StartsWith("sk")
                || name.StartsWith("sl") || name.StartsWith("hr"))
                return Qwertz;
            if(name.StartsWith("fr") || name.StartsWith("nl-be"))
                return Azerty;
            if(name.StartsWith("en-us")
                || name.StartsWith("ja") || name.StartsWith("ko")
                || name.StartsWith("zh") || name.StartsWith("th"))
                return AnsiQwerty;

            return IsoQwerty;

        }

        // The input language Windows currently has active, or the interface
        // culture if it declines to say
        private static string CurrentInputCulture() {
            try {
                CultureInfo language = InputLanguageManager.Current != null
                    ? InputLanguageManager.Current.CurrentInputLanguage : null;
                if(language != null && !string.IsNullOrEmpty(language.Name))
                    return language.Name;
            } catch { }
            try {
                return CultureInfo.CurrentCulture.Name;
            } catch {
                return null;
            }
        }

        protected override void OnRender(DrawingContext context) {

            if(this.DeckPen == null)
                ResolveTheme();

            if(this.Keys == null)
                this.Keys = LayoutFor(CurrentInputCulture(), this.IsIsoBody);

            // A board with no zones still gets drawn. Zero zones means the
            // backlight switches but takes no colour from this application —
            // a per-key RGB deck — and the picture of the keyboard is exactly
            // as useful there as anywhere else.
            KeyboardViewModel model = this.ModelValue;
            if(model == null)
                return;

            double width = this.ActualWidth, height = this.ActualHeight;
            if(width <= 40 || height <= 30)
                return;

            // The deck, with a little room around it for the light to spill into
            Rect deck = new Rect(2, 2, width - 4, height - 4);
            context.DrawRoundedRectangle(this.Deck, this.DeckPen, deck, 12, 12);

            this.ZoneBounds.Clear();

            bool lit = model.IsBacklightOn;
            int zones = model.Zones.Count;

            double pad = Math.Min(width, height) * 0.05;
            double gap = Math.Max(2, width * 0.005);

            // The keyboard has a fixed shape, so it is fitted and centred
            // rather than stretched to fill: a key kept square reads as a real
            // keycap, where a key stretched to the container's proportions
            // reads as a squashed diagram. The unit is chosen as the largest
            // that fits both the width and the height, and whatever room is
            // left over becomes margin around a centred board.
            // A ten-key-less deck is the main block alone, and gets the whole
            // width for it rather than four empty columns of reserved space
            double padCols = this.HasNumPadValue ? 4 : 0;
            double spanUnits = this.HasNumPadValue ? 1.2 : 0;
            const double mainUnits = 15;
            const double rows = 6;

            // A real chiclet cap is a touch wider than it is tall
            const double keyAspect = 0.90;  // keyH / unit

            double totalUnitsW = mainUnits + spanUnits + padCols;
            double availW = deck.Width - pad * 2;
            double availH = deck.Height - pad * 2;

            // Largest unit the width allows, and the largest the height allows
            // once a row's gap and the square aspect are accounted for
            double unitByWidth = (availW - gap * (this.HasNumPadValue ? 3 : 0)) / totalUnitsW;
            double unitByHeight = (availH - gap * (rows - 1)) / (rows * keyAspect);
            double unit = Math.Min(unitByWidth, unitByHeight);

            double keyH = unit * keyAspect;

            // Centre the board in whatever space is left
            double boardW = totalUnitsW * unit + gap * (this.HasNumPadValue ? 3 : 0);
            double boardH = rows * keyH + gap * (rows - 1);
            double blockTop = deck.Top + pad + Math.Max(0, (availH - boardH) / 2);
            double mainLeft = deck.Left + pad + Math.Max(0, (availW - boardW) / 2);
            double padLeft = mainLeft + mainUnits * unit + gap + spanUnits * unit;

            // Zone boundaries across the main block, for colouring and clicks
            double mainRight = mainLeft + mainUnits * unit;
            double third = (mainRight - mainLeft) / 3.0;

            Rect wasd = zones > 1
                ? WasdRect(mainLeft, blockTop, unit, keyH, gap) : Rect.Empty;

            DrawRows(context, this.Keys.Widths, this.Keys.Legends, mainLeft, blockTop, 0,
                unit, keyH, gap, model, lit, mainLeft, third, wasd, false);

            // The numeric pad starts one row down: the function row does not
            // reach across it on the real deck
            if(this.HasNumPadValue)
                DrawRows(context, PadRows, PadLegends, padLeft, blockTop, 1,
                    unit, keyH, gap, model, lit, mainLeft, third, wasd, true);

            DrawArrows(context, mainLeft + 11.6 * unit, blockTop + 5 * (keyH + gap),
                3.4 * unit, keyH, unit, model, lit, mainLeft, third, wasd);

            // The clickable regions. A single-zone board is one region over the
            // whole deck; a four-zone one is the three vertical bands plus the
            // WASD island, in the same order as the view model's zones.
            if(zones <= 1) {
                this.ZoneBounds.Add(deck);
            } else {
                // The bands have to be the ones ColourFor draws, or clicking a
                // key selects a zone other than the one that key lights. The
                // first two are thirds of the main block; the third is the
                // rest of the board, which is where the numeric pad goes when
                // there is one and simply ends at the board edge when not.
                double bandTop = blockTop, bandH = boardH;
                double rightBandLeft = mainLeft + 2 * third;
                this.ZoneBounds.Add(new Rect(mainLeft, bandTop, third, bandH));
                this.ZoneBounds.Add(new Rect(mainLeft + third, bandTop, third, bandH));
                this.ZoneBounds.Add(new Rect(rightBandLeft, bandTop,
                    Math.Max(1, mainLeft + boardW - rightBandLeft), bandH));
                this.ZoneBounds.Add(wasd);
            }

            // The selected zone, ringed where the board has more than one
            if(zones > 1 && this.Selected >= 0 && this.Selected < this.ZoneBounds.Count) {
                Pen ring = new Pen(new SolidColorBrush(
                    Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF)), 1.5);
                ring.Freeze();
                context.DrawRoundedRectangle(null, ring,
                    Rect.Inflate(this.ZoneBounds[this.Selected], -1, -1), 6, 6);
            }

            DrawBrand(context, deck);

        }

        // Draws a block of rows. Each key is a dark cap with the light of its
        // zone rising through it, so the board reads as backlit rather than as
        // painted slabs.
        private void DrawRows(DrawingContext context, double[][] rows,
            string[][] legends, double left, double top, int rowOffset,
            double unit, double keyH, double gap,
            KeyboardViewModel model, bool lit, double mainLeft, double third,
            Rect wasd, bool isPad) {

            double keyGap = Math.Max(1.5, unit * 0.12);

            for(int r = 0; r < rows.Length; r++) {

                double y = top + (r + rowOffset) * (keyH + gap);
                double x = left;
                int keyIndex = 0;

                foreach(double w in rows[r]) {

                    if(w <= 0) { x += -w * unit; keyIndex++; continue; }

                    double kw = w * unit - keyGap;
                    Rect key = new Rect(x, y, kw, keyH - keyGap * 0.5);

                    double centreX = isPad
                        ? mainLeft + third * 3 + 1   // numpad falls in the right band
                        : x + kw / 2;

                    Color colour = ColourFor(model, centreX, y + keyH / 2,
                        mainLeft, third, wasd);

                    string label = legends != null && r < legends.Length
                        && keyIndex < legends[r].Length ? legends[r][keyIndex] : null;

                    DrawKey(context, key, colour, lit, label);

                    x += w * unit;
                    keyIndex++;

                }

            }

        }

        // The arrow cluster the bottom row leaves room for: left and right
        // full height, the up/down pair stacked between them, the way the
        // real deck squeezes them in
        private void DrawArrows(DrawingContext context, double left, double y,
            double width, double keyH, double unit,
            KeyboardViewModel model, bool lit, double mainLeft, double third,
            Rect wasd) {

            double keyGap = Math.Max(1.5, unit * 0.12);
            double w = width / 3 - keyGap;
            double halfH = (keyH - keyGap * 1.5) / 2;

            Color colour = ColourFor(model, left + width / 2, y + keyH / 2,
                mainLeft, third, wasd);

            DrawKey(context, new Rect(left, y, w, keyH - keyGap * 0.5),
                colour, lit, "←");

            double midX = left + width / 3;
            DrawKey(context, new Rect(midX, y, w, halfH), colour, lit, "↑");
            DrawKey(context, new Rect(midX, y + halfH + keyGap, w, halfH),
                colour, lit, "↓");

            DrawKey(context, new Rect(left + 2 * width / 3, y, w, keyH - keyGap * 0.5),
                colour, lit, "→");

        }

        // One key, drawn as a real laptop keycap: a moulded chiclet with a
        // lit top face inset into a darker base, a highlight along its top
        // edge and a shadow along its bottom, so it reads as a solid piece of
        // plastic whether the backlight is on or off. When it is on, the light
        // pools from beneath the cap and the legend glows.
        private void DrawKey(DrawingContext context, Rect key, Color colour,
            bool lit, string label) {

            if(key.Width < 2 || key.Height < 2)
                return;

            double radius = Math.Min(3.5, key.Height / 5.0);

            // The underglow, drawn first so the cap sits on top of it: a soft
            // pool of the backlight a little wider than the cap. Two passes
            // approximate a bloom without a shader.
            if(lit) {
                Rect halo = Rect.Inflate(key, 2.0, 2.4);
                Brush wide = new SolidColorBrush(
                    Color.FromArgb(0x30, colour.R, colour.G, colour.B));
                wide.Freeze();
                context.DrawRoundedRectangle(wide, null, halo, radius + 3, radius + 3);

                Brush near = new SolidColorBrush(
                    Color.FromArgb(0x66, colour.R, colour.G, colour.B));
                near.Freeze();
                context.DrawRoundedRectangle(near, null,
                    Rect.Inflate(key, 0.8, 1.0), radius + 1, radius + 1);
            }

            // The base of the cap: the darker skirt that the top face sits in,
            // which is what gives a chiclet key its rim
            Rect baseRect = key;
            Brush skirt = new SolidColorBrush(lit
                ? Blend(Color.FromRgb(0x0A, 0x0C, 0x10), colour, 0.20)
                : Color.FromRgb(0x0A, 0x0C, 0x10));
            skirt.Freeze();
            context.DrawRoundedRectangle(skirt, null, baseRect, radius, radius);

            // The top face, inset within the skirt with its own gradient — the
            // part the finger touches, lighter at the top as if lit from above
            Rect face = new Rect(key.X + 1, key.Y + 0.8,
                Math.Max(0, key.Width - 2), Math.Max(0, key.Height - 2.4));
            if(face.Width <= 1 || face.Height <= 1)
                return;

            double faceRadius = Math.Max(1.5, radius - 1);

            LinearGradientBrush topFace = new LinearGradientBrush(
                lit ? Blend(Color.FromRgb(0x2C, 0x30, 0x39), colour, 0.16)
                    : Color.FromRgb(0x2C, 0x30, 0x39),
                lit ? Blend(Color.FromRgb(0x16, 0x18, 0x1E), colour, 0.10)
                    : Color.FromRgb(0x16, 0x18, 0x1E),
                new Point(0, 0), new Point(0, 1));
            topFace.Freeze();
            context.DrawRoundedRectangle(topFace, null, face, faceRadius, faceRadius);

            // A highlight along the top of the face and a shadow along the
            // bottom: the two together are what make the cap read as raised
            double inset = faceRadius;
            Pen top = new Pen(new SolidColorBrush(lit
                ? Color.FromArgb(0x9A, colour.R, colour.G, colour.B)
                : Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF)), 1);
            top.Freeze();
            context.DrawLine(top,
                new Point(face.Left + inset, face.Top + 0.6),
                new Point(face.Right - inset, face.Top + 0.6));

            Pen bottom = new Pen(new SolidColorBrush(
                Color.FromArgb(0x50, 0, 0, 0)), 1);
            bottom.Freeze();
            context.DrawLine(bottom,
                new Point(face.Left + inset, face.Bottom - 0.6),
                new Point(face.Right - inset, face.Bottom - 0.6));

            if(string.IsNullOrEmpty(label))
                return;

            // The legend: crisp light grey when off so it is always readable,
            // and the lit backlight colour when on — a backlit keyboard's
            // legends are exactly the part that lights up. Long words shrink.
            double size = Math.Min(face.Height * 0.42, 10);
            if(label.Length > 2)
                size = Math.Min(size, 7.5);

            Brush ink = new SolidColorBrush(lit
                ? Lift(colour)
                : Color.FromArgb(0xC0, 0xC6, 0xCB, 0xD4));
            ink.Freeze();

            FormattedText text = new FormattedText(label,
                CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                LegendFace, size, ink, Dpi.For(this));

            if(text.Width > face.Width - 3 && text.Width > 0) {
                size *= (face.Width - 3) / text.Width;
                if(size < 4)
                    return;
                text = new FormattedText(label,
                    CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                    LegendFace, size, ink, Dpi.For(this));
            }

            // A faint glow behind a lit legend, so the lettering reads as
            // emitting light rather than merely being coloured
            Point at = new Point(
                face.Left + (face.Width - text.Width) / 2,
                face.Top + (face.Height - text.Height) / 2);

            context.DrawText(text, at);

        }

        // Brightens a backlight colour toward white for a lit legend, so the
        // lettering reads as the lit part of the cap rather than a dim block
        // of the same colour it sits on
        private static Color Lift(Color c) {
            return Color.FromArgb(0xFF,
                (byte) (c.R + (255 - c.R) * 0.55),
                (byte) (c.G + (255 - c.G) * 0.55),
                (byte) (c.B + (255 - c.B) * 0.55));
        }

        // Mixes a colour a fraction of the way toward another
        private static Color Blend(Color a, Color b, double t) {
            return Color.FromRgb(
                (byte) (a.R + (b.R - a.R) * t),
                (byte) (a.G + (b.G - a.G) * t),
                (byte) (a.B + (b.B - a.B) * t));
        }

        private static readonly Typeface LegendFace = new Typeface(
            new FontFamily("Bahnschrift, Segoe UI"),
            FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

        // Which zone lights a key, from its horizontal position. A single-zone
        // board is all zone 0; a multi-zone one is left / centre / right thirds
        // with the WASD keys pulled out into their own zone.
        private Color ColourFor(KeyboardViewModel model, double centreX,
            double centreY, double mainLeft, double third, Rect wasd) {

            // No zones: the deck lights, but its colour is not this
            // application's to know, so it is drawn as plain white light
            if(model.Zones.Count == 0)
                return Color.FromRgb(0xEC, 0xEF, 0xF4);

            if(model.Zones.Count == 1)
                return model.Zones[0].Colour;

            // WASD island: the W on the letter rows, pulled into its own zone
            if(!wasd.IsEmpty && wasd.Contains(centreX, centreY))
                return model.Zones[3].Colour;

            int band = centreX < mainLeft + third ? 0
                : centreX < mainLeft + 2 * third ? 1 : 2;
            return model.Zones[band].Colour;
        }

        // The WASD keys sit a little in from the left edge on the letter rows
        // (Tab row and the one below it). The island is approximate — it is a
        // zone label, not a key map — but it lands on the right keys.
        private Rect WasdRect(double mainLeft, double top, double unit,
            double keyH, double gap) {
            double y = top + 2 * (keyH + gap);
            return new Rect(mainLeft + 0.9 * unit, y,
                3.6 * unit, 2 * keyH + gap);
        }

        // The model's name, small and quiet on the deck's lower right, so the
        // picture is of this machine
        private void DrawBrand(DrawingContext context, Rect deck) {

            string text = this.BrandValue;
            if(string.IsNullOrEmpty(text))
                return;

            FormattedText formatted = new FormattedText(text.ToUpperInvariant(),
                CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                LegendFace, 9,
                new SolidColorBrush(Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF)), Dpi.For(this));

            context.DrawText(formatted, new Point(
                deck.Right - formatted.Width - 12, deck.Bottom - formatted.Height - 7));

        }

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e) {

            base.OnMouseLeftButtonDown(e);
            Point at = e.GetPosition(this);

            // The WASD island overlaps the left band, so it is tested first
            for(int i = this.ZoneBounds.Count - 1; i >= 0; i--) {

                if(!this.ZoneBounds[i].Contains(at))
                    continue;

                this.Selected = i;
                InvalidateVisual();

                Action<int> handler = this.ZonePicked;
                if(handler != null)
                    handler(i);

                break;

            }

        }

    }

}
