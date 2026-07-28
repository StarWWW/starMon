// StarMon: hardware monitoring and control
// Portions copyright © 2023-2024 Piotr Szczepański (GPL-3.0)

using System;
using System.Collections.Generic;
using System.Windows.Controls;

namespace StarMon.Ui.Shell {

    // One entry in the tray menu, described rather than constructed.
    //
    // The WinForms menu was 1,399 lines, most of it the same four statements
    // repeated: make an item, give it a handler, give it a name, put it in a
    // list — and then, because a menu shows the state of things, a second pass
    // that walked every item before opening and set its caption and tick from
    // the configuration.
    //
    // Here an item says what its caption is and whether it is ticked, as
    // functions, and the menu asks them when it opens. That removes the second
    // pass entirely, and with it the class of bug where an item's caption and
    // the setting behind it drift apart because one of them was updated in two
    // places and the other in one.
    public sealed class MenuModel {

        private readonly List<MenuModel> ChildList = new List<MenuModel>();

        private MenuModel() { }

        // What to show. A function rather than a string because most of these
        // captions carry a value — "Update: 3 s", "Language: Türkçe" — and a
        // caption that has to be refreshed is a caption that can be forgotten.
        public Func<string> Caption;

        // Whether to show a tick, or null for an item that is not a setting
        public Func<bool> IsChecked;

        // Whether the item can be used at all. Null means always.
        public Func<bool> IsEnabled;

        // What happens when it is chosen
        public Action Invoke;

        // Whether the menu stays open afterwards. Flipping four settings in a
        // row should not mean opening the menu four times.
        public bool StaysOpen;

        public bool IsSeparator { get; private set; }

        public IList<MenuModel> Children { get { return this.ChildList; } }

        public static MenuModel Item(Func<string> caption, Action invoke) {
            return new MenuModel { Caption = caption, Invoke = invoke };
        }

        public static MenuModel Item(string caption, Action invoke) {
            return new MenuModel { Caption = () => caption, Invoke = invoke };
        }

        // A setting: ticked when on, and the menu stays open so several can be
        // changed together
        public static MenuModel Toggle(Func<string> caption,
            Func<bool> isChecked, Action invoke) {

            return new MenuModel {
                Caption = caption,
                IsChecked = isChecked,
                Invoke = invoke,
                StaysOpen = true
            };

        }

        public static MenuModel Separator() {
            return new MenuModel { IsSeparator = true };
        }

        public static MenuModel Branch(Func<string> caption, params MenuModel[] children) {

            MenuModel branch = new MenuModel { Caption = caption };
            branch.ChildList.AddRange(children);
            return branch;

        }

        public MenuModel Add(MenuModel child) {
            this.ChildList.Add(child);
            return this;
        }

        public MenuModel Disable(Func<bool> isEnabled) {
            this.IsEnabled = isEnabled;
            return this;
        }

        // Builds the WPF items. Called every time the menu opens rather than
        // once at startup: the captions and ticks are functions of the current
        // state, and rebuilding is both simpler and cheaper than walking an
        // existing tree to bring it up to date — a tray menu is thirty items,
        // not thirty thousand.
        public static void Fill(ItemsControl into, IEnumerable<MenuModel> models) {

            into.Items.Clear();

            foreach(MenuModel model in models) {

                if(model.IsSeparator) {
                    into.Items.Add(new Separator());
                    continue;
                }

                MenuItem item = new MenuItem {
                    Header = model.Caption != null ? model.Caption() : "",
                    StaysOpenOnClick = model.StaysOpen
                };

                if(model.IsChecked != null) {
                    item.IsCheckable = true;
                    item.IsChecked = model.IsChecked();
                }

                if(model.IsEnabled != null)
                    item.IsEnabled = model.IsEnabled();

                if(model.Children.Count > 0) {

                    // Filled when the branch opens, not now: a sub-menu nobody
                    // looks at should not cost a hardware read to build
                    MenuModel captured = model;
                    item.SubmenuOpened += delegate {
                        Fill(item, captured.Children);
                    };

                    // A placeholder, so the branch shows an arrow before it
                    // has ever been opened
                    item.Items.Add(new MenuItem());

                } else if(model.Invoke != null) {

                    MenuModel captured = model;
                    item.Click += delegate {

                        try {
                            captured.Invoke();
                        } catch(Exception e) {
                            Library.Logger.Error("Menu",
                                "A menu action failed", e.Message);
                        }

                        // A toggle that stays open has to bring its own tick
                        // up to date, since nothing is going to rebuild it
                        if(captured.StaysOpen && captured.IsChecked != null)
                            item.IsChecked = captured.IsChecked();

                        if(captured.StaysOpen && captured.Caption != null)
                            item.Header = captured.Caption();

                    };

                }

                into.Items.Add(item);

            }

        }

    }

}
