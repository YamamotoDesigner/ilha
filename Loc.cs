using System;
using System.Globalization;
using System.Threading;
using System.Windows;
using System.Windows.Markup;

namespace Ilha
{
    /// <summary>
    /// Idioma da interface. O texto em português é a própria chave: em inglês, procura a tradução
    /// em <see cref="LocEn"/>; sem tradução, mostra o original.
    /// </summary>
    public static class L
    {
        /// <summary>"pt" ou "en".</summary>
        public static string Lang { get; private set; } = "pt";

        public static bool English => Lang == "en";

        /// <summary>Idioma padrão quando o usuário ainda não escolheu: o do Windows.</summary>
        public static string SystemDefault =>
            CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "pt" ? "pt" : "en";

        /// <summary>Troca o idioma (textos via <see cref="T"/> e formatos de data/número).</summary>
        public static void Set(string? lang)
        {
            Lang = lang == "en" ? "en" : "pt";
            var culture = CultureInfo.GetCultureInfo(English ? "en-US" : "pt-BR");
            CultureInfo.DefaultThreadCurrentCulture = culture;
            CultureInfo.DefaultThreadCurrentUICulture = culture;
            Thread.CurrentThread.CurrentCulture = culture;
            Thread.CurrentThread.CurrentUICulture = culture;
        }

        /// <summary>Formatos dos bindings do WPF (ex.: StringFormat) da janela seguem o idioma escolhido.</summary>
        public static void ApplyTo(FrameworkElement window) =>
            window.Language = XmlLanguage.GetLanguage(CultureInfo.CurrentCulture.IetfLanguageTag);

        public static string T(string pt) =>
            English && LocEn.Strings.TryGetValue(pt, out var en) ? en : pt;

        public static string F(string ptFormat, params object[] args) =>
            string.Format(CultureInfo.CurrentCulture, T(ptFormat), args);
    }

    /// <summary>Texto traduzido no XAML: <c>Text="{local:T 'Configurações'}"</c>.</summary>
    [MarkupExtensionReturnType(typeof(string))]
    public sealed class TExtension : MarkupExtension
    {
        public TExtension() { }
        public TExtension(string text) => Text = text;

        [ConstructorArgument("text")]
        public string Text { get; set; } = "";

        public override object ProvideValue(IServiceProvider serviceProvider) => L.T(Text);
    }
}
