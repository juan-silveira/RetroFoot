using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using RetroFoot.Motor.Online;
using Xunit;

namespace RetroFoot.Motor.Testes.Online;

// O banco da sala na memoria (o modelo do Firebase Realtime Database): gravar, ler, PATCH,
// filhos em ordem, escrita condicional e o observar (o no inteiro primeiro, depois as mudancas)
public class BancoTestes
{
    [Fact]
    public async Task GravarLerEApagar()
    {
        var b = new BancoMemoria();
        await b.Gravar("salas/ABC/info", JsonNode.Parse("""{"host":"u1","vagas":6}"""));
        Assert.Equal(6, (int)(await b.Ler("salas/ABC/info/vagas"))!);
        await b.Atualizar("salas/ABC/info", new JsonObject { ["vagas"] = 5 });
        Assert.Equal("u1", (string)(await b.Ler("salas/ABC/info/host"))!);
        Assert.Equal(5, (int)(await b.Ler("salas/ABC/info/vagas"))!);
        await b.Gravar("salas/ABC/info", null);
        Assert.Null(await b.Ler("salas/ABC"));
    }

    [Fact]
    public async Task AcrescentarEmOrdem()
    {
        var b = new BancoMemoria();
        var k1 = await b.Acrescentar("salas/A/comandos/u2", JsonValue.Create("um")!);
        var k2 = await b.Acrescentar("salas/A/comandos/u2", JsonValue.Create("dois")!);
        Assert.True(string.CompareOrdinal(k1, k2) < 0);
        var o = (JsonObject)(await b.Ler("salas/A/comandos/u2"))!;
        Assert.Equal(new[] { k1, k2 }, new List<string>(System.Linq.Enumerable.Select(o, x => x.Key)));
    }

    [Fact]
    public async Task SoUmAssumeOHost()
    {
        var b = new BancoMemoria();
        await b.Gravar("salas/A/host", JsonValue.Create("u1"));
        var (_, etag) = await b.LerComEtag("salas/A/host");
        Assert.True(await b.GravarSe("salas/A/host", JsonValue.Create("u2"), etag));
        Assert.False(await b.GravarSe("salas/A/host", JsonValue.Create("u3"), etag));
        Assert.Equal("u2", (string)(await b.Ler("salas/A/host"))!);
    }

    [Fact]
    public async Task ObservarMudancas()
    {
        var b = new BancoMemoria();
        await b.Gravar("salas/A/jogadores/u1/estado", JsonValue.Create("AGUARDANDO"));
        var vistos = new List<string>();
        using (b.Observar("salas/A/jogadores", (c, v) => vistos.Add(c + "=" + (v?.ToJsonString() ?? "null"))))
        {
            await b.Gravar("salas/A/jogadores/u1/estado", JsonValue.Create("PRONTO"));
            await b.Gravar("salas/A", JsonNode.Parse("""{"jogadores":{"u2":{"estado":"PRONTO"}}}"""));
            await b.Gravar("salas/B/x", JsonValue.Create(1));
        }
        await b.Gravar("salas/A/jogadores/u1/estado", JsonValue.Create("SAIU"));
        Assert.Equal(new[]
        {
            """/={"u1":{"estado":"AGUARDANDO"}}""",
            "/u1/estado=\"PRONTO\"",
            """/={"u2":{"estado":"PRONTO"}}""",
        }, vistos);
    }
}
