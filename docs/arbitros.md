# Árbitros do jogo novo

`RetroFoot.App/Assets/arbitros.tsv` (UTF-8, `pais<TAB>nome<TAB>origem`) é a lista de árbitros que o
RetroFoot usa nos **jogos novos**. O `REFEREE.TXE` do jogo original não é alterado.

- Países que estão no `arbitros.tsv` usam só os árbitros dele; os outros países continuam com os
  do `REFEREE.TXE`.
- Se o jogador escolher **um só país** na seleção de países, o jogo fica só com os árbitros desse
  país, **nunca** estrangeiros. O jogo exige `equipas / 2 + 1` árbitros (seg05:3e83); se o país
  tiver menos, os dele repetem-se até esse número (cada cópia é outro árbitro, com outro ID; o
  motor só olha o ID e o país, e o save aceita nomes repetidos). Só um país sem nenhum árbitro
  na lista fica com a lista toda.
- A pasta de dados do jogador (`referees.tsv`) nunca é substituída: quando o pacote muda
  (`DadosJogo.VersaoPacote` 2), entram no fim só os árbitros do pacote que o jogador ainda não
  tem (`TextosDados.AcrescentarArbitros`; a mesma pessoa com nome mais curto ou mais longo,
  ativa ou desativada, conta como já tida).
- Tudo é feito em `ListaArbitros` (RetroFoot.Motor/NovoJogo) antes de `Criacao.Criar`; o motor da
  partida não muda. Saves existentes mantêm os árbitros que já têm.
- Nomes ficam em Latin-1 com no máximo 40 letras (TReferee); letras fora do Latin-1 perdem o
  acento.

## Fontes (consultadas em outubro de 2026)

### `fifa`: lista FIFA de árbitros internacionais 2026

Página de cada uma das 211 federações da FIFA no Inside FIFA, só a categoria *Referee*
masculina: `https://inside.fifa.com/en/associations/<código FIFA>/referees` (ano 2026; a lista
das federações vem de `https://inside.fifa.com/en/associations`). Baixadas em outubro de 2026,
com pelo menos 1,6 s entre pedidos. A FIFA escreve os nomes sem acentos (`SOBRENOME Nome`); foram
convertidos para `Nome Sobrenome`, com partículas em minúsculas nos países de língua portuguesa,
espanhola e holandesa. China, Coreias, Taiwan, Hong Kong, Macau e Vietnã ficam com a família
primeiro. Correções de letras que a FIFA perdeu ou repetiu: Cihan Aydin (AYDN), Gergo Bogar
(Gerg), Aaron O'Dowd, Kevin O'Sullivan, Matthew Cain MacDermid, Bence Csonka (sem o "Dr."),
Eric Guia de la Cruz (sobrenome repetido no nome), Baraa A. Aisha. Nomes com mais de 40 letras
são cortados pelo jogo na última palavra que cabe.

Códigos FIFA -> jogo: todas as 211 federações têm código no `COUNTRY.TXE` do patch (258 países,
igual a `Assets/paises.tsv`). Os que diferem: AIA AGI, ALG AGL, ASA ASM, ATG ANT, AZE AZB,
BAN BGD, BDI BUR, BEN BNI, BFA BKF, BHR BAR, BHU BUT, BIH BOS, BOT BTW, BRB BBD, CAM CBD, CAY CYM,
CGO CNG, CHA CHD, CHI CHL, CIV CMF, CMR CAM, COD ZAI, COM CMR, CPV CAV, CTA RCA, CYP CHP, CZE RCH,
DEN DIN, DJI DJB, DMA DOM, DOM RDO, ECU EQU, EGY EGT, ENG ING, EQG GNQ, ETH ETI, FRO FAR, GAM GMB,
GER ALE, GHA GNA, GNB GBI, GUI GNE, GUY GUI, HAI HTI, JPN JAP, KAZ CQT, KEN QUE, KOR CRS, KSA ASA,
LBN LIB, LBR LBE, LBY LBA, LCA SLU, LIE LTS, LTU LIT, LVA LET, MAC MCO, MAS MAL, MDA MOL, MDV MLD,
MKD MAC, MNE MNG, MNG MGL, MOZ MOC, MRI MRC, MTN MRT, MWI MLW, MYA BIR, NAM NMI, NCA NIC, NED HOL,
NGA NIG, NIG NGE, NIR ILN, NZL NZE, PAK PAQ, PHI FIL, PLE PSE, PRK CRN, PUR PRC, ROU ROM, RSA AFS,
RWA RUA, SCO ESC, SDN SUD, SGP SIN, SLV SAL, SMR SMA, SRB SER, SVK EVQ, SVN EVN, SWE SUE, SWZ SUA,
SYR SIR, TAH PLN, TGA TNG, THA TAI, TJK TJQ, TPE TWN, TRI TND, UAE EAU, UKR UCR, USA EUA, VAN VNT,
VEN VNZ, VGB VGK, VIE VTM, VIN SVG, VIR VGS, YEM IEM, ZIM ZBW.

Sem árbitro masculino na lista 2026 (ficam com os do `REFEREE.TXE`, se houver): Samoa Americana,
Belize, Ilhas Cook, Dominica, Guam, Liechtenstein, Montserrat, Samoa, Taiti, Turcas e Caicos,
Tonga, Ilhas Virgens Americanas.

### `liga`: quadro da 1ª divisão (para os países com muitas equipas no jogo)

Os nomes que também estão na lista FIFA ficam marcados `fifa`, na forma usada na liga (com
acentos).

