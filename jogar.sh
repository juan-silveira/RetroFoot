#!/bin/sh
# Abre o RetroFoot (C#/Avalonia) no Linux. Os dados (equipas, saves, logos, patches...) ficam
# na pasta de dados do jogo ($XDG_DATA_HOME/retrofoot, docs/ferramentas.md): na 1a vez o jogo
# copia para la os dados que vem com ele e os saves de retrofoot/JOGOS (que ficam onde estao).
export DOTNET_ROOT="$HOME/.dotnet-ms" PATH="$HOME/.dotnet-ms:$PATH" DOTNET_CLI_TELEMETRY_OPTOUT=1
cd "$(dirname "$0")" && exec dotnet run --project RetroFoot.Desktop
