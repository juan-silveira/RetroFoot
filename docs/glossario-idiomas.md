# Glossário dos idiomas

Os termos do jogo nos 4 idiomas, para todas as traduções ficarem iguais. O pt-PT é o
original (jogo clássico, 98.003) e o idioma padrão. Como traduzir: `RetroFoot.App/Idioma.cs`
(o comentário do topo) e os arquivos `RetroFoot.App/Assets/Idiomas/<idioma>/<grupo>.txt`.

Regras gerais

- A chave é o texto em pt-PT, como o original. Se no código ficou em pt-BR, o arquivo pt-PT
  do grupo leva de volta ao original (ex. `Técnico` → `Treinador`).
- pt-BR: os termos do Brasil (tabela do launcher, `pt-BR/base.txt`); "você" em vez do
  tratamento indireto do original; "salvar" em vez de "gravar"; "tela" em vez de "ecrã".
- en: inglês britânico do futebol ("manager", "fixtures", "standings", "season",
  "behaviour").
- es: espanhol de Espanha, tratamento por "tú" nas mensagens ao jogador.
- Nunca traduzir o que vai para o save (nomes de equipas, jogadores, notícias gravadas).
- Textos com `{0}`: a ordem dos argumentos pode mudar na tradução, os números não.
- Textos curtos em colunas estreitas (listas, cabeçalhos): manter o comprimento próximo do
  original; abreviar se for preciso (ver Abreviaturas).

## Equipa e competição

| pt-PT (original) | pt-BR | en | es |
|---|---|---|---|
| Equipa | Time | Team | Equipo |
| Clube | Clube | Club | Club |
| Jornada | Rodada | Round | Jornada |
| Época | Temporada | Season | Temporada |
| Fim de Época | Fim de Temporada | End of season | Fin de temporada |
| Campeonato | Campeonato | League | Liga |
| Divisão / 1ª Divisão | Divisão / 1ª Divisão | Division / 1st Division | División / Primera División |
| 2ª, 3ª, 4ª Divisão | 2ª, 3ª, 4ª Divisão | 2nd, 3rd, 4th Division | Segunda, Tercera, Cuarta División |
| Distrital | Distrital | Regional League | Regional |
| Classificação | Classificação | Standings | Clasificación |
| Taça | Copa | Cup | Copa |
| Vencedor da taça | Campeão da copa | Cup winner | Campeón de copa |
| Oitavos / Quartos de final | Oitavas / Quartas de final | Last 16 / Quarter-finals | Octavos / Cuartos de final |
| Meias-finais / Final | Semifinais / Final | Semi-finals / Final | Semifinales / Final |
| Sorteio | Sorteio | Draw | Sorteo |
| Calendário | Calendário | Fixtures | Calendario |
| Jogo (partida) | Jogo | Match | Partido |
| Jogo (o jogo, um save) | Jogo | Game | Partida |
| Casa / Fora | Casa / Fora | Home / Away | Local / Visitante |
| Sobe / Desce | Sobe / Desce | Promoted / Relegated | Asciende / Desciende |
| Pontos (P) | Pontos (P) | Points (Pts) | Puntos (Pts) |
| Vitórias / Empates / Derrotas | Vitórias / Empates / Derrotas | Won / Drawn / Lost | Ganados / Empatados / Perdidos |
| Golos marcados / sofridos | Gols marcados / sofridos | Goals for / against | Goles a favor / en contra |
| Diferença (saldo) de golos | Saldo de gols | Goal difference | Diferencia de goles |
| Melhor marcador | Artilheiro | Top scorer | Máximo goleador |
| Melhores marcadores de sempre | Maiores artilheiros | All-time top scorers | Máximos goleadores históricos |
| Últimos vencedores | Últimos campeões | Past winners | Últimos campeones |

## Pessoas

