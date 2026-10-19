# Montar o pacote do RetroFoot (computador)

Um executavel so, com o .NET e as bibliotecas nativas dentro. As opcoes ficam no
`RetroFoot.Desktop/RetroFoot.Desktop.csproj` (valem so no `publish -c Release -r ...`), entao o
comando e curto:

```sh
export DOTNET_ROOT="$HOME/.dotnet-ms" PATH="$HOME/.dotnet-ms:$PATH"
dotnet publish RetroFoot.Desktop -c Release -r linux-x64 -o saida/linux-x64
dotnet publish RetroFoot.Desktop -c Release -r win-x64   -o saida/win-x64
dotnet publish RetroFoot.Desktop -c Release -r osx-x64   -o saida/osx-x64
```

Sai um arquivo so em cada pasta (`RetroFoot.Desktop` / `RetroFoot.Desktop.exe`).

## O que o csproj liga

- `SelfContained`, `PublishSingleFile`, `IncludeNativeLibrariesForSelfExtract`,
  `EnableCompressionInSingleFile`, `DebugType=none` (o que antes ia na linha de comando).
- `PublishTrimmed` + `TrimMode=full`: o ILLink corta o que nao e usado do .NET e do Avalonia.
  O jogo nao tem XAML nem bindings por nome; os assets (`avares://`) sao recursos e nao sao
  cortados. A unica reflexao do jogo (`typeof(Sessao).GetField("host")`, com nome constante)
  o ILLink entende.
- `JsonSerializerIsReflectionEnabledByDefault=true`: o `EquipaJson` usa
  `JsonSerializer.Serialize(string, opcoes)`; sem isto, exportar equipa daria excecao.
- `EventSourceSupport`, `DebuggerSupport`, `MetadataUpdaterSupport`,
  `HttpActivityPropagationSupport` desligados, `EnableUnsafeBinaryFormatterSerialization=false`.
- No Windows: tira os `.pdb` nativos do Skia/HarfBuzz (105 MB) que o pacote punha ao lado.
- No Linux: `strip` nas `.so` do Skia/HarfBuzz (so a tabela de simbolos; se o `strip`
  faltar, segue sem).

Nao foi ligado (perderia algo): `InvariantGlobalization` (a tela do celular usa pt-BR;
no Linux/Windows o ICU e do sistema, quase nao economiza), `UseSystemResourceKeys` (as
mensagens de erro mostradas ao jogador sumiriam), tirar a fonte Inter (e a fonte padrao).

## Tamanhos (out/2026, .NET SDK 10, net8.0)

| | antes | agora |
|---|---|---|
| linux-x64 executavel | 53,3 MB | 27,5 MB |
| linux-x64 .tar.gz | 46,6 MB | 21,1 MB |
| win-x64 .exe | 54,3 MB (+105 MB de .pdb) | 28,6 MB |
| osx-x64 executavel | 56,3 MB | 30,8 MB |

Para comparar com o pacote sem corte: `-p:PublishTrimmed=false`.

## Conferir depois de mexer

Os avisos `IL2026`/`IL2xxx` do publish apontam reflexao que o corte pode quebrar. Rodar o
executavel publicado (nao o `dotnet run`) e passar pelas telas; trocar idioma/visual/formato
nas Configuracoes; exportar uma equipa.
