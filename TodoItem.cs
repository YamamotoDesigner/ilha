using System.ComponentModel;

namespace Ilha
{
    /// <summary>Tarefa da lista da Agenda (salva nas configurações).</summary>
    public sealed class TodoItem : INotifyPropertyChanged
    {
        private bool _done;

        public string Text { get; set; } = "";

        public bool Done
        {
            get => _done;
            set
            {
                if (_done == value) return;
                _done = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Done)));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
