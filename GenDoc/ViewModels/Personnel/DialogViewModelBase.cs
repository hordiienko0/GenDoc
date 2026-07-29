using CommunityToolkit.Mvvm.ComponentModel;

namespace GenDoc.ViewModels.Personnel
{
    public abstract class DialogViewModelBase : ObservableObject
    {
        public event EventHandler? RequestClose;

        public bool DialogResultValue { get; private set; }

        protected void CloseDialog(bool result)
        {
            DialogResultValue = result;
            RequestClose?.Invoke(this, EventArgs.Empty);
        }
    }
}
