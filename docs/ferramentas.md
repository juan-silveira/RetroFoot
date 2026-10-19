# Pasta de dados, hub e ferramentas do RetroFoot

O RetroFoot não precisa mais do launcher: os dados do jogo ficam numa **pasta de dados** que o
jogador abre no gestor de arquivos (como as pastas `EQUIPAS`, `FLAGS`... ao lado do executável
do jogo clássico) e as ferramentas (Editor de Equipas, Editor de Save, Scout, Patches, Árbitros)
abrem a partir do **hub**, a primeira tela depois do Sobre.

## 1. A pasta de dados

| Onde | Quando |
|---|---|
| `$RETROFOOT_DADOS` | a variável existe (testes, instalações especiais) |
| `<pasta do executável>/RetroFoot` | **modo portátil**: a pasta existe ao lado do executável; o `config.json` e o `opcoes.json` também vão para ela |
| `$XDG_DATA_HOME/retrofoot` | o normal (`~/.local/share/retrofoot` no Linux, `%LOCALAPPDATA%\retrofoot` no Windows) |

O caminho aparece no rodapé do hub e em Configurações > Dados, com o botão **Abrir pasta de
dados** (explorer / open / xdg-open). Código: `RetroFoot.App/DadosJogo.cs` (onde fica, cópia do
pacote, abrir a pasta) e `RetroFoot.Motor/Arquivos/` (formatos, leitura e conversão).

```
retrofoot/
  countries.tsv            países (era COUNTRY.TXE)
  referees.tsv             árbitros (era REFEREE.TXE)
  teams/<PAÍS>/<slug>.json uma equipa por arquivo (era EQUIPAS/*.EFT)
  logos/                   logos de clubes e ligas do jogador (+ logos.tsv, leagues.tsv)
  saves/                   os jogos gravados (.e98)
  patches/                 a biblioteca de patches (.zip) da ferramenta Patches
  country-groups/          bosman.txt e plop.txt (era CTRGROUP/*.TXE)
  flags/                   bandeiras <CÓDIGO>.BMP ou .png (era FLAGS)
  sponsors/                patrocinadores: MARCAS.DAT e os logos (era SPONSORS)
  backup/<data-hora>/      os originais de tudo o que foi convertido
```

**Primeira vez.** O jogo copia para a pasta os dados que vêm com ele (`RetroFoot.App/Assets/Dados`:
as **282 equipas originais do jogo clássico**, os 217 países, as bandeiras de 16 cores, as listas Bosman
e PLOP de 1998, os patrocinadores e os árbitros) **sem nunca substituir um arquivo que já exista**.
O arquivo `.retrofoot-pacote` guarda a versão do pacote (`DadosJogo.VersaoPacote`); quando ela sobe,
só os arquivos que faltam entram (uma equipa do pacote não entra se o jogador já tem outra JSON com
o mesmo `file`). Os `.e98` de `retrofoot/JOGOS` (ou, sem ela, os do launcher) são **copiados** para
`saves/`; os originais ficam onde estavam.

**Arquivos novos sem importar.** O hub e cada jogo novo leem a pasta de novo: basta copiar
arquivos para lá. Nenhum pacote pago (ex.: o pacote Turbo) vai com o jogo; o jogador o aplica na
pasta dele.

### Arquivos do jogo clássico deixados na pasta

`PastaDados.ConverterSoltos` (ao abrir o hub e antes de cada jogo novo) converte para o formato
novo e guarda os originais em `backup/<data-hora>/`:

| Deixado | Vira |
|---|---|
| `EQUIPAS/` (pasta inteira) ou `.EFT` soltos em `teams/` | `teams/<PAÍS>/<slug>.json` |
| `COUNTRY.TXE` na raiz | `countries.tsv` (o anterior fica em `countries.tsv.bak`) |
| `REFEREE.TXE` na raiz | `referees.tsv` (idem `.bak`) |
| `CTRGROUP/` ou `.TXE` em `country-groups/` | `country-groups/bosman.txt`, `plop.txt` |
| `FLAGS/`, `SPONSORS/` (nomes antigos) | arquivos copiados para `flags/`, `sponsors/` (o que for substituído vai para `backup/`) |

Uma equipa é identificada pelo nome do `.EFT` (`file` no JSON): o mesmo `.EFT` outra vez substitui
a JSON dela (mesmo se mudou de país), como copiar por cima na pasta `EQUIPAS`.

