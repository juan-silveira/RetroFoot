using RetroFoot.Motor.Save;
using static RetroFoot.Motor.Partida.Memoria;
using static RetroFoot.Motor.Partida.Sorteio;

namespace RetroFoot.Motor.Partida;

// Cartoes amarelos do launcher (tools/divisoes/cartoes.asm e cartoes.py do launcher;
// docs internos do launcher, seccao 10). Ligados por Jornada.Amarelos (Regras.CartoesAmarelos).
// Os dados ficam no jogador, nos 2 bytes de cima de dois contadores do Historial que nunca
// passam de 65 535, como nos EXEs do launcher (os saves servem nos dois):
//   +0x21 (Historial[10..12])  palavra: amarelos na carreira (lesoes = so +0x1F..+0x20)
//   +0x25 (Historial[14])      byte: amarelos acumulados
//   +0x26 (Historial[15])      byte: marca do jogo do ultimo amarelo
public static class Cartoes
{
    // Random(1000) < 5 e vermelho direto; de 5 a 49, amarelo (cartoes.py: VERM, LIMITE)
    internal const int Vermelho = 5, Limite = 50;

    public static ushort AmarelosCarreira(Jogador j) => Le16(j.Historial, 10);
    // +0x25 (3 = 1 jogo de suspensao no fim do jogo)
    public static byte AmarelosAcumulados(Jogador j) => j.Historial[14];
    public static byte MarcaAmarelo(Jogador j) => j.Historial[15];
}

public sealed partial class Jornada
{
    // a conta da parte 5 de cartoes.asm, em cl (byte): 2 x jornada (DS:2B40) + 1, mais 80
    // nos anos impares (DS:2B42), mais 1 nos jogos da Taca (+0x2D = 1)
    internal byte MarcaDoJogo(JogoAtual jg)
    {
        byte m = (byte)(((byte)Numero << 1) + 1);
        if ((Ano & 1) != 0) m += 80;
        if (jg.Dados.Taca == TipoJogo.Taca) m++;
        return m;
    }

    // seg03:aa11 com a parte 1 de cartoes.asm (seg03:aa40): um sorteio para cada lado, a casa
    // primeiro, r = Random(1000); se r < 50 e r passa a influencia do arbitro sobre aquele
    // lado, cartao (parte 2). O 1o cartao dado acaba o sorteio do minuto. Com cartao: lance
    // tipo 4 (vermelho) ou 3 (amarelo, o jogador continua em campo); num amarelo nao ha
    // substituicao nem forca a recalcular.
    bool MinutoCartao(JogoAtual jg, int minuto)
    {
        for (int lado = Lado.Casa; lado <= Lado.Visit; lado++)
        {
            short r = (short)R.Random(1000);
            if (r >= Cartoes.Limite || r <= jg.Influencia(lado)) continue;
            var (ok, j, segundo) = Cartao(jg, jg.Equipa[lado]!, r);
            if (!ok) continue;
            if (Escalacao(j!) != EscFora)
            {
                var l = NovoLance(lado, minuto, TipoLance.Amarelo, j!);
                jg.Lances.Add(l);
                AvisarLance(jg, l);
                return true;
            }
            DepoisDeSair(jg, lado, j!, minuto, TipoLance.Expulsao, segundo);
            return true;
        }
        return false;
    }

    // a parte 2 de cartoes.asm (no lugar de seg03:48be) com a parte 5 (o amarelo). O jogador
    // e sorteado como na expulsao (pelo comportamento; Fair Play nunca); r e o sorteio do minuto.
    //  - r < 5: vermelho: sai de campo, suspensao de 1 a 4 jogos pela palavra de cima da
    //    semente (sem chamar o Random), (semente >> 16) & 3 + 1, e +1 nas expulsoes (so os 2
    //    bytes de baixo de +0x23);
    //  - amarelo com a marca do jogo igual a do ultimo amarelo (+0x26): e o 2o neste jogo, e o
    //    arbitro so expulsa 1 vez em 4 (r & 3 = 0): +1 nos amarelos da carreira e o vermelho
    //    acima (Segundo: desenhado com o amarelo atras); nas outras 3 nao ha cartao;
    //  - senao: +1 nos amarelos da carreira, a marca, +1 nos acumulados (o 3o da a suspensao
    //    no fim do jogo).
    (bool Ok, Jogador? J, bool Segundo) Cartao(JogoAtual jg, Equipa e, short r)
    {
        var j = SorteiaJogador(R, Elenco(e), 1, PesoComportamento);
        if (j == null || (short)j.Comportamento <= 0) return (false, j, false);
        bool segundo = false;
        if (r >= Cartoes.Vermelho)
        {
            byte m = MarcaDoJogo(jg);
            if (Cartoes.MarcaAmarelo(j) == m)
            {
                if (((byte)r & 3) != 0) return (false, j, false);
                Poe16(j.Historial, 10, (ushort)(Cartoes.AmarelosCarreira(j) + 1));
                segundo = true;
            }
            else
            {
                Poe16(j.Historial, 10, (ushort)(Cartoes.AmarelosCarreira(j) + 1));
                j.Historial[15] = m;
                j.Historial[14]++;
                return (true, j, false);
            }
        }
        MudarEscalacao(j, EscFora);
        j.Suspensao = (ushort)(((R.Semente >> 16) & 3) + 1);
        Poe16(j.Historial, 12, (ushort)(Le16(j.Historial, 12) + 1));
        return (true, j, segundo);
    }
}
