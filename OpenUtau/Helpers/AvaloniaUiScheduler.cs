using System.Reactive.Concurrency;
using Avalonia.Threading;

namespace OpenUtau.App.Helpers {
    /// <summary>
    /// System.Reactive IScheduler over Avalonia's dispatcher.
    /// ReactiveUI 24's AvaloniaScheduler is ISequencer and does not plug into System.Reactive ObserveOn.
    /// </summary>
    public static class AvaloniaUiScheduler {
        public static IScheduler Instance { get; } =
            new SynchronizationContextScheduler(new AvaloniaSynchronizationContext());
    }
}
