# Tema verde (visual Moderno)

O visual Moderno usa o verde e amarelo da marca e do hub (quadro verde da capa, cartoes
verde-escuros com fio dourado, icones amarelos, Jogar em pilula ouro) no lugar do antigo tema
cinza-escuro. O Classico nao muda.

A paleta unica fica em `RetroFoot.App/TemaVerde.cs` (`TemaVerde.*` como `IBrush`,
`TemaVerde.Cores.*` como `Color`). As paletas de cada arquivo apontam para ela:

| Arquivo | Paleta | Estado |
|---|---|---|
| TelaEquipaModerna.cs / TelaEquipaCampinho.cs | `M*` | ajustado (abas em pilula amarela, Jogar ouro, cartoes com borda) |
| TelaJornadaModerna.cs | `M*`, `FundoLogo` | ajustado (relogio amarelo, cartoes com borda) |
| TelaClassificacaoModerna.cs | `Fundo`, `Cor*` | ajustado (tabela num cartao, Continuar ouro) |
| TelaInicioModerna.cs | `ConfigEstilo` | ajustado (quadro `FundoTela`, a bola no topo, cartoes `CartaoHub`, linhas dos saves, passos) |
| TelaConfiguracoes.cs | `ConfigEstilo` | ajustado (quadro, a bola no topo, secao escolhida com fio amarelo, previa do Moderno verde) |
| ModernoConfig.cs (`ConfigEstilo`, `BotaoConfig`, `EscolhaModerna`, `CaixaModerna`, `AbasModernas`) | `ConfigEstilo` | ajustado: botoes como os do hub (pilula ouro; secundario verde-escuro com fio dourado; o principal desligado fica verde apagado), fichas e abas escolhidas em pilula amarela, dialogos como a caixa do hub (fio amarelo), pinceis do Fluent no tema (interruptor, caixa de marcar, radio, campos, combos abertos, rolagem, dicas) |
| Ui.cs (`Tema`), UiModerno.cs, TelaHub.cs (pagina da ferramenta) | `Tema` | ajustado: as ferramentas no quadro `FundoTela`, cartoes `CartaoHub` com sombra, icone amarelo no circulo escuro, janelinhas `DialogoHub` |
| ClubeModerno.cs, ModernoMercado.cs (`MM`), PartidaModerna.cs, TelaDetalhesModerna.cs, ModernoConfigDialogos.cs, TelaLeilao.cs, TelaEstadio.cs | varias | apontam para os tokens, sem ajuste fino |

## Tokens

| Token | Cor | Uso |
|---|---|---|
| `FundoTela` | degrade radial #11683A -> #0D5A2E -> #0A4A25 | fundo das telas cheias |
| `Fundo` | #0D5A2E | fundo liso (dialogos) |
| `FundoEscuro` | #073A1D | o mais fundo |
| `Barra` | #073A1D | barras de menus e de titulo |
| `Cartao` | #094622 | cartoes (mais escuros que o fundo, como no hub) |
| `CartaoForte` | #0F5A31 | pilulas, campos, botoes secundarios dentro de um cartao |
| `Menu` | #0A4D27 | menus abertos |
| `Borda` | #4E7A2E | o fio dourado-oliva dos cartoes |
| `Linha` | #1A6A3A | separadores, trilhos de barras |
| `Zebra` | #0B4E27 | linhas pares das tabelas |
| `Hover` | #13663C | rato em cima |
| `Selecao` | #1A7442 | linha escolhida (com traco `Destaque`) |
| `Humano` | #3B6B22 | a equipa do jogador nas tabelas |
| `Texto` / `TextoSuave` / `TextoFraco` / `Apagado` | #FFFFFF / #C8E6D2 / #9DC8AB / #5E8C6C | textos e desabilitado |
| `Destaque` / `DestaqueClaro` / `Ouro` | #F8D734 / #FFE46A / #DDA917 | acoes, abas, foco, icones |
| `SobreDestaque` | #0B4A25 | texto sobre o amarelo |
| `DestaqueFundo` | #F8D734 a 18% | etiquetas amarelas |
| `BotaoPrimario` / `BotaoPrimarioHover` | degrade #FCE46A -> #F8D734 -> #DDA917 | pilula do Jogar |
| `Verde` / `Vermelho` / `Laranja` / `Azul` / `Roxo` / `Prata` | #6EE89A / #FF8A80 / #FFB566 / #8EC8FF / #CBA8FF / #D3E2D8 | estados em texto sobre o verde |
| `VerdeForte` / `CinzaForte` / `VermelhoForte` | #22A355 / #5F806B / #E5483B | preenchimentos (vitoria/empate/derrota, zonas) |
| `DiscoLogo` | #E6F0E9 | circulo claro por tras dos logos que somem no verde |
| `Sombra` / `Veu` | preto 38% / 60% | sombras e veu dos dialogos |
| `CartaoHub` / `BarraHub` | `Cartao` / `Barra` a 80% | cartoes e barras sobre o `FundoTela` (o vidro do hub) |
| `CirculoIcone` | #062A15 a 60% | o circulo escuro dos icones amarelos e dos logos |
| `DialogoHub` | #0A4523 a 97% | a caixa dos dialogos (com fio `Destaque`) |
| `BotaoSecundario` / `BotaoDesligado` | `Barra` a 55% / 70% | botao secundario; o principal desligado |

Cartoes amarelo/vermelho e a cruz da lesao continuam nas cores proprias (#fbc02d/#e53935).

## Logos

`Escudos.PrecisaDisco(bitmap)`: o logo escuro (`Escudos.Escuro`) ou com a borda (pontos
opacos encostados no transparente) mais de metade verde ganha o `DiscoLogo` por tras
(Coritiba, Guarani, Juventude). `Escudos.ControleComDisco` faz o mesmo para controles.

## Capturas

Em `/tmp/claude-1000/verde/` (Xvfb :240, XDG temporarios): `final-equipa-*.png`,
`final-jornada-43.png`, `final-jornada-169.png`, `final-classificacao-43.png`,
`final-classificacao20-169.png`, `final-configuracoes-169.png`, `final-partida-169.png`;
o antes em `antes/`.
