namespace RetroFoot.Motor.Base;

// O Random do Delphi 1 usado pelo jogo classico (runtime, seg25:212d e seg25:218a do
// 98.003). A semente fica em DS:247C (32 bits). Cada chamada a avanca com
//   semente = semente x 0x08088405 + 1   (mod 2^32)
// e Random(n) devolve os 16 bits altos de semente x n vistos como 48 bits, ou seja
// floor(semente x n / 2^32), com a semente ja avancada.
public sealed class Aleatorio
{
    public uint Semente;

    public Aleatorio(uint semente) => Semente = semente;

    public uint Avanca() => Semente = unchecked(Semente * 0x08088405u + 1);

    // Random(n) do Delphi: [0, n). n e um Word no original: so os 16 bits de baixo contam.
    public int Random(int n) => (int)(((ulong)Avanca() * (ushort)n) >> 32);

    // Random sem argumento: a semente (com sinal) vezes 2^-32, mais 0,5.
    public double Real() => (int)Avanca() / 4294967296.0 + 0.5;
}