- Brasil, Série A 2026: árbitros profissionais contratados pela CBF
  (https://www.lance.com.br/futebol-nacional/quem-sao-os-arbitros-profissionais-contratados-pela-cbf.html,
  https://sousaf.com.br/cbf-lanca-programa-de-profissionalizacao-da-arbitragem-72-arbitros-contratados-para-serie-a-em-2026/)
  e escalas da Série A 2026
  (https://10frames.com.br/2026/08/19/cbf-define-escala-de-arbitragem-para-a-25a-rodada-do-brasileirao-serie-a/,
  https://10frames.com.br/2026/03/30/cbf-divulga-escala-de-arbitragem-confira-quem-apita-os-jogos-da-9a-rodada-do-brasileirao-serie-a/,
  https://www.365scores.com/pt-br/news/magazine/arbitragem-rodada-31-brasileirao/).
- Inglaterra, PGMOL Select Group 1 2025/26: https://en.wikipedia.org/wiki/Select_Group e
  https://www.premierleague.com/en/news/4349617/pgmol-confirms-match-official-promotions-for-202526.
- Portugal, árbitros C1 2025/26 (FPF, CO-00011 de 07/07/2025):
  https://www.record.pt/futebol/arbitragem/detalhe/fpf-divulga-arbitros-para-a-nova-epoca-saiba-quem-sao-os-tres-promovidos.
- Itália, organico CAN A/B 2025/26:
  https://www.footballnapolinews.it/organico-2025-26-arbitri-can-serie-a-e-serie-b-5-promossi-dalla-serie-c-ben-10-non-arbitreranno-piu/
  e https://it.wikipedia.org/wiki/Commissione_Arbitri_Nazionale (Piccinini ficou de fora: o
  primeiro nome não foi confirmado).
- França, Ligue 1 2025/26:
  https://ligue1.com/fr/articles/l1_article_2924-saison-2025-2026-presentation-des-arbitres-l1.
- Espanha, Primera División 2025/26:
  https://el1900.com/plantilla-arbitros-primera-division-rfef-cta-2025-2026/ e
  https://iusport.com/art/141155/la-rfef-presenta-la-plantilla-arbitral-de-primera-division-con-tres-nuevos-colegiados.

### `cbf`: árbitros do Brasil (escalas da CBF 2026)

Para um jogo só com o Brasil (até 128 equipas, 65 árbitros), os árbitros centrais (sem
assistentes, quarto árbitro nem VAR) das escalas da CBF de 2026, com o nome completo da fonte. Os
27 que já estavam (FIFA e Série A) ficaram com o nome completo da CBF.

- CBF, quadro FIFA 2026 (seção Árbitros):
  https://www.cbf.com.br/a-cbf/noticias/arbitragem/informes-arbitragem/confira-a-lista-de-arbitros-brasileiros-que-integrarao-quadro-fifa-em-2026
- Escalas da CBF republicadas jogo a jogo (soesporte.com.br, 2026):
  - Série D: https://soesporte.com.br/veja-toda-arbitragem-da-serie-d-do-brasileiro/ ,
    https://soesporte.com.br/escala-de-arbitragem-da-serie-d-2/ ,
    https://soesporte.com.br/escala-de-arbitragem-da-serie-d-3/ ,
    https://soesporte.com.br/escala-de-arbitragem-do-campeonato-brasileiro-serie-d/ ,
    https://soesporte.com.br/eslacao-de-arbitragem-da-serie-d/ ,
    https://soesporte.com.br/escalacao-de-arbitragem-da-quarta-fase-da-serie-d/
  - Série C: https://soesporte.com.br/veja-toda-arbitragem-da-setima-rodada-da-serie-c/ ,
    https://soesporte.com.br/arbitragem-da-sexta-rodada-do-campeonato-brasileiro-da-serie-c/ ,
    https://soesporte.com.br/arbitragem-do-brasileiro-da-serie-c/ ,
    https://soesporte.com.br/escala-de-arbitragem-da-campeonato-brasileiro-serie-c/ ,
    https://soesporte.com.br/escala-de-arbitragem-d-campeonato-brasileiro-serie-c/
  - Série B: https://soesporte.com.br/escala-de-arbitragem-da-serie-b/
- Copa do Brasil 2026 (árbitro de cada jogo): https://en.wikipedia.org/wiki/2026_Copa_do_Brasil ;
  Série A, Copa do Brasil e Copa do Nordeste de
  https://en.wikipedia.org/wiki/2026_Esporte_Clube_Bahia_season e
  https://en.wikipedia.org/wiki/2026_Esporte_Clube_Vit%C3%B3ria_season (sem jogos estaduais).
- Série A 2026 (conferência): https://10frames.com.br/2026/01/26/cbf-divulga-escala-de-arbitragem-da-1a-rodada-do-brasileirao-2026/ ,
  https://10frames.com.br/2026/10/14/cbf-divulga-escala-de-arbitragem-para-a-33a-rodada-do-brasileirao-serie-a/

Cerca de 10 são árbitras do quadro FIFA ou de jogos femininos que também apitam competições da
CBF (inclusive a Série D). Três nomes passam de 40 letras e são cortados na última palavra.

## Cobertura

199 países, 1208 árbitros (956 da FIFA, os quadros das ligas e 180 do Brasil). Só com um país
escolhido: BRA (180) basta para 128 equipas; ING (25), POR (25) e ITA (41) para as equipas do
jogo original; países com menos repetem os seus.
