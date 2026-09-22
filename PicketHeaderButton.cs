using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;

namespace Pickets;

public sealed class PicketHeaderButton : Button
{
    public static readonly DependencyProperty IsExpandedProperty = DependencyProperty.Register(
        nameof(IsExpanded), typeof(bool), typeof(PicketHeaderButton), new PropertyMetadata(true, ExpandedChanged));
    public bool IsExpanded { get => (bool)GetValue(IsExpandedProperty); set => SetValue(IsExpandedProperty, value); }
    internal Action<bool>? SetExpanded { get; set; }

    protected override AutomationPeer OnCreateAutomationPeer() => new HeaderPeer(this);

    private static void ExpandedChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        if (UIElementAutomationPeer.FromElement((PicketHeaderButton)sender) is HeaderPeer peer)
            peer.RaisePropertyChangedEvent(ExpandCollapsePatternIdentifiers.ExpandCollapseStateProperty,
                (bool)e.OldValue ? ExpandCollapseState.Expanded : ExpandCollapseState.Collapsed,
                (bool)e.NewValue ? ExpandCollapseState.Expanded : ExpandCollapseState.Collapsed);
    }

    private sealed class HeaderPeer(PicketHeaderButton owner) : ButtonAutomationPeer(owner), IExpandCollapseProvider
    {
        public override object? GetPattern(PatternInterface patternInterface)
            => patternInterface == PatternInterface.ExpandCollapse ? this : base.GetPattern(patternInterface);
        public ExpandCollapseState ExpandCollapseState
            => owner.IsExpanded ? ExpandCollapseState.Expanded : ExpandCollapseState.Collapsed;
        public void Expand() => owner.Dispatcher.Invoke(() => owner.SetExpanded?.Invoke(true));
        public void Collapse() => owner.Dispatcher.Invoke(() => owner.SetExpanded?.Invoke(false));
    }
}
