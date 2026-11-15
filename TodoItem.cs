using System.ComponentModel;

namespace Ilha
{
    /// <summary>Tarefa ou lembrete da Agenda (salvo nas configurações).</summary>
    public sealed class TodoItem : INotifyPropertyChanged
    {
        private bool _done;

        public string Text { get; set; } = "";

        /// <summary>
        /// Dia do lembrete ("yyyy-MM-dd"). Vazio = tarefa sem dia (as antigas), que aparece sempre no dia de hoje.
        /// </summary>
        public string? Date { get; set; }

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
