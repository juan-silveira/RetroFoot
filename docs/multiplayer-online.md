# Multiplayer online (0.2.0)

Dois ou mais técnicos, cada um no seu aparelho, em lugares diferentes, jogando o mesmo jogo
por turnos: cada um prepara o time e marca **PRONTO**; a rodada só começa quando todos estão
prontos ("Jogador 1 PRONTO · Jogador 2 AGUARDANDO…").

## Decisões

- **Um técnico por aparelho.** O jogo online não mistura técnicos locais (hotseat).
- **O host roda o jogo.** Um dos aparelhos (computador ou Android) roda o motor de verdade
  (`Sessao`/`JogoEmCurso`); os outros são telas remotas que recebem o estado e mandam as
  decisões. Só existe um estado oficial; o que um cliente muda na cópia dele não vale.
- **Firebase gratuito como ponte.** Os aparelhos não se ligam direto (roteador, 4G/5G): todos
  falam com um Firebase Realtime Database (plano Spark, gratuito) por REST e eventos (SSE), com
  `HttpClient`, sem SDK. O Firebase só leva mensagens e o estado; não roda nada.
- **Nada de lockstep.** Rodar o motor em todos os aparelhos e sincronizar só as entradas exigiria
  determinismo exato entre plataformas (`Math.Log/Exp` no `Ext.Pow`, o leilão em `Task.Run`, o
  estado que não vai para o save) — frágil demais.

## Como uma rodada acontece

1. O motor do host chega ao **primeiro** técnico humano da rodada (`Preparar`) e para.
2. O host publica um **retrato** do jogo (os bytes do save, comprimidos, e a rodada) e abre a
   preparação para **todos ao mesmo tempo**.
3. Cada cliente abre a tela do time (`TelaEquipa`, nas duas peles e no celular) numa cópia
   local do retrato. As ações viram **comandos** para o host:
   - **Guardadas e aplicadas no gancho do time** (o motor zera o estado do jogo do time logo
     antes): a escalação final (jogadores e estado), preço do ingresso, empréstimos, estádio,
     observados, opção de leilão do técnico. Os botões de formação usam um sorteio local só para
     montar a escalação; vai a escalação pronta, e o sorteio do host não é tocado.
   - **Executadas no host na hora** (o motor está parado; uma fila no host): vender (leilão),
     renovar contrato, comprar direto. O host responde com o resultado e um retrato novo.
4. Cada um marca **PRONTO**. Com todos prontos, o motor segue: os ganchos dos outros humanos da
   rodada aplicam os comandos guardados e voltam na hora (com a conferência da escalação; um
   jogador vendido no meio sai dela).
5. A rodada é transmitida a todos (placar, lances, minuto). No fim, um retrato novo.

## Durante a rodada

- **Substituição (paralisação):** quando um técnico pede para parar o jogo e substituir,
  **o jogo para para todos** (como no jogo local). Os outros veem "Jogador 1 está fazendo
  substituições…"; o relógio volta quando ele confirma.
- **Intervalo, lesão, goleiro expulso, batedor de pênalti:** a janela abre no aparelho do dono
  do time; os outros aguardam.
- **Pedido de aumento, convite para outro clube, patrocínio:** perguntados ao dono do time. O
  pedido de aumento e os leilões dos times do computador vêm **logo depois da rodada**, antes de
  os técnicos prepararem (`Mundo.Online`: a CPU prepara primeiro, depois os humanos), para nada
  chegar a quem já deu PRONTO. Antes da 1ª rodada (patrocínios do começo) o jogo vai junto com
  a pergunta, para a janela abrir no outro aparelho.
- **Leilões:** humanos nunca dão lance em jogador de outro humano (`PodeComprar`); só disputam
  leilões de times do computador. O leilão abre **para todos os técnicos ao mesmo tempo**
  (`MercadoJogo.LanceDeTodos`, lance fechado) e fica aberto até todos darem o lance ou pularem
  (ou o prazo); quem já deu o lance vê "aguardando os outros técnicos". O host registra os lances
  na ordem sorteada do motor, então o desempate não muda.
- **Troca de clube:** o aparelho segue o técnico; quem aceita um convite (ou é demitido e
  contratado por outro) passa a mandar no time novo na rodada seguinte, com um aviso.
- **Tempo para responder:** cada pergunta tem um prazo (ex.: 45 s) com contagem na tela. Sem
  resposta, vale o padrão do motor: aumento aceito, sem lance, convite e patrocínio recusados,
  substituição automática, melhor batedor.
