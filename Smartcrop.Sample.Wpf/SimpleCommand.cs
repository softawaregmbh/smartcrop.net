using System;
using System.Windows.Input;

namespace Smartcrop.Sample.Wpf
{
    public class SimpleCommand : ICommand
    {
        private readonly Action executeAction;

        public SimpleCommand(Action executeAction)
        {
            this.executeAction = executeAction ?? throw new ArgumentNullException(nameof(executeAction));
        }

        public event EventHandler CanExecuteChanged;

        public bool CanExecute(object parameter) => true;

        public void Execute(object parameter) => this.executeAction();
    }
}