### Textos (`countries.tsv`, `referees.tsv`, `country-groups/*.txt`)

UTF-8, uma linha por item, colunas separadas por **TAB** (abre em qualquer editor ou planilha).
Linhas começadas por `#` são comentários; a primeira linha sem `#` dos `.tsv` é o cabeçalho.

```
# RetroFoot: countries. UTF-8, one per line, columns separated by TAB.
code	name
BRA	Brasil
```

- `countries.tsv`: `code` (3 letras, o que as equipas e jogadores usam) e `name` (até 20 letras no jogo).
  Como no original, `POR` entra se faltar.
- `referees.tsv`: `country`, `name` (até 40 letras; letras fora do Latin-1 perdem o acento) e `source`
  (opcional: `fifa`, `liga`, `1998`, `txe`). É a lista final do jogo novo: a do pacote já junta a
  lista FIFA (`Assets/arbitros.tsv`, docs/arbitros.md) com os do `REFEREE.TXE` dos países que ela não
  tem; os do original que a FIFA substituiu estão no fim, comentados (tirar o `#` para usar). Com um
  só país escolhido, só os árbitros dele (como antes).
- `country-groups/bosman.txt` e `plop.txt`: um código por linha (Lei Bosman e língua portuguesa).

### Equipas (`teams/<PAÍS>/<slug>.json`)

Uma equipa por arquivo (arquivos pequenos, fáceis de editar à mão; um patch troca uma equipa só),
numa pasta por país (o código em maiúsculas). O `<slug>` sai do nome completo (minúsculas, sem
acentos nem pontos: `sc-palmeiras.json`). Todos os campos do `.EFT` (docs internos do
launcher, secção 7), em ordem fixa, um jogador por linha:

```json
{
  "format": "retrofoot-team",
  "version": 1,
  "file": "PALMEIRA.EFT",
  "name": "SC PALMEIRAS",
  "shortName": "PALMEIRAS",
  "country": "BRA",
  "level": 13,
  "textColor": "#FFFFFF",
  "backgroundColor": "#008000",
  "coach": "Luiz Felipe Scolari",
  "players": [
    { "name": "Marcos", "position": "G", "country": "BRA" },
    { "name": "Cléber", "position": "D", "country": "BRA" }
  ]
}
```

| Campo | No `.EFT` | Notas |
|---|---|---|
| `format`, `version` | — | `retrofoot-team`, 1 |
| `file` | o nome do arquivo | a ordem das equipas de mesmo nível no jogo novo é a ordem alfabética destes nomes; sem ele, o nome do JSON em maiúsculas + `.EFT` |
| `name`, `shortName` | nome completo e abreviado | |
| `country` | país da equipa | código de `countries.tsv`, guardado como está no `.EFT` (pode vir em minúsculas) |
| `level` | nível inicial (1 a 20) | os maiores entram nas divisões mais altas |
| `textColor`, `backgroundColor` | cores da letra e do fundo | `#RRGGBB` |
| `coach` | treinador | |
| `players[]` | `name`, `position`, `country` | posição `G`, `D`, `M`, `A` (também aceita `GK`, `DF`, `MF`, `FW`, nomes por extenso ou 0–3) |
| `eft` (opcional) | `reserved`, `textColorByte4`, `backgroundColorByte4`, `trailing` | só aparece quando um `.EFT` de patch tem bytes fora do normal; serve para a volta ao `.EFT` ser idêntica |

Não há estádio no `.EFT` (o jogo calcula). Os textos são Latin-1 no jogo: letras fora dele perdem
o acento; até 255 letras. Comentários `//` e vírgulas no fim são aceitos na leitura. As regras do
editor (14 a 20 jogadores, até 5 estrangeiros...) não são verificadas aqui; o jogo novo usa a
equipa como está, como o jogo clássico faz com um `.EFT`. Uma JSON quebrada fica de fora do jogo
novo (as outras continuam).

### Conversor

O mesmo código do jogo (`RetroFoot.Motor/Arquivos`: `EquipaJson`, `TextosDados`, `PastaDados`)
também roda na linha de comando:

