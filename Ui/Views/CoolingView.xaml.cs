// StarMon: hardware monitoring and control
// Portions copyright © 2023-2024 Piotr Szczepański (GPL-3.0)

using System.Windows;
using System.Windows.Controls;
using StarMon.Ui.ViewModels;

namespace StarMon.Ui.Views {

    // The cooling section. This was CurveView, which held only the editor;
    // the saved programs and the machine's own cooling limits live here now,
    // because all three answer the same question and were in three different
    // places — one of them a log file.
    public partial class CoolingView : UserControl {

        private readonly FanCurveEditor Editor = new FanCurveEditor();

        public CoolingView() {

            InitializeComponent();

            this.EditorHost.Content = this.Editor;
            this.DataContextChanged += OnDataContextChanged;

        }

        private void OnDataContextChanged(object sender,
            DependencyPropertyChangedEventArgs e) {

            // The editor is a drawn control and takes the curve model
            // directly; the page's own context is the cooling model that
            // holds it
            CoolingViewModel model = e.NewValue as CoolingViewModel;
            this.Editor.Model = model != null ? model.Curve : null;

        }

    }

}