| pt-PT | pt-BR | en | es |
|---|---|---|---|
| Treinador | Técnico | Manager | Entrenador |
| Despedimento / foi despedido | Demissão / foi demitido | Sacking / was sacked | Destitución / fue destituido |
| Chicotadas psicológicas | Demissões de técnicos | Managerial sackings | Destituciones de entrenadores |
| Gestão de treinadores | Gestão de técnicos | Manage managers | Gestión de entrenadores |
| Jogador | Jogador | Player | Jugador |
| Plantel | Elenco | Squad | Plantilla |
| Árbitro | Árbitro | Referee | Árbitro |
| Ajuda do Árbitro | Ajuda do Árbitro | Referee Aid | Ayuda del árbitro |

## Posições

| pt-PT | pt-BR | en | es |
|---|---|---|---|
| Guarda-redes | Goleiro | Goalkeeper | Portero |
| Defesa | Defensor (zagueiro: o central) | Defender | Defensa |
| Médio | Meia (meio-campista) | Midfielder | Centrocampista |
| Avançado | Atacante | Forward | Delantero |

"Defesa" também é o lance do guarda-redes (a defesa de um remate): en "save", es "parada",
pt-BR "defesa". Ver o contexto antes de usar a entrada geral.

**Letras das posições** (`Idioma.Posicao`, entradas `posicao:G`/`D`/`M`/`A`; uma letra só,
as colunas não cabem mais):

| | G | D | M | A |
|---|---|---|---|---|
| pt-PT, pt-BR | G | D | M | A |
| en | G | D | M | F |
| es | P | D | M | A |

en: G (goalkeeper), D, M, F (forward). es: P (portero), D (defensa), M (medio), A (atacante:
"D" de delantero colidiria com defensa). Nas táticas (4-4-2, 5-0-5) os números não mudam.

## Jogador (atributos e estado)

| pt-PT | pt-BR | en | es |
|---|---|---|---|
| Nota | Nota | Rating | Nota |
| Risco de lesão | Risco de lesão | Injury risk | Riesgo de lesión |
| Lesionado / lesionou-se | Machucado (lesionado) / se lesionou | Injured / got injured | Lesionado / se lesionó |
| Castigado (suspenso) | Suspenso | Suspended | Sancionado |
| Estrela | Estrela | Star | Estrella |
| Comportamento | Comportamento | Behaviour | Comportamiento |
| Fair Play | Fair Play | Fair Play | Juego limpio |
| Cordeirinho | Cordeirinho | Lamb | Corderito |
| Sarrafeiro | Sarrafeiro (botinudo) | Hatchet man | Leñero |
| Idade | Idade | Age | Edad |
| Salário | Salário | Wage | Sueldo |
| Valor (preço) | Valor (preço) | Value (price) | Valor (precio) |
| Nacionalidade / País | Nacionalidade / País | Nationality / Country | Nacionalidad / País |
| Estrangeiro | Estrangeiro | Foreigner | Extranjero |
| Lei Bosman | Lei Bosman | Bosman ruling | Ley Bosman |

Os rótulos de comportamento são nomes próprios do jogo: traduzir sempre igual nas listas, no
plantel e nas ajudas.

## Partida

| pt-PT | pt-BR | en | es |
|---|---|---|---|
| Golo / GOLO | Gol / GOL | Goal / GOAL | Gol / GOL |
| Golos | Gols | Goals | Goles |
| Penalti | Pênalti | Penalty | Penalti |
| Desempate por penaltis | Disputa de pênaltis | Penalty shoot-out | Tanda de penaltis |
| Ao poste / À barra | Na trave / Travessão | Hit the post / Hit the bar | Al poste / Al larguero |
| Cartão amarelo / vermelho | Cartão amarelo / vermelho | Yellow / red card | Tarjeta amarilla / roja |
| Expulso | Expulso | Sent off | Expulsado |
| Substituição | Substituição | Substitution | Cambio |
| Intervalo | Intervalo | Half-time | Descanso |
| Escalar / Selecção (a equipa do jogo) | Escalar / Escalação | Pick / Line-up | Alinear / Alineación |
| Tática / Formação | Tática / Formação | Formation | Táctica / Formación |
| Assistência (público) | Público | Attendance | Asistencia |