```
dotnet run --project ferramentas/Conversor -- importar <patch.zip|pasta|arquivo> <pasta de dados>
dotnet run --project ferramentas/Conversor -- exportar <pasta de dados> <pasta do jogo clássico>
dotnet run --project ferramentas/Conversor -- validar  <patch.zip|pasta> <pasta temporária vazia>
dotnet run --project ferramentas/Conversor -- pacote   <pasta do jogo 98> RetroFoot.App/Assets/arbitros.tsv RetroFoot.App/Assets/Dados
```

- `importar` = `PastaDados.Importar`: zip (nomes Latin-1 dos zips antigos), pasta ou um arquivo; lê
  `.EFT`, `COUNTRY.TXE`, `REFEREE.TXE`, `CTRGROUP/*.TXE`, `FLAGS/*`, `SPONSORS/*`, JSON de `teams/` e
  `logos/` (imagens e as linhas de `logos.tsv`/`leagues.tsv`, que entram no índice do jogador). Num
  patch com a mesma equipa em JSON e em `.EFT` (os do Editor de Equipas), vale o JSON.
- `exportar` = `PastaDados.ExportarEquipas` + `ExportarListas`: `EQUIPAS/*.EFT`, `COUNTRY.TXE`,
  `REFEREE.TXE`, `CTRGROUP/BOSMAN.TXE` e `PLOP.TXE` para levar ao jogo clássico.
- `validar`: importa e confere que cada `.EFT` volta **byte a byte** do JSON. Com o maior pacote da
  comunidade (1710 equipas, 258 países, numa pasta temporária, nada copiado para o repositório):
  1710 convertidas, 1710 idênticas na volta, 166 países com equipas, 0 erros.
- `pacote`: gera `Assets/Dados` a partir das equipas e listas originais de 1998.

Testes: `RetroFoot.Motor.Testes/PastaDadosTestes.cs` (as 282 equipas originais `.EFT` → JSON → `.EFT`
idênticas; a pasta nova dá os mesmos `Dados` e o mesmo jogo novo, byte a byte, que a pasta do
jogo clássico; `.TXE` → texto → `.TXE`; conversão dos arquivos soltos; o pacote = as equipas originais).

## 2. Logos (`logos/`)

Os logos do jogador vêm antes dos do jogo (`Assets/Escudos`), no visual Moderno:

1. `logos/logos.tsv` — `country<TAB>team<TAB>file` (o nome da equipa como no jogo, completo ou
   abreviado; acentos e maiúsculas não importam; `-` no arquivo = sem logo);
2. sem índice: `logos/<PAÍS>_<slug do nome>.png` (ou `.jpg`, `.bmp`), ex. `BRA_sc-palmeiras.png`;
3. um arquivo com o mesmo nome de um do jogo (ex. `brazil_flamengo.png`, os nomes de
   `Assets/Escudos/escudos.tsv`) substitui o do jogo.

Ligas: `logos/leagues.tsv` — `country<TAB>level<TAB>file` (nível 1 a 4, `T` = taça), como
`Assets/Escudos/ligas.tsv`. O hub relê os logos cada vez que aparece (`Escudos.Recarregar`).

