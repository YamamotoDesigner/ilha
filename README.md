<div align="center">

<img src="docs/assets/icon.png" width="96" alt="Ilha">

# Ilha

### O topo da sua tela, finalmente útil.

A Ilha vive no topo do Windows e se abre com um passar de mouse — música, arquivos, notas,
timer e área de transferência, sempre à mão e nunca no caminho.

[![Baixar para Windows](https://img.shields.io/badge/Baixar_para_Windows-gr%C3%A1tis-7CE3A1?style=for-the-badge&logo=windows&logoColor=white&labelColor=111113)](https://github.com/YamamotoDesigner/ilha/releases/latest)
&nbsp;
[![Site](https://img.shields.io/badge/Conhecer_o_site-%E2%86%92-111113?style=for-the-badge)](https://yamamotodesigner.github.io/ilha/)

[![Versão](https://img.shields.io/github/v/release/YamamotoDesigner/ilha?label=vers%C3%A3o&color=7CE3A1&labelColor=1c1c1e)](https://github.com/YamamotoDesigner/ilha/releases/latest)
[![Downloads](https://img.shields.io/github/downloads/YamamotoDesigner/ilha/total?label=downloads&color=7CE3A1&labelColor=1c1c1e)](https://github.com/YamamotoDesigner/ilha/releases)
![Windows 10 e 11](https://img.shields.io/badge/Windows-10_%7C_11-7CE3A1?labelColor=1c1c1e)

<br>

<img src="docs/assets/screen.png" alt="A Ilha aberta no topo da tela, mostrando a música tocando e a prateleira de arquivos" width="900">

</div>

<br>

## ✨ Tudo que você abre dez vezes por dia — a um gesto de distância

<img src="docs/assets/tabs.png" alt="As quatro abas da Ilha: mídia e prateleira, notas, timer e área de transferência" width="100%">

| | |
|---|---|
| 🎵 **Controle de mídia** | Capa, título, progresso e controles do que estiver tocando — Spotify, YouTube no navegador, Media Player e qualquer app compatível com o Windows. |
| 📂 **Prateleira de arquivos** | Arraste arquivos para a ilha e solte onde quiser depois. Leve coisas entre pastas, e-mails e apps sem perder nada. |
| 📝 **Notas rápidas** | Um bloco sempre à mão, salvo automaticamente. |
| ⏱️ **Foco** | Modos Foco, Pausa e Pausa longa em um clique. A contagem aparece na ilha recolhida e ela avisa quando acabar. |
| ✅ **Agenda e tarefas** | Calendário do mês inteiro e uma lista de tarefas para o dia, salva automaticamente. |
| 📈 **Monitor do sistema** | Uso de processador e memória com gráfico dos últimos segundos. |
| 📋 **Área de transferência** | Histórico do que você copiou; clique para copiar de novo. Fica só na memória. |
| 🫧 **Vidro ou sólido** | Efeito de vidro com desfoque real do que está atrás (o mesmo do Windows 11), tingido pela sua cor — ou cor sólida, se preferir. |
| 🪞 **Espelho** | Confira o cabelo antes da call: a câmera aparece na ilha e só fica ligada enquanto a aba está aberta. |
| 🔔 **Avisos rápidos** | A ilha recolhida cresce por um instante para mostrar volume, carregador conectado e música nova. |
| ⌨️ **Atalho global** | <kbd>Ctrl</kbd> + <kbd>Alt</kbd> + <kbd>N</kbd> abre e fecha de qualquer lugar. |

## 🎨 Do seu jeito

Visual próprio, com o capricho da Apple: abas num trilho lateral, cantos suaves (nada de pílulas),
fonte fácil de ler, ícones desenhados à mão e animações com mola.
Material vidro ou sólido, intensidade da cor, cor da ilha e de destaque, tamanho, arredondamento, largura e altura da pílula, sombra,
animação com mola, atrasos de abertura e fechamento, monitor, relógio 24 h, quais recursos
aparecem — tudo muda ao vivo. E ela **some sozinha em jogos e vídeos em tela cheia**.

## 🔄 Sempre atualizada

Quando sai uma versão nova, a Ilha avisa. Um clique e ela se atualiza e reabre, com
configurações e notas no lugar. (Dá para desligar em Configurações → Atualizações.)

## 📥 Instalar

1. Baixe o **`Ilha-Setup-x.y.z.exe`** na [página de versões](https://github.com/YamamotoDesigner/ilha/releases/latest).
2. Execute — não precisa de administrador.
3. Se aparecer "O Windows protegeu o computador", clique em **Mais informações → Executar assim mesmo**
   (o app ainda não tem assinatura digital paga).

**Privacidade:** notas, prateleira e configurações ficam só no seu PC (`%APPDATA%\Ilha`).
A única conexão com a internet é a verificação de atualizações.

---

<details>
<summary><b>Para desenvolvedores</b></summary>

<br>

Feito em C# / WPF (.NET 10).

```powershell
dotnet run
```

**Publicar uma nova versão**

1. Aumente `<Version>` em `Ilha.csproj` e `AppVersion` em `installer/Ilha.iss`.
2. Gere o exe e o instalador:
   ```powershell
   Remove-Item publish -Recurse -ErrorAction SilentlyContinue; dotnet publish -c Release -o publish
   & "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe" installer\Ilha.iss
   ```
3. Publique: `gh release create vX.Y.Z Ilha-Setup-X.Y.Z.exe --title "Ilha X.Y.Z" --notes "..."`

Os apps instalados encontram a nova versão em até 6 horas (ou ao abrir).

O site fica em `docs/` (GitHub Pages).

</details>
