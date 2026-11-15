using System.IO;
using System.Windows.Media;

namespace Ilha
{
    public sealed class ShelfItem
    {
        private ShelfItem(string path, string name, ImageSource? icon)
        {
            Path = path;
            Name = name;
            Icon = icon;
        }

        public string Path { get; }
        public string Name { get; }
        public ImageSource? Icon { get; }

        public bool Exists() => File.Exists(Path) || Directory.Exists(Path);

        public static ShelfItem From(string path)
        {
            string name = System.IO.Path.GetFileName(path.TrimEnd('\\', '/'));
            return new ShelfItem(path, string.IsNullOrEmpty(name) ? path : name, Native.GetShellIcon(path));
        }
    }
}