**Fundo opaco.** `ferramentas/logos/fundo.py` audita os logos (padrão: `Assets/Escudos`; sem
`--aplicar` só analisa; `--relatorio ARQ` grava o relatório, o último está em
`ferramentas/logos/fundo-relatorio.txt`): borda opaca de um branco uniforme que não chega ao centro
vira transparente (inundação a partir da borda com tolerância, orla por "cor para alfa", sem franja
branca); borda opaca de outra cor ou fundo que entra no desenho só vai para a lista de rever. A
importação de logos do jogador (futuro Editor de Equipas) deve reaproveitar `tirar_fundo(imagem)`
desse script (ou um porte dele para C#). O relatório também lista os logos que ganham o círculo
claro por trás no visual Moderno (`Escudos.Escuro`: média escura e menos de 1/4 dos pontos claros
ou de cor viva; o Flamengo, rubro-negro, não ganha).

## 3. O hub e como registrar uma ferramenta

`RetroFoot.App/TelaHub.cs`: Jogar (o começo de sempre, `TelaInicio`; Cancelar/Esc lá volta ao hub),
as ferramentas, Configurações, Sobre, a pasta de dados e Sair, no visual escolhido (`Visual.cs`):
no Clássico, a janela do Windows 95 sobre o verde do jogo, como o launcher do jogo clássico (o título
do Sobre do original e a coluna de botões com a descrição ao lado); no Moderno, a página inicial
de um launcher no tema escuro (o cartão grande Jogar e um cartão por ferramenta). Em 4:3 (800×600
lógicos) e 16:9 (1067×600), num `Viewbox`.

Cada ferramenta é **um arquivo próprio**; o hub não muda:

```csharp
// RetroFoot.App/FerramentaScout.cs
using System.Runtime.CompilerServices;
namespace RetroFoot.App;

static class FerramentaScout
{
    [ModuleInitializer]
    internal static void Registar() =>
        Ferramentas.Registrar(Ferramentas.Scout, "Scout", "Procure os melhores jogadores", "lupa",
            ctx => new TelaScout(ctx));
}
```

`Ferramentas.Registrar(string id, string titulo, string descricao, string icone,
Func<Ferramentas.Contexto, Control> criar, int? ordem = null)`:

- `id`: `Ferramentas.EditorEquipas` (`equipas`), `EditorSave` (`save`), `Scout` (`scout`), `Patches`
  (`patches`), `Arbitros` (`arbitros`) substituem o "Em breve" deles; um id novo ganha um cartão.
- `titulo`, `descricao`: em pt-PT; o hub passa por `T()` (traduções em
  `Assets/Idiomas/<idioma>/<grupo>.txt`).
- `icone`: um nome de `UiClassico.Icones` / `UiModerno.Icones` (`jogar`, `escudo`, `disquete`,
  `lupa`, `peca`, `apito`, `ajustes`, `config`, `info`, `sair`, `pasta`, `relogio`, `voltar`,
  `fechar`, `bola`, `visto`, `aviso`, `seta`) ou dados de um `Path` 24×24 (cheio, nos dois visuais).
- `criar(ctx)` devolve a página; o hub a põe na página inteira com o título e **Voltar** (no
  Clássico, a janela do Win95 maximizada; no Moderno, a barra de cima escura)
  (Esc também volta). `Ferramentas.Contexto`: `RaizDados` (a pasta de dados, já preparada),
  `Voltar()`, `Mensagem(texto)`, `Largo`, `Largura` (800 ou 1067), `Altura` (600). A área útil
  da página é cerca de 760×470 (4:3) ou 1025×470 (16:9).

`Ui` (`RetroFoot.App/Ui.cs`) dá as peças no visual escolhido na hora de montar: `UiClassico` (as
janelas do Windows 95 como o jogo as desenha: cinzento, relevo, barra de título azul, botões retos
com o glifo colorido, listas e caixas brancas afundadas) ou `UiModerno` (o tema escuro das telas
modernas: cartões arredondados, Inter, ícones Lucide, botões com hover). `Janela(titulo, conteudo,
fechar, icone)`, `Veu(janela, fechar)`, `Caixa(titulo, texto, ok)`, `Cartao`, `Afundado`,
`Botao(texto, clique, principal, icone)`, `Titulo`, `Txt`, `Icone(nome, tam, cor)`, `Lista()`,
`CaixaTexto()`, `Combo()`, `Campo(rotulo, editor)`, `Pagina(controle)` (o tema do Fluent e os
estilos do visual); cores `CorTexto`, `CorFraca`, `CorRotulo`, `CorDestaque`, `CorErro`, `CorBom`,
`CorAviso`. `Ui.AlinharCabecalho(cabecalho, lista)` tira do cabeçalho de uma tabela a largura que
a barra de rolagem tira das linhas (os títulos ficam em cima das colunas). `Clicavel` é o
`Border` clicável (rato, toque, Enter/Espaço) dos cartões, botões e linhas.

## 4. Editor de Save

`RetroFoot.App/FerramentaEditorSave.cs` (a página, registada como `Ferramentas.EditorSave`) sobre
`RetroFoot.Motor/Save/EditorSave.cs` (`SaveEditavel`, `EquipaEditavel`, `JogadorEditavel`,
`LimitesSave`). Porte do Editor de Save e do `SaveCodec` do launcher, mas em cima do `Jogo98`
(o `.e98` lido campo a campo): nada de procurar deslocamentos no arquivo.

- Abre os `.e98` de `saves/` (ou `RETROFOOT_JOGOS`). Edita a inflação (5 a 100, o jogo mostra × 10),
  o dinheiro (com sinal: as dívidas são negativas; −999.999.999 a 999.999.999), a moral (0 a 20),
  o estádio (5.000 a 120.000 lugares), a troca de equipa de um treinador humano (como uma chicotada
  psicológica: as duas equipas ficam com moral 10; só equipas das divisões) e, na ficha do
  jogador, força, salário, nota, lesão, suspensão (S), amarelos acumulados (A), jogos lesionado
  (L) e comportamento. A estrela segue a nota (médio/avançado com nota ≥ 8).
- **Limites de 16 bits** (docs internos do launcher): a ficha mostra a força máxima
  daquele jogador naquele plantel (`LimitesSave.ForcaMaximaNoElenco`) e não aceita um valor que
  estoure o peso de golo do jogador, a soma dos pesos de golo, a soma da força ou a melhor
  escalação; o cartão "Limites de 16 bits" mostra as três contas do plantel.
- **Gravar**: `.bak` do arquivo como estava na 1.ª vez (o original de antes do editor), os bytes
  novos lidos de volta para conferir, gravados num `.tmp` e trocados pelo arquivo. Um campo só é
  regravado se mudou (os reais de 6 e 10 bytes têm mais precisão que um `double`).
- Testes (`EditorSaveTestes`): abrir e gravar sem mudar = os mesmos bytes (todos os saves do
  Oráculo e os reais encontrados), também depois de repor cada valor; as mudanças voltam lidas
  iguais; o `.bak`; a força máxima não estoura nenhum limite e +1 estoura; a troca de equipa.
- O `SaveCodec` do launcher lia o dinheiro sem sinal e, ao gravar, trocava uma dívida por
  999.999.999; aqui uma dívida volta igual.

## 5. Editor de Equipas

`RetroFoot.App/FerramentaEditorEquipas.cs` (a página, registada como `Ferramentas.EditorEquipas`),
`EditorEquipasEscudo.cs` (escudos e logos das ligas), `EditorEquipasUi.cs` (as peças nos dois
visuais), `EscudoArquivo.cs` (abrir e gravar imagens: SkiaSharp e Svg.Skia) sobre
`RetroFoot.Motor/Arquivos/EquipaEditavel.cs` (`EquipaEditavel`, `JogadorEditavel`, `RegrasEquipa`),
`LogosDados.cs`, `PatchEquipas.cs` e `EscudoProcessador.cs`. Porte do Editor de Equipes e do
`TeamCodec` do launcher (e do ramo `editor-escudos`: `TeamEditorEscudo.cs`, `EscudoImagem.cs`).

- **Equipas** de `teams/<PAÍS>/*.json`, por país (com bandeira e contagem) e com busca. Criar,
  editar (nomes, país, nível, treinador, cores das 16 do original ou `#RRGGBB`), apagar (o JSON vai
  para `backup/<data-hora>/`) e os jogadores (posição, país; a nota, a lesão, o comportamento e a
  estrela que o jogo tira do nome aparecem ao vivo). Gravar = o JSON da pasta (muda de pasta se o
  país mudou; uma equipa nova ganha um `.EFT` livre de até 8 letras).
- **Regras do original** (seg12:275e, `RegrasEquipa`): 14 a 20 jogadores, pelo menos 1 guarda-redes
  e 10 de campo, até 5 estrangeiros (Lei Bosman e PLOP de `country-groups/` não contam; tudo
  na Bosman = sem limite), nome completo até 40 e abreviado até 20 letras, nomes de jogadores e
  treinador só com letras Latin-1, nível 1 a 20, país em `countries.tsv`. O painel "Situação para
  o jogo" mostra o que falta; Gravar e Exportar .EFT recusam a equipa com a lista.
- **Exportar .EFT**: o arquivo do formato clássico (o mesmo do `.EFT` de origem byte a byte, os
  bytes fora do normal de um `.EFT` de patch voltam iguais). Sem seletor de arquivos (celular):
  `export/EQUIPAS/` na pasta de dados.
- **Escudo** (aba Escudo): *Escolher imagem…* (seletor do sistema ou arrastar para o quadro),
  *Da pasta de dados…* (as imagens copiadas para `logos/`), *Da coleção…* (os logos de
  `Assets/Escudos`, filtráveis pelo país), *Automático* (o que o jogo acha sozinho) e *Sem escudo*
  (`-` no índice). *Cores do escudo* sugere letra/fundo das 16 cores que mais aparecem nele.
  O escudo só vai para o disco ao Gravar: `logos/<PAÍS>_<slug do nome completo>.png` e a linha em
  `logos/logos.tsv`; mudar o nome ou o país leva a linha junto.
- **Qualquer imagem** (PNG, JPG, WEBP, SVG, BMP, GIF) é adaptada por `EscudoProcessador`: corta a
  borda transparente ou de uma cor; se o fundo é de uma cor só (≥ 85 % da borda), pode tirá-lo
  (opção com prévia: só o que encosta na borda, o branco de dentro do escudo fica, sem halo);
  encosta o escudo nos lados no sentido mais comprido e centra no outro, num quadrado transparente
  de **256×256** (os do jogo são 128×128, também sem margem); grava PNG. Recusa com o motivo: vazia,
  só fundo, menor que 32 px, proporção acima de 3:1, arquivo que não abre. Avisa: menor que 128 px,
  proporção acima de 1,6:1, fundo que ficou, sem transparência. A janela mostra a original, o
  resultado e como fica no cabeçalho da equipa e na Classificação, nos dois visuais (o Clássico
  não mostra logos, como o original: valem as cores).
- **Logos das ligas** (botão na lista): para cada país, 1ª a 4ª divisão e taça, com as mesmas
  opções (imagem adaptada, coleção, automático, sem logo), gravados na hora em `logos/` e
  `logos/leagues.tsv` (`<PAÍS>_league-N.png`, `<PAÍS>_cup.png`).
- **Patches** (`PatchEquipas`): *Exportar…* grava um `.zip` (esta equipa, as do país ou todas) em
  `patches/` ou onde o jogador escolher:

  ```
  retrofoot-patch.json         {"format": "retrofoot-patch", "version": 1, "name", "kind": "teams",
                               "created", "teams", "logos", "eft"}
  teams/<PAÍS>/<slug>.json     as equipas, como na pasta de dados
  logos/<arquivo>.png          os escudos delas (e, opcional, os logos das ligas dos países delas)
  logos/logos.tsv, leagues.tsv só as linhas desses logos
  EQUIPAS/<ARQUIVO>.EFT        (opcional) as mesmas equipas no formato clássico
  ```

  *Importar…* aplica um `.zip` da biblioteca `patches/` (ou escolhido, ou um `.EFT`/`.json`) com
  `PastaDados.Importar` (o mesmo da ferramenta Patches e do conversor): as equipas substituem as de
  mesmo `.EFT`, os logos substituídos vão para `backup/`, as linhas dos índices entram no do jogador.
- Esc fecha a janelinha aberta; com alterações por gravar, pergunta antes de voltar ao hub.
- Testes (`EditorEquipasTestes`): as 282 equipas originais abrem, voltam ao `.EFT` e ao JSON idênticas e
  passam nas regras (só duas vêm sem treinador no original: CRUZEIRO EC e INTER DE LIMEIRA-BR); as regras (cada motivo de recusa, Bosman/PLOP); gravar nova, mudar de país e
  apagar; os índices de logos; o patch de ida e volta (JSON e `.EFT`, logos, índices); os escudos
  (JPG de fundo branco, PNG transparente, recusas e avisos, cores sugeridas).


## 5. Scout

`RetroFoot.App/FerramentaScout.cs` (registada como `Ferramentas.Scout`) e `RetroFoot.App/ScoutScore.cs`
(o `Score.cs` do launcher, mesmos coeficientes). Só leitura: abre um `.e98` de `saves/` (ou
`RETROFOOT_JOGOS`) com `Jogo98.Ler` e lista **todos os jogadores do save**.

- Colunas (clique no título ordena só por ela, outro clique inverte; Shift+clique junta colunas, ¹²³ mostram a ordem): posição,
  nome, estrela, **Score**, **Impacto**, força, nota, lesão, comportamento, equipa, divisão, país
  (bandeira), valor (`MercadoJogo.ValorDoJogador`, na moeda do save), salário, jogos, golos,
  lesões, amarelos na carreira, expulsões e situação (S/A/L, Bosman, PLOP, estrangeiro).
- Filtros (painel que se esconde no 4:3): nome, posições G/D/M/A, país do jogador, divisão,
  equipa, força mín./máx., nota mínima, lesão máxima, comportamento máximo, valor máximo, só
  estrelas, só estrangeiros, esconder suspensos/lesionados, esconder equipas humanas.
  **Idade**: o `.e98` não guarda idade, por isso não há filtro (o "?" diz isso).
- **Score** (0 a 1000) = Nota × 100 × a parte da época (14 jogos) em que ele joga, descontando
  0,105/0,13 jogo por ponto de Lesão (G/outros) e 0,17/0,21 por ponto de Comportamento.
  **Impacto** = pontos por época a mais ou a menos que um jogador de referência da mesma posição
  (sem estrela, Lesão 0, Fair Play), das simulações do launcher (docs/score.md de lá), com o
  efeito "para-raios". O botão **?** explica cada coluna.
- Duplo clique (ou Enter) abre o jogador e a equipa dele (cores, divisão, treinador, dinheiro,
  plantel); duplo clique no plantel abre outro jogador.

## 6. Árbitros

`RetroFoot.App/FerramentaArbitros.cs` (registada como `Ferramentas.Arbitros`) edita
`referees.tsv` da pasta de dados, a lista que `PastaDados` dá aos jogos novos. O pacote do jogo
traz `Assets/Dados/referees.tsv` (a lista FIFA de `Assets/arbitros.tsv` + os do original dos
países que ela não tem); a cópia na pasta de dados é a do jogador.

- Acrescentar (Novo), mudar país e nome, ativar/desativar (o desativado fica **comentado** no
  arquivo, `#CCC<TAB>nome<TAB>origem`, como os do original no fim da lista do pacote: o jogo
  não o usa), remover (Delete), procurar e filtrar por país e por ativos/desativados.
- Grava sozinho 0,7 s depois de cada mudança e ao sair (`TextosDados.GravarArbitrosEditaveis`;
  o anterior fica em `referees.tsv.bak`). Árbitros sem nome não são gravados. Avisos: mais de
  40 letras, letras fora do Latin-1, país fora de `countries.tsv`.
- **Repor a lista do jogo**: copia de novo a do pacote (a atual vai para `.bak`).
- Testes: `ArbitrosEditaveisTestes` (o pacote lido e gravado dá a mesma lista do jogo novo;
  acrescentar, desativar e reativar).

## 5. Patches (`RetroFoot.App/FerramentaPatches.cs`)

O "Aplicar Patch" do launcher (`PatchApplier.cs` e `Patches.cs`) dentro do RetroFoot. A lógica
fica no motor, `RetroFoot.Motor/Arquivos/BibliotecaPatches.cs` (testes:
`RetroFoot.Motor.Testes/PatchesTestes.cs`); a página só mostra e chama.

```
patches/<nome>.zip                       a biblioteca (Importar ou arrastar o .zip para a página copia-o para cá; copiar à mão também vale)
patches/.aplicados/<nome>/registo.json   o que o patch mudou: cada arquivo, se existia, o hash depois
patches/.aplicados/<nome>/antes/<rel>    os arquivos como estavam antes do patch
```

- **Aplicar**: cada entrada do zip vai para o lugar dela no formato novo (o zip pode ter uma pasta
  por fora; nomes Latin-1 dos zips antigos):

  | No zip | Na pasta de dados |
  |---|---|
  | `*.EFT` (em `EQUIPAS/` ou soltos) | `teams/<PAÍS>/<slug>.json` (`PastaDados.GravarEquipa`: o mesmo `file` substitui) |
  | `teams/**/*.json` | idem (passa por `EquipaJson.ParaEft`) |
  | `COUNTRY.TXE` / `countries.tsv` | `countries.tsv` |
  | `REFEREE.TXE` / `referees.tsv` | `referees.tsv` |
  | `CTRGROUP/*.TXE` / `country-groups/*.txt` | `country-groups/<nome>.txt` |
  | `FLAGS/*`, `SPONSORS/*` (ou `flags/`, `sponsors/`) | `flags/`, `sponsors/` |
  | `logos/<arquivo>` (png, jpg, bmp, gif, webp, svg) | `logos/<arquivo>` |
  | `logos/logos.tsv`, `logos/leagues.tsv` | **juntados** ao índice do jogador: a linha da mesma equipa (país + nome normalizado) ou liga (país + nível) é trocada, as outras ficam |
  | `retrofoot-patch.json` (ou `patch.json`) | nome, descrição, autor, versão (não é copiado) |
  | leia-me `.txt`/`.nfo`/`README` | o começo vira a descrição (UTF-8 ou Latin-1) |
  | o resto (EXE, DLL...) | ignorado |
  | caminhos com `..`, `:` ou absolutos | ignorados (nada sai da pasta de dados) |

  A mesma equipa em JSON e em `.EFT` no mesmo zip (o Editor de Equipas pode levar os dois): vale o
  JSON e conta uma vez.

  **Substituir todas as equipas** (marcado por padrão quando um zip clássico, sem manifesto, traz uma
  pasta `EQUIPAS/`, como o jogo clássico pedia: "apenas equipes de um único pacote"): as equipas que o patch não traz saem
  (guardadas em `antes/`). Um erro no meio desfaz o que já foi feito.
- **Reverter**: põe de volta os arquivos de `antes/` e apaga os que o patch criou. Se outro patch
  aplicado **depois** mexeu nos mesmos arquivos, é preciso reverter esse primeiro
  (`Bloqueadores`). Um arquivo mudado depois do patch (editor, à mão) tem a versão atual copiada
  para `backup/<data>-<patch>/` antes de voltar.
- **Conflitos** (`Analisar`, antes de aplicar): os patches aplicados que mexem nas mesmas equipas
  (pelo `file` do `.EFT`) ou nos mesmos arquivos; equipas de países fora de `countries.tsv` (ou da
  lista que o patch traz); quantas equipas são novas, substituídas e (com "substituir todas") retiradas.
- Os jogos gravados não mudam (o save já tem as equipas); o patch vale para os jogos novos.
- Com o maior pacote da comunidade (1710 equipas, 288 bandeiras, `COUNTRY.TXE`, `CTRGROUP`), numa
  pasta temporária: aplicar ~1 s, reverter ~0,3 s, a pasta volta idêntica byte a byte
  (`RETROFOOT_PATCH_TESTE=<zip> dotnet test --filter PatchGrandeDeFora`). Nenhum patch vai com o jogo.

### O patch do RetroFoot (o que o Editor de Equipas exporta)

Um `.zip` com os mesmos nomes da pasta de dados e um `retrofoot-patch.json` (o que
`PatchEquipas.Exportar` grava; `patch.json` também é aceito):

```
retrofoot-patch.json
EQUIPAS/<ARQUIVO>.EFT      (opcional) as mesmas equipas para o jogo clássico; o JSON vale mais
teams/BRA/sc-palmeiras.json
logos/BRA_sc-palmeiras.png
logos/logos.tsv            country<TAB>team<TAB>file (só as linhas destas equipas/logos)
logos/leagues.tsv          (opcional) country<TAB>level<TAB>file
countries.tsv, referees.tsv, country-groups/*.txt, flags/*, sponsors/*   (opcionais)
```

```json
{ "format": "retrofoot-patch", "version": 1, "name": "Brasileirão 2026", "kind": "teams",
  "description": "Séries A e B com logos", "author": "Fulano", "patchVersion": "1.2" }
```

Para criar: `BibliotecaPatches.Criar(raiz, zipDestino, new Manifesto(nome, descricao, autor, versao),
arquivos)`, com os caminhos relativos à pasta de dados (`teams/BRA/x.json`, `logos/x.png`...).
As linhas de `logos.tsv`/`leagues.tsv` que apontam para os logos levados, ou que são das equipas
levadas, entram sozinhas no zip. Os outros campos do manifesto (`kind`, `teams`, `logos`, `eft`,
`created`) são só informação.

A página (`TelaPatches`) existe nos dois visuais: no Clássico os cartões do hub com a lista branca e
a caixa de marcar do Win95; no Moderno o tema escuro do Editor de Save (cartões `#292a2d`, botões
azuis, avisos com a barra colorida à esquerda). Textos em `Assets/Idiomas/<idioma>/patches.txt`.
Testes: `PatchesTestes` (patch clássico aplica e reverte idêntico, conflitos e ordem de reverter,
patch do RetroFoot com logos, o zip do Editor de Equipas com JSON + `.EFT` e entradas `../`).
