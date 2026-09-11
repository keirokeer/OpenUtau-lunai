using System.Collections.Generic;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using OpenUtau.Api;
using OpenUtau.App.Controls;
using OpenUtau.Core.Ustx;

namespace OpenUtau.App.ViewModels {
    public class MenuItemViewModel {
        public object? Header { get; set; }
        public ICommand? Command { get; set; }
        public ICommand? SecondaryCommand { get; set; }
        public object? CommandParameter { get; set; }
        public IList<MenuItemViewModel>? Items { get; set; }
        public double Height { get; set; } = 24;
        public bool IsChecked { get; set; } = false;
        public KeyGesture? InputGesture { get; set; }
        public bool IsEnabled { get; set; } = true;
        public object? Icon { get; set; }
        public virtual object HeaderViewModel => Header!;

        public MenuItemViewModel() { }
        public MenuItemViewModel(bool isChecked) {
            IsChecked = isChecked;
            Dispatcher.UIThread.Post(() => {
                Icon = new Path {
                    IsVisible = isChecked,
                    Classes = { "checkmenu" },
                };
            });
        }
    }

    public sealed class MenuSeparatorViewModel : MenuItemViewModel {
        public MenuSeparatorViewModel() {
            // Visual header is lazy: RefreshSingersAsync builds the menu on a
            // worker thread, and Avalonia controls must be created on the UI thread.
            Height = 8;
            IsEnabled = false;
        }

        public override object HeaderViewModel => EnsureHeader();

        object EnsureHeader() {
            if (Header != null) {
                return Header;
            }
            void Create() {
                Header ??= new Panel {
                    Height = 8,
                    Background = Brushes.Transparent,
                    IsHitTestVisible = false,
                };
            }
            if (Dispatcher.UIThread.CheckAccess()) {
                Create();
            } else {
                Dispatcher.UIThread.Invoke(Create);
            }
            return Header!;
        }
    }

    public sealed class PhonemizerMenuSeparatorViewModel : MenuItemViewModel {
        public PhonemizerMenuSeparatorViewModel() {
            Height = 1;
            IsEnabled = false;
        }

        public override object HeaderViewModel => EnsureHeader();

        object EnsureHeader() {
            if (Header != null) {
                return Header;
            }
            void Create() {
                Header ??= new Border {
                    Height = 1,
                    Margin = new Thickness(0),
                    Background = ThemeManager.MutedIconBrush,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    VerticalAlignment = VerticalAlignment.Center,
                    IsHitTestVisible = false,
                };
            }
            if (Dispatcher.UIThread.CheckAccess()) {
                Create();
            } else {
                Dispatcher.UIThread.Invoke(Create);
            }
            return Header!;
        }
    }

    public class PhonemizerMenuItemViewModel : MenuItemViewModel {
        public override object HeaderViewModel => this;

        public PhonemizerMenuItemViewModel(
            PhonemizerFactory factory,
            ICommand command,
            string? prefix = null,
            ICommand? secondaryCommand = null) {
            Command = command;
            SecondaryCommand = secondaryCommand;
            CommandParameter = factory;
            Header = BuildHeader(factory, prefix);
        }

        static Control BuildHeader(PhonemizerFactory factory, string? prefix) {
            var panel = new StackPanel {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
            };
            var tagBlock = new TextBlock {
                Text = prefix ?? factory.tag,
                VerticalAlignment = VerticalAlignment.Center,
            };
            tagBlock.Foreground = ThemeManager.ForegroundBrush;
            var detail = factory.name;
            if (!string.IsNullOrEmpty(factory.author)) {
                detail += $" (Contributed by {factory.author})";
            }
            var detailBlock = new TextBlock {
                Text = detail,
                Opacity = 0.4,
                VerticalAlignment = VerticalAlignment.Center,
            };
            detailBlock.Foreground = ThemeManager.ForegroundBrush;
            panel.Children.Add(tagBlock);
            panel.Children.Add(detailBlock);
            return panel;
        }
    }

    public class SingerMenuItemViewModel : MenuItemViewModel {
        Grid? singerMenuRow;
        public override object HeaderViewModel => singerMenuRow ??= SingerMenuRow.Create(this);
        public new object? Icon => null;

        public bool IsFavourite {
            get {
                if(CommandParameter is USinger singer) {
                    return singer.IsFavourite;
                }
                return false;
            }
            set {
                if (CommandParameter is USinger singer) {
                    singer.IsFavourite = value;
                }
            }
        }
        public string? Location {
            get {
                if (CommandParameter is USinger singer) {
                    return singer.Location;
                }
                return null;
            }
        }
    }
}
