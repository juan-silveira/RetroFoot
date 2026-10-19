/* Gera tabelas de referência com long double do x87 (gcc, x86-64 Linux:
   mantissa de 64 bits, arredondamento ao par), para os testes de motor/partida. */
#include <stdio.h>
#include <stdint.h>
#include <math.h>
#include <stdlib.h>

static long double batedor(int pos, int forca, int nota) {
	int16_t a = (int16_t)(uint16_t)((pos + 1) * forca);
	long double f = (long double)(int16_t)nota / 10.0L;
	f = f + 1.0L;
	long double x = (long double)a * f;
	x = x / 2.0L;
	return rintl(x);
}

int main(int argc, char **argv) {
	FILE *b = fopen("batedor.txt", "w");
	int forcas[] = {200, 255, 300, 333, 500, 999, 1000, 4321, 9999};
	for (int pos = 0; pos < 4; pos++)
		for (int nota = 1; nota <= 10; nota++) {
			for (int f = 1; f <= 120; f++)
				fprintf(b, "%d %d %d %.0Lf\n", pos, f, nota, batedor(pos, f, nota));
			for (int k = 0; k < 9; k++)
				fprintf(b, "%d %d %d %.0Lf\n", pos, forcas[k], nota, batedor(pos, forcas[k], nota));
		}
	fclose(b);

	FILE *m = fopen("media.txt", "w");
	for (int n = 1; n <= 22; n++)
		for (int s = 0; s <= 400; s++)
			fprintf(m, "%d %d %.0Lf\n", s, n, rintl((long double)s / (long double)n));
	fclose(m);

	/* força relativa R e o argumento do Ln, exatos; Q aproximado (logl/powl) */
	FILE *q = fopen("chance.txt", "w");
	srand(12345);
	for (int i = 0; i < 400; i++) {
		unsigned g = rand() % 60, d = rand() % 260, mm = rand() % 260, at = rand() % 260;
		unsigned g2 = rand() % 60, d2 = rand() % 260, m2 = rand() % 260, a2 = rand() % 260;
		if (i % 50 == 0) { d = 30000 + rand() % 30000; at = 40000 + rand() % 20000; } /* estouros de 16 bits */
		uint16_t a = 1, bb = 1;
		long double R = 0;
		for (int k = 1; k <= 3; k++) {
			if (k == 1) { a += d; bb += a2; }
			if (k == 2) { a += mm; bb += m2; }
			if (k == 3) { a += at; bb += (uint16_t)(3 * g2 + d2); }
			long double t = (long double)a / (long double)(uint16_t)(a + bb + 1);
			R = R + t * (long double)(k == 2 ? 2 : 1);
		}
		long double num = ((long double)at + (long double)mm / 2.0L) * 4.0L;
		long double den = ((long double)(uint16_t)(3 * g2 + d2) + (long double)m2 / 2.0L) + 1.0L;
		long double arg = num / den + 1.0L;
		double moral = (rand() % 2001) / 1000.0; /* exato em Real48? não: usado só como float64 */
		long double Q = powl(logl(arg) * R, 1.0L) * sqrtl(sqrtl((long double)moral / 2.0L));
		fprintf(q, "%u %u %u %u %u %u %u %u %.17g %La %La %La\n", g, d, mm, at, g2, d2, m2, a2, moral, R, arg, Q);
	}
	fclose(q);
	return 0;
}
