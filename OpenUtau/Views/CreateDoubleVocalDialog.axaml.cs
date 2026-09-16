using Avalonia.Controls;
using Avalonia.Interactivity;
using OpenUtau.App.ViewModels;
using OpenUtau.Core.Editing;

namespace OpenUtau.App.Views {
    public partial class CreateDoubleVocalDialog : Window {
        readonly CreateDoubleVocalViewModel viewModel;

        public DoubleVocalOptions? Result { get; private set; }

        public CreateDoubleVocalDialog() {
            viewModel = new CreateDoubleVocalViewModel();
            InitializeComponent();
            DataContext = viewModel;
        }

        void OnCancel(object? sender, RoutedEventArgs e) => Close(false);

        void OnOk(object? sender, RoutedEventArgs e) {
            Result = viewModel.ToOptions();
            Close(true);
        }
    }
}
