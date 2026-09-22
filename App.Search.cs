namespace Pickets;

public partial class App
{
    private QuickFindWindow? _quickFind;
    internal void ShowQuickFind()
    {
        if (_quickFind != null) { _quickFind.Activate(); return; }
        _quickFind = new QuickFindWindow(this);
        _quickFind.Closed += (_, _) => _quickFind = null;
        _quickFind.Show();
    }
}
