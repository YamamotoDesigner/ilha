using System.Collections.Generic;

namespace Ilha
{
    /// <summary>
    /// Novidades de cada versão, mostradas uma vez na primeira abertura depois de atualizar.
    /// Cada página tem uma ilustração animada (ver <see cref="WhatsNewArt"/>), um título curto e uma frase.
    /// Para uma versão nova: acrescente uma entrada com a versão exata (ex.: "1.10.0").
    /// </summary>
    public static class ReleaseNotes
    {
        /// <param name="Art">Cena animada: intro, full, notes, agenda, tasks, music, dynamic, icon.</param>
        public sealed record Slide(string Art, string Title, string Body);

        public static readonly Dictionary<string, Slide[]> ByVersion = new()
        {
            ["1.12.0"] = new[]
            {
                new Slide("slim", "Discreta quando você trabalha",
                    "Com um app na frente, a ilha vira um risco fino no topo. Passe o mouse e ela abre. Na área de trabalho, volta ao normal."),
                new Slide("convert", "Solte e converta",
                    "Arraste um arquivo até a ilha e escolha Converter: imagem em PDF, PNG ou JPG, comprimir, PDF em imagens, vídeo e áudio."),
            },
            ["1.11.0"] = new[]
            {
                new Slide("hello", "Bom dia, você!",
                    "Na primeira vez do dia que você passa o mouse, a ilha te cumprimenta pelo nome."),
                new Slide("wellness", "Um cuidado no meio do dia",
                    "Água, uma esticada nas pernas, os dentes, o despertador e a hora de dormir: a ilha lembra no horário certo."),
                new Slide("music", "No tempo certo",
                    "A barra da música acompanha cada segundo, sem atraso. E dá para segurar e arrastar até o ponto exato."),
                new Slide("full", "Tudo à vista, se quiser",
                    "Em Configurações → Ilha, escolha \"Completa\" para ver música, agenda, tarefas, notas e timer juntos."),
                new Slide("notes", "Uma página para cada ideia",
                    "Toque no +, escolha uma cor e escreva. Dá para usar negrito, itálico e sublinhado."),
                new Slide("agenda", "Gire pelos dias",
                    "Passe a rodinha do mouse sobre a agenda. O dia escolhido mostra os seus lembretes."),
                new Slide("tasks", "Feito, feito, feito",
                    "Uma lista só para hoje. O que você marcou sai amanhã; o que ficou, continua."),
                new Slide("dynamic", "Do tamanho do que tem dentro",
                    "Copiou mais? A ilha cresce. Apagou um item? Ela encolhe junto."),
            },            ["1.10.0"] = new[]
            {
                new Slide("intro", "A ilha cresceu",
                    "Mais coisas cabem nela, e ela muda de tamanho conforme o que você está fazendo."),
                new Slide("full", "Tudo à vista, se quiser",
                    "Em Configurações → Ilha, escolha \"Completa\" para ver música, agenda, tarefas, notas e timer juntos."),
                new Slide("notes", "Uma página para cada ideia",
                    "Toque no +, escolha uma cor e escreva. Dá para usar negrito, itálico e sublinhado."),
                new Slide("agenda", "Gire pelos dias",
                    "Passe a rodinha do mouse sobre a agenda. O dia escolhido mostra os seus lembretes."),
                new Slide("tasks", "Feito, feito, feito",
                    "Uma lista só para hoje. O que você marcou sai amanhã; o que ficou, continua."),
                new Slide("music", "Volte naquela parte",
                    "Segure a barra da música e arraste até o ponto exato. Aleatório e repetir estão ali do lado."),
                new Slide("dynamic", "Do tamanho do que tem dentro",
                    "Copiou mais? A ilha cresce. Apagou um item? Ela encolhe junto."),
                new Slide("icon", "Cara nova",
                    "Um ícone desenhado do zero e letras mais nítidas em toda a ilha."),
            },
        };

        public static Slide[]? For(string version) => ByVersion.TryGetValue(version, out var slides) ? slides : null;
    }
}
