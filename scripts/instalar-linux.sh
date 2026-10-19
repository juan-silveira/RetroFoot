#!/bin/sh
# Instala o RetroFoot para o usuario (sem sudo): o executavel publicado em
# ~/.local/opt/retrofoot, o comando ~/.local/bin/retrofoot e o atalho no menu.
# uso: scripts/instalar-linux.sh   (publica sempre antes, para nao instalar um build velho)
set -e
cd "$(dirname "$0")/.."
export DOTNET_ROOT="$HOME/.dotnet-ms" PATH="$HOME/.dotnet-ms:$PATH"
rm -rf dist/linux
dotnet publish RetroFoot.Desktop -c Release -r linux-x64 -o dist/linux
mv dist/linux/RetroFoot.Desktop dist/linux/RetroFoot
DEST="$HOME/.local/opt/retrofoot"
mkdir -p "$DEST" "$HOME/.local/bin" "$HOME/.local/share/applications" "$HOME/.local/share/icons/hicolor/256x256/apps" \
    "$HOME/.local/share/icons/hicolor/512x512/apps"
cp -f dist/linux/* "$DEST/"
chmod +x "$DEST/RetroFoot"
ln -sf "$DEST/RetroFoot" "$HOME/.local/bin/retrofoot"
# o icone da marca (RetroFoot.App/Assets/Marca)
cp -f RetroFoot.App/Assets/Marca/icone-256.png "$HOME/.local/share/icons/hicolor/256x256/apps/retrofoot.png"
cp -f RetroFoot.App/Assets/Marca/icone-512.png "$HOME/.local/share/icons/hicolor/512x512/apps/retrofoot.png"
cat > "$HOME/.local/share/applications/retrofoot.desktop" <<D
[Desktop Entry]
Type=Application
Name=RetroFoot
Comment=Gerenciador de futebol
Exec=$DEST/RetroFoot
Icon=$HOME/.local/share/icons/hicolor/512x512/apps/retrofoot.png
StartupWMClass=RetroFoot.Desktop
Terminal=false
Categories=Game;SportsGame;
D
update-desktop-database "$HOME/.local/share/applications" 2>/dev/null || true
# o menu e a barra de tarefas guardam os icones: refaz o cache
touch "$HOME/.local/share/icons/hicolor"
gtk-update-icon-cache -f -t "$HOME/.local/share/icons/hicolor" 2>/dev/null || true
echo "RetroFoot instalado: menu de aplicativos ou o comando 'retrofoot'"
