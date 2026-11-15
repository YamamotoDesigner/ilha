using System;
using System.Text.RegularExpressions;

namespace Ilha
{
    public sealed class ClipItem
    {
        public ClipItem(string text)
        {
            Text = text;
            string oneLine = Regex.Replace(text.Trim(), @"\s+", " ");
            Preview = oneLine.Length > 160 ? oneLine[..160] + "…" : oneLine;
            Time = DateTime.Now.ToString("HH:mm");
        }

        public string Text { get; }
        public string Preview { get; }
        public string Time { get; }
    }
}
