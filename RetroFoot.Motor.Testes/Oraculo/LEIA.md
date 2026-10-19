# Casos do oráculo

O jogo original (98.003 puro, com o Randomize fixo em 0: ferramentas/oraculo/)
jogou a partir da entrada: escalação Seleccionar > Automático, pênalti do humano
pelo 1º da lista, intervalo sem trocas, e RetroFoot > Sair na tela da equipa da
jornada seguinte. O resultado gravado pelo original está em *-original.e98.

O motor novo faz o mesmo com:

    go run ./cmd/jornada testdata/oraculo/j3-entrada.e98 /tmp/go.e98
    go run ./cmd/e98 diff testdata/oraculo/j3-original.e98 /tmp/go.e98

(o texto do cabeçalho tem a data e hora: sempre difere.)

- j3: save de 8 equipas por divisão, jornada 3 de 2026, Juan no JUVENTUS-BR
  (4ª divisão). O original gasta 1 Random entre o Randomize e a jornada.

## Casos dos EXEs do launcher

Os casos com `NOME-exe.txt` foram gravados por um `ORACnn.EXE`: o
`ELIFnn.EXE` do launcher (árvore de trabalho de 2026-10-11, com o 3º amarelo
no fim do jogo) passado por `ferramentas/oraculo/oraculo_exe.py`. O teste
liga então `regras.Launcher` (30 jogadores, amarelos, 5-0-5); o N vem do
save. Para refazer um: `ORACULO_EXE=ORAC20.EXE ferramentas/oraculo/casos.sh
NOME=save.e98` (com `ORACULO_TELA` e `ORACULO` próprios). No motor:

    go run ./cmd/jornada -launcher testdata/oraculo/a20-entrada.e98 /tmp/go.e98

- a20 (ORAC20): `~/Downloads/20.e98`, 20 equipas (88 no total), jornada 1 com
  13 compras do humano pendentes: as compras chegam no fim da lista e a
  janela do intervalo reordena o elenco.
- b20 (ORAC20): 20 equipas, 128 no total, jornada 2: Taça na jornada 2 (a
  palavra de baixo de 2^18 é 0) e um jogador editado com força 9999 (a CPU lê
  a tabela do CRC depois de DS:0300).
- c20 (ORAC20): 20 equipas, jornada 2, com amarelos de uma versão anterior do
  patch no save.
- d20 (ORAC20): gravado pelo motor na preparação do jogo da Taça do humano
  (jornada 2, DS:271A = 1); um jogo decidido no prolongamento.
- e20 (ORAC20): gravado pelo motor na jornada 26 (Taça pelo total de
  equipas: 88 > 64).
- y8 (ORAC08): 8 equipas com amarelos (`JOGOS/8 equipes/2.e98` do launcher),
  jornada 3.
- y3 (ORAC08): a entrada do j3 com as regras do launcher.
- f20 (ORAC20): gravado pelo motor na jornada 38: última jornada, final da
  Taça, fim de temporada com 4 que sobem e descem (`subida.asm`) e a 1ª
  jornada de 2027.
- g10, g14, g16 (ORAC10, ORAC14, ORAC16): jogos novos de 10, 14 e 16 equipas
  criados por `motor/novojogo` (todos os países, 128 equipas) e jogados pelo
  motor até a jornada 2; o original joga a jornada 2.
- Pendente (fora daqui): h16, gravado pelo motor na jornada 30 de 16 equipas,
  com o humano rebaixado ao Distrital e desempregado até a 9ª jornada de 2027;
  o original e o motor se separam na preparação da 2ª jornada de 2027 (os
  públicos da jornada 2 batem, os golos não). Causa ainda não achada.
- h14 (ORAC14): gravado pelo motor na jornada 26 de 14 equipas: final da
  Taça, fim de temporada com 2 que sobem e descem (N − 2, N − 4) e a 1ª
  jornada seguinte.
