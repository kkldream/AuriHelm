namespace AudioOutputManager.Windows;

/// <summary>Dispatches shell callbacks without confusing context-menu activation with the primary action.</summary>
public sealed class TrayInteraction(Action quickMenu,Action contextMenu)
{
    public bool Dispatch(uint notification)
    {
        switch(notification&0xffff)
        {
            case 0x202:quickMenu();return true;
            case 0x205:case 0x7b:contextMenu();return true;
            default:return false;
        }
    }
}
