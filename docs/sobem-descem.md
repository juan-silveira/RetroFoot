# Sobem e descem por divisão

Opção da versão Modificada (`Regras.SobemDescemEscolhido`; Configurações > Regras >
"Sobem e descem por divisão", chave `sobemDescem` do `config.json`: 0 = Automático,
1 a 4). Na Original são sempre 2, como no 98.003.

- **Automático** (padrão): K = 2, ou 4 com N ≥ 16, a regra dos `ELIFnn.EXE` do
  launcher (`tools/divisoes/subida.asm`). Nada muda em relação a antes: o save não
  ganha marca e o oráculo fica igual.
- **1 a 4**: K fixo, limitado por `Regras.MaximoSobemDescem(N)` = mín(4, (N − 1) / 2),
  ou seja 2K < N (na 2ª a 4ª fica pelo menos uma equipa que nem sobe nem desce).
  Com 8 equipas o máximo é 3: escolher 4 vale 3, e a tela explica isso.

## Onde o K vale

- Fim de temporada (`Epoca.SubidaEDescida`): K = 2 usa o caminho do original
  (dois sorteios do Distrital); outro K usa `SubidaK` (K trocas por nível; do
  Distrital sobem K por sorteio, `Random(n − i)`).
- Mínimo de equipas do jogo novo: `Regras.MinimoEquipas` = 4N + K (seleção de
  países e o texto "o jogo novo precisa de pelo menos X equipas").
- Zonas da Classificação moderna (sobe/desce), o resumo das Configurações e do
  jogo novo e o aviso do jogo em curso.

## No save

O K escolhido fica no jogo (não muda se as Configurações mudarem depois), sem mudar o
formato do `.e98`: vai no último byte (o 21º) do nome do Distrital, um `string[20]` do
Pascal (`Divisao.Nome` da divisão 4). "Distrital" ocupa 10 bytes e o resto nunca é
lido; nos saves do original o 11º byte é um "o" que sobra do "5ª Divisão" e o 21º é
0 em todos os saves do oráculo. Valor 0x40 + K (`Regras.MarcarSobemDescem` /
`SobemDescemDoSave`); outro valor = Automático. `Regras.DoSave` lê o K do save (e
ignora o das Configurações); um save com a marca abre sempre como Modificada, como um
save com N ≠ 8. Não foi verificado se o executável do jogo clássico do launcher preserva o byte (para
ele não importa: usa o próprio K).

Testes: `SobemDescemTestes` (regras, marca, jogo novo, fim de temporada com K = 1 a
4) e `SoakTestes.CurtoSobemDescem` (duas temporadas com recargas do save). O SoakUI
aceita `SOBEM=1..4`.
