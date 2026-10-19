# Acréscimos

Adição da versão Modificada, **desligada por padrão** e inexistente na Original
(`Regras.Acrescimos`; Configurações > Regras > Adições, chave `acrescimos` do
`config.json`). Desligada, o motor é o 98.003 byte a byte (oráculo e
`PartidaTestes.SemAcrescimosIgualAoOriginal`, que compara com a impressão digital
das jornadas tirada antes da mudança).

## Como funciona (`RetroFoot.Motor/Partida/Acrescimos.cs`)

- No último minuto de cada tempo (45, 90 e, com prorrogação da Taça, 120), depois
  dos lances desse minuto em todos os jogos, cada jogo com as duas equipas sorteia,
  na ordem da lista, com o `Random` da jornada:
  - 1º tempo: `Random(5) + 1` (1 a 5);
  - 2º tempo: `Random(10) + 1` (1 a 10);
  - fim da prorrogação: `Random(3) + 1` (1 a 3).
- O relógio fica parado no minuto (`Jornada.Minuto` = 45/90/120) e
  `Jornada.Acrescimo` anda de 1 até o maior acréscimo sorteado. Em cada tique só
  jogam os jogos com acréscimo até ali, com o mesmo laço de sempre (golo/pênalti,
  expulsão ou cartão, lesão, golo anulado, evolução).
- Depois do último tique vem o fim do tempo: intervalo (a janela de substituição),
  ou, no 90 da Taça, a prorrogação dos que continuam empatados.
- Os lances guardam `Minuto` = 45/90/120 e `Acrescimo` = n (`LanceExtra` também;
  as trocas do intervalo ficam com 0 e aparecem no 46).
- Regras nos acréscimos:
  - o golo usa o minuto corrido (45 + n, 90 + n): "golo só vale se
    `minuto > Random(45)`" vale sempre, como nos minutos finais, e o autor sorteado
    sem golo é só no 45 de verdade;
  - "minuto anterior" (pênalti por expulsão, golo anulado) é o acréscimo anterior
    (o 45'+1' vem depois do 45'), sem a exceção do 1º minuto do tempo
    (`minuto mod 45 <= 1`);
  - a janela da jornada fecha 5 minutos depois do fim, contados depois dos
    acréscimos.
- Saves: os lances e os acréscimos não vão para o save, e a rodada é atômica (o
  save é gravado antes ou depois dela). Um save abre com a opção das
  Configurações daquele momento.

## Tela

`MinutoTexto` (RetroFoot.App): `45'+2'`. Mostrado no relógio do visual Moderno, em
cada jogo da jornada (o jogo que já acabou os seus mostra "Intervalo" ou "Fim"),
no último lance de cada linha, nos detalhes (cabeçalho e linha do tempo) e nas
janelas de substituição. No Clássico o relógio (pizza) fica parado e aparece
`+n'` à esquerda dele.

## Testes

- `PartidaTestes.SemAcrescimosIgualAoOriginal`, `AcrescimosLigados`,
  `RelogioDosAcrescimos`; `SoakTestes.CurtoAcrescimos` (uma temporada).
- `ACRESCIMOS=1 TRAVOU=60 dotnet run --project ferramentas/SoakUI -- 8 mod 1 1 1`.
- Demo: `RETROFOOT_TELA=jornada RETROFOOT_ACRESCIMOS=1 RETROFOOT_MS=300`.