## Dinheiro e clube

| pt-PT | pt-BR | en | es |
|---|---|---|---|
| Leilão | Leilão | Auction | Subasta |
| Transferência | Transferência | Transfer | Traspaso |
| Compra directa | Compra direta | Direct purchase | Compra directa |
| Aquisições | Contratações | Signings | Fichajes |
| Vender | Vender | Sell | Vender |
| Empréstimo (bancário) | Empréstimo | Loan | Préstamo |
| Juros | Juros | Interest | Intereses |
| Balanço | Balanço | Balance sheet | Balance |
| Receitas / Despesas | Receitas / Gastos | Income / Expenses | Ingresos / Gastos |
| Bilhetes / Preço dos bilhetes | Bilheteria / Preço do ingresso | Tickets / Ticket price | Taquilla / Precio de la entrada |
| Prémios | Prêmios | Bonuses | Primas |
| Estádio | Estádio | Stadium | Estadio |
| Bancada / Bancadas | Arquibancada / Arquibancadas | Stand / Stands | Grada / Gradas |
| Lugares (capacidade) | Lugares | Capacity | Aforo |
| Patrocinador principal | Patrocinador master | Main sponsor | Patrocinador principal |
| Equipamento (camisola) | Uniforme | Kit | Equipación |
| Historial | Histórico | History | Historial |

**Moeda.** A moeda vem do jogo (escolhida no jogo novo, gravada no save): o nome e o símbolo
não se traduzem. Só o separador de milhares dos números: pt e es "1.234.567"; en
"1,234,567".

## Interface

| pt-PT | pt-BR | en | es |
|---|---|---|---|
| Ficheiro | Arquivo | File | Archivo |
| Gravar | Salvar | Save | Guardar |
| Abrir | Abrir | Open | Abrir |
| Sair / Sair sem gravar | Sair / Sair sem salvar | Quit / Quit without saving | Salir / Salir sin guardar |
| Acerca... | Sobre... | About... | Acerca de... |
| Opções | Opções | Options | Opciones |
| Configurações | Configurações | Settings | Configuración |
| Registo / Registado | Registro / Registrado | Registration / Registered | Registro / Registrado |
| Ecrã | Tela | Screen | Pantalla |
| Procurar | Procurar | Search | Buscar |
| Jogadores sob observação | Jogadores sob observação | Watched players | Jugadores en observación |
| Sim / Não | Sim / Não | Yes / No | Sí / No |
| OK / Cancelar / Fechar | OK / Cancelar / Fechar | OK / Cancel / Close | Aceptar / Cancelar / Cerrar |
| Jogar | Jogar | Play | Jugar |

## Datas

Meses: pt Janeiro, Fevereiro, Março, Abril, Maio, Junho, Julho, Agosto, Setembro, Outubro,
Novembro, Dezembro (pt-BR igual, com minúscula no meio da frase); en January ... December;
es enero, febrero, marzo, abril, mayo, junio, julio, agosto, septiembre, octubre, noviembre,
diciembre (sempre minúscula). Abreviaturas de 3 letras: pt Jan Fev Mar Abr Mai Jun Jul Ago
Set Out Nov Dez; en Jan Feb Mar Apr May Jun Jul Aug Sep Oct Nov Dec; es ene feb mar abr may
jun jul ago sep oct nov dic. Datas: pt e es "dd/mm/aaaa"; en "dd/mm/yyyy" (britânico).
Ordinais: pt "1ª jornada" / pt-BR "1ª rodada"; en "Round 1"; es "Jornada 1".

## Abreviaturas (colunas)

| pt-PT | pt-BR | en | es |
|---|---|---|---|
| J (jogos) | J | P (played) | PJ |
| V / E / D | V / E / D | W / D / L | G / E / P |
| GM / GS | GP / GC | GF / GA | GF / GC |
| P (pontos) | P | Pts | Pts |
