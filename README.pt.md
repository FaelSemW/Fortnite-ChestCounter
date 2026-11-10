<div align="center">

[English](README.md) · [Français](README.fr.md) · [Español](README.es.md) · **Português (Brasil)**

<img src="docs/icon.png" alt="" width="88">

# Contador de Baús Fortnite (Chest Counter)

[![Windows 10 | 11](https://img.shields.io/badge/Windows-10%20%7C%2011-3a7bd5?style=flat-square)](https://github.com) [![Licença](https://img.shields.io/badge/license-MIT-green?style=flat-square)](LICENSE)

**Conta todos os baús que você abre no Fortnite partida após partida, com uma interface desktop nativa moderna e HUD em jogo.**

[**⬇ Baixar ChestCounter.exe**](../../releases/latest) · Windows 10 / 11 · Gratuito · Arquivo Único (Sem Instalação)

</div>

---

O **Contador de Baús Fortnite** lê passivamente os replays que o próprio Fortnite salva após cada partida e mantém uma contagem 100% precisa e oficial dos baús e tesouros especiais abertos por você. Inclui um aplicativo desktop nativo (WPF) com visual gamer moderno e um overlay opcional translúcido direto sobre a tela do jogo.

> Ele conta com precisão cirúrgica de servidor os baús que **você abriu**. Ele **não** é hack e **não** mostra a localização de baús na ilha.

---

## Como Funciona (e por que é 100% Seguro)

O Fortnite grava automaticamente um **arquivo de replay** de cada partida no seu computador (`%LOCALAPPDATA%\FortniteGame\Saved\Demos`). O Chest Counter apenas lê esses arquivos após a partida ser concluída.

- ✅ **Apenas Leitura Passiva de Arquivos**: Lê os arquivos `.replay` em modo somente leitura. Nunca modifica nem apaga nada.
- ✅ **Zero Modificação no Jogo**: Nenhum arquivo da pasta do Fortnite é alterado ou injetado.
- ✅ **Sem Acesso à Memória (Zero Risco Anti-Cheat)**: A ferramenta nunca injeta DLLs nem lê a memória do processo do Fortnite. É 100% compatível com Easy Anti-Cheat e BattlEye.
- ✅ **100% Privado e Seguro**: Não requer login na Epic Games, não coleta dados e não possui telemetria.
- ✅ **Leve e Otimizado**: Roda em prioridade reduzida com consumo mínimo de memória RAM e CPU.

> [!IMPORTANT]
> **Os Replays devem estar ativados no Fortnite**: No jogo, vá em *Configurações → Jogo (engrenagem) → Gravar Replays: **Ligado***.

---

## Recursos Principais

1. **Aplicativo Desktop Nativo (Sem depender de navegador)**:
   - Dashboard com estatísticas completas: Total de Baús, Partidas Jogadas, Média por Jogo, Recorde em 1 Partida e Tesouros Especiais.
   - Tabela detalhada de histórico com colocação, modo de jogo, barra de progresso visual de baús e nome do arquivo de replay.
   - Filtros rápidos por *Construção Zero*, *Esquadrões*, *Solo* e *Apenas Vitórias Royale 👑*, além de busca em tempo real.
   - Indicador de status do Fortnite em tempo real (*No lobby*, *Em partida*, *Processando replay*).
2. **Sincronização Automática**:
   - Monitoramento instantâneo via sistema de arquivos (`FileSystemWatcher`). Ao voltar para o lobby após a partida, o app detecta e adiciona o jogo ao histórico sozinho em poucos segundos.
3. **Overlay em Jogo Integrado**:
   - Visual HUD transparente estilo minimapa ou cartão clássico.
   - Atalhos práticos: `Ctrl+Shift+O` (Exibir/Ocultar) e `Ctrl+Shift+L` (Travar/Destravar cliques para não atrapalhar mira e tiros).
4. **Sem Limite de Partidas**:
   - Ao contrário do Fortnite que rotaciona e apaga replays antigos do disco após 100 partidas, o banco local do Chest Counter armazena e exibe todas as suas partidas de forma cumulativa e permanente (100, 300, 500+).

---

## Como Usar

1. Baixe o arquivo executável **[`ChestCounter.exe`](../../releases/latest)**.
2. Certifique-se de que a opção de Replays está ativada no seu Fortnite.
3. Dê dois cliques em `ChestCounter.exe` (ou use o atalho criado).
4. Jogue normalmente. Ao concluir a partida e voltar ao Lobby, seus baús aparecerão no histórico automaticamente!

---

## Compilação a partir do Código-Fonte

Requisitos: **.NET 10 SDK** (Windows x64).

```powershell
# Publicar como arquivo executável único independente:
dotnet publish src/ChestCounter/ChestCounter.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o dist
```
