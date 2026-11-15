# Ilha

Uma "ilha" no topo da tela do Windows que se expande ao passar o mouse.

**[⬇ Baixar a versão mais recente](https://github.com/YamamotoDesigner/ilha/releases/latest)**
(baixe o `Ilha-Setup-x.y.z.exe` e execute — não precisa de administrador).

## Recursos

- **Ilha no topo**: pílula centralizada no topo; expande com animação ao passar o mouse
  ou com o atalho global **Ctrl+Alt+N**.
- **Início — mídia**: música/vídeo atual de qualquer app que use os controles de mídia do
  Windows (Spotify, Chrome, Edge, Firefox, Media Player…): capa, título, artista, progresso,
  tocar/pausar, anterior/próxima.
- **Início — prateleira**: arraste arquivos/pastas para a ilha para guardá-los; arraste de
  volta para qualquer lugar; duplo clique abre. Persistida entre execuções.
- **Notas**: bloco de anotações salvo automaticamente (Esc para sair da edição).
- **Timer**: atalhos de 1 min a 1 h, +1/+5/−1 min, pausar/retomar; a contagem aparece na
  pílula recolhida e a ilha abre com alerta sonoro quando termina.
- **Área de transferência**: histórico dos textos copiados; clique para copiar de novo.
  Fica só na memória (não é gravado em disco).
- **Ilha recolhida**: mini capa, equalizador animado e contagem do timer (cada um opcional).
- **Relógio, data e bateria** (bateria só aparece em notebooks).

## Personalização (engrenagem na ilha ou clique no ícone da bandeja)

- Cor da ilha e cor de destaque (paletas prontas ou qualquer hex)
- Tamanho geral, arredondamento, largura e altura da pílula recolhida
- Sombra, animação com efeito de mola
- Atrasos para abrir e fechar
- Ligar/desligar cada recurso e aba; tamanho do histórico
- Monitor, ocultar com app em tela cheia, relógio 24 h, segundos, iniciar com o Windows

## Atualizações

O Ilha verifica os [releases deste repositório](https://github.com/YamamotoDesigner/ilha/releases)
ao abrir e a cada 6 horas. Quando há versão nova, aparece **"Atualizar para X"** na ilha e no
ícone da bandeja; ao clicar, o instalador é baixado e roda em silêncio, e o Ilha reabre
atualizado (configurações preservadas). Dá para desligar em Configurações → Atualizações.

### Publicar uma nova versão

1. Aumente `<Version>` em `Ilha.csproj` e `AppVersion` em `installer/Ilha.iss`.
2. Gere o exe e o instalador:
   ```powershell
   dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:DebugType=none -o publish
   & "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe" installer\Ilha.iss
   ```
3. Publique: `gh release create vX.Y.Z Ilha-Setup-X.Y.Z.exe --title "Ilha X.Y.Z" --notes "..."`

## Rodar

```powershell
dotnet run
```

## Gerar um .exe único

```powershell
dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o publish
```

O executável fica em `publish\Ilha.exe`.

Configurações, notas e prateleira ficam em `%APPDATA%\Ilha\settings.json`.
