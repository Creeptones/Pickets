using System.Windows;
using System.Windows.Controls;

namespace Pickets;

/// <summary>Picks File vs. Label template based on FenceItem.Kind. WrapPanel lays labels out on
/// their own rows because the label template's Border matches the wrap panel's width.</summary>
public class FenceItemTemplateSelector : DataTemplateSelector
{
    public DataTemplate? FileTemplate  { get; set; }
    public DataTemplate? LabelTemplate { get; set; }

    public override DataTemplate? SelectTemplate(object item, DependencyObject container)
    {
        if (item is FenceItem fi && fi.Kind == ItemKind.Label) return LabelTemplate;
        return FileTemplate;
    }
}