- **Quem cai:** fica AGUARDANDO; se não voltar a tempo, o time entra de férias (a CPU dirige) até
  ele voltar.

## Regras (anti-trapaça)

- Jogos online ficam numa pasta própria (`data/online/<id>/`), fora da lista de jogos locais e
  das ferramentas: **Editor de Save, Scout, Árbitros e Importar/Exportar não abrem jogo online.**
- Durante o jogo online: sem Gerenciar técnicos, sem mudar Opções/Configurações que mexem nas
  regras, sem "Sair sem salvar". As regras ficam fixas na criação.
- O cliente nunca muda o estado; só o host. A cada rodada o host publica um hash do save e a
  lista de comandos (auditoria).
- Limitação: o host tem o arquivo no aparelho; o jogo impede a edição, mas não ferramentas de
  fora.

## Firebase

- Um projeto Firebase gratuito (plano Spark), criado uma vez pelo dono do RetroFoot:
  Realtime Database + Autenticação anônima. A configuração (URL do banco e chave da Web API)
  vai no app; não é segredo — a proteção são as regras do banco.
- Estrutura: `salas/<código>/` com `info` (regras, versão, host), `jogadores/<uid>` (técnico,
  equipa, estado PRONTO/AGUARDANDO, visto por último), `retrato` (save comprimido + rodada),
  `comandos/<uid>/…`, `perguntas/<uid>`, `respostas/<uid>`, `rodada` (transmissão).
- Regras do banco: cada jogador só escreve nos próprios comandos/respostas/estado; só o host
  escreve o retrato, as perguntas e a rodada.
- **Sem login nem conta.** Entra-se numa sala só com a **chave da sala** (código de 6 letras
  que o host cria e passa aos amigos) e o nome do técnico. A "autenticação anônima" do Firebase
  é invisível: o app cria sozinho uma identidade para o aparelho (guardada nele), só para as
  regras do banco saberem quem escreve o quê.
- **No máximo 6 vagas por sala** — o limite de técnicos humanos do jogo. Cada vaga é um
  técnico num aparelho; com a sala cheia, ninguém mais entra.
- Retrato de ~70–230 KB por rodada, comprimido: folgado no plano gratuito.

## Host no Android e troca de host

O Android pausa apps em segundo plano. O host no celular roda o motor num **serviço em primeiro
plano** (notificação "RetroFoot: jogo online em andamento") para não parar com a tela apagada.

E para nenhuma queda derrubar o jogo, **o host passa para outro jogador**:

- A cada rodada (no ponto em que todos preparam o time) o host publica o **retrato completo**:
  o save, a **semente do sorteio**, as opções e os comandos já recebidos. Qualquer aparelho
  consegue continuar o jogo a partir dele.
- O host manda um sinal de vida a cada poucos segundos. Se ele some (ex.: 30 s), o próximo
  jogador conectado, na ordem de entrada, **assume o host** — a troca é uma escrita condicional
  no Firebase (ETag), então dois não assumem ao mesmo tempo.
- O novo host carrega o último retrato e segue dali:
  - **queda na preparação:** nada se perde (os comandos estão no Firebase);
  - **queda no meio da rodada:** a rodada é jogada de novo a partir do retrato, com a mesma
    semente e os mesmos comandos (o resultado tende a ser o mesmo; entre plataformas diferentes
    pode variar em detalhes).
- O antigo host, ao voltar, entra como jogador comum. O host também pode **passar o host** de
  propósito (ex.: antes de sair do celular).

## Fases

0. **Bases** — `JogoEmCurso` com o estado completo (save + semente + opções); `Sessao` com
   semente fixa; uma interface de comandos do time (`IComandosEquipa`) que as telas usam: local
   chama o motor direto, remoto manda comando; transporte com duas implementações (memória, para
   testes, e Firebase); testes sem tela com o harness do Soak.
1. **MVP** — sala com código, um técnico por aparelho, retrato e preparação remota completa,
   PRONTO/AGUARDANDO, leilão/compra/renovação no host, perguntas da rodada com o padrão,
   resultados ao fim da rodada, ferramentas bloqueadas, host em qualquer aparelho (serviço no
   Android), retrato com a semente a cada rodada (a base da troca de host).
2. **Perguntas remotas** — lances em leilão, aumento, convite, patrocínio, intervalo, lesão,
   batedor, com prazo.
3. **Rodada ao vivo** — transmissão minuto a minuto e a paralisação para substituição que para
   para todos.
4. **Robustez** — troca automática e manual de host, reconexão, férias automáticas de quem
   caiu, hash por rodada.
