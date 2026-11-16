using System.Collections.Generic;

namespace Ilha
{
    /// <summary>
    /// Novidades de cada versão, mostradas uma vez na primeira abertura depois de atualizar.
    /// Para uma versão nova: acrescente uma entrada com a versão exata (ex.: "1.10.0").
    /// Os textos são em português; a tradução fica em LocEn.
    /// </summary>
    public static class ReleaseNotes
    {
        /// <param name="Icon">Chave do ícone (Theme.xaml).</param>
        /// <param name="Filled">Ícone cheio (Fill*) ou de traço (Ico*).</param>
        public sealed record Slide(string Icon, bool Filled, string Title, string Body, string[]? Bullets = null);

        public static readonly Dictionary<string, Slide[]> ByVersion = new()
        {
            ["1.10.0"] = new[]
            {
                new Slide("IcoStar", false, "O que há de novo",
                    "A Ilha ficou mais completa e mais dinâmica. Passe para o lado para ver cada novidade.",
                    new[]
                    {
                        "Ilha completa: tudo lado a lado, se você quiser",
                        "Notas em páginas, com cores e formatação",
                        "Agenda com os dias passando de lado",
                        "Tarefas do dia com check",
                        "Barra da música arrastável, aleatório e repetir",
                        "A ilha se ajusta ao que tem dentro",
                    }),
                new Slide("FillLayout", true, "Ilha completa",
                    "Prefere ver tudo de uma vez? Em Configurações → Ilha → Ilha aberta, escolha \"Completa\": música, agenda, tarefas, notas e timer aparecem lado a lado. A forma separada por abas continua sendo a padrão."),
                new Slide("FillNotes", true, "Notas em páginas",
                    "Toque no + para criar uma página, como no Notion. Cada página tem a sua cor (vermelho, verde, amarelo, azul, roxo ou laranja) e aceita negrito, itálico e sublinhado."),
                new Slide("FillCalendar", true, "Agenda de lado",
                    "Os dias agora passam na horizontal. Role o mouse ou clique num dia: ele cresce no centro e os lembretes daquele dia aparecem logo embaixo."),
                new Slide("FillTasks", true, "Tarefas do dia",
                    "Uma lista só para hoje, com check e uma barrinha de progresso. As concluídas saem no dia seguinte; as pendentes continuam com você."),
                new Slide("IcoMusic", false, "Música no seu ritmo",
                    "Segure e arraste a barra da música para escolher o ponto exato. E os novos botões de aleatório e repetir ficam ao lado dos controles."),
                new Slide("FillClip", true, "Uma ilha de verdade dinâmica",
                    "Copiados, tarefas, lembretes e páginas: a ilha cresce e encolhe conforme o que tem dentro. E agora dá para excluir só alguns itens copiados."),
                new Slide("IcoCheck", false, "Ajustes e correções",
                    "Pequenas coisas que deixam tudo mais bonito.",
                    new[]
                    {
                        "Ícone novo, nítido em qualquer tamanho",
                        "Texto mais nítido em toda a ilha",
                        "Esta tela de novidades a cada atualização",
                    }),
            },
        };

        public static Slide[]? For(string version) => ByVersion.TryGetValue(version, out var slides) ? slides : null;
    }
}
