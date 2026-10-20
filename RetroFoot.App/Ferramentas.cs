using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;

namespace RetroFoot.App;

// As ferramentas do hub do RetroFoot (TelaHub.cs): Editor de Equipas, Editor de Save, Scout,
// Patches, Arbitros... Cada uma se regista aqui, num arquivo proprio, sem mexer no hub:
//
//   // RetroFoot.App/FerramentaScout.cs
//   static class FerramentaScout
//   {
//       [System.Runtime.CompilerServices.ModuleInitializer]
//       internal static void Registar() =>
//           Ferramentas.Registrar("scout", "Scout", "Procure jogadores nos saves", "lupa",
//               ctx => new TelaScout(ctx));
//   }
//
// id: um dos ids de baixo substitui o "Em breve" dele; um id novo ganha um cartao no hub.
// titulo/descricao: em pt-PT, traduzidos por T() ao mostrar (pôr as traducoes em
// Assets/Idiomas/<idioma>/<grupo>.txt). icone: um nome de UiModerno.Icones. criar recebe o
// Contexto (a pasta de dados, Voltar ao hub, o formato) e devolve o controle da pagina, que o
// hub poe na pagina inteira, no visual escolhido (Ui.cs), com o titulo e o botao Voltar.
public static class Ferramentas
{
    public sealed record Ferramenta(string Id, string Titulo, string Descricao, string Icone,
        Func<Contexto, Control>? Criar, int Ordem)
    {
        // a versao do celular (RegistrarCelular): a pagina em pe, ao toque, nas duas peles do
        // kit (CelularUi.cs); sem ela, a ferramenta aparece "Em breve" no celular
        public Func<Contexto, Control>? CriarCelular { get; init; }
    }

    // o que a pagina de uma ferramenta recebe
    public sealed class Contexto
    {
        public required string RaizDados { get; init; }          // DadosJogo.Raiz (ja preparada)
        public required Action Voltar { get; init; }             // fecha a ferramenta (volta ao hub)
        public required Action<string> Mensagem { get; init; }   // uma caixa com OK por cima da pagina
        public bool Largo => Formato.Largo;                      // 16:9 (a pagina tem 1067 de largura)
        public double Largura => Formato.Largo ? 1067 : 800;     // a area logica do hub
        public double Altura => 600;
    }

    // ids das ferramentas que o hub ja mostra (sem tela: "Em breve")
    public const string EditorEquipas = "equipas", EditorSave = "save", Scout = "scout",
        Patches = "patches", Arbitros = "arbitros";

    static readonly object _trava = new();
    static readonly Dictionary<string, Ferramenta> _lista = new()
    {
        [EditorEquipas] = new(EditorEquipas, "Editor de Equipas", "Crie e edite equipas, jogadores e cores", "escudo", null, 10),
        [EditorSave] = new(EditorSave, "Editor de Save", "Altere dinheiro, jogadores e equipas de um jogo gravado", "disquete", null, 20),
        [Scout] = new(Scout, "Scout", "Procure os melhores jogadores", "lupa", null, 30),
        [Patches] = new(Patches, "Patches", "Aplique patches de equipas e países", "peca", null, 40),
        [Arbitros] = new(Arbitros, "Árbitros", "Edite a lista de árbitros", "apito", null, 50),
    };

    public static event Action? Mudou;

    // regista (ou substitui, pelo id) uma ferramenta; ordem = a posicao no hub (menor primeiro)
    public static void Registrar(string id, string titulo, string descricao, string icone, Func<Contexto, Control> criar, int? ordem = null)
    {
        lock (_trava)
        {
            int o = ordem ?? (_lista.TryGetValue(id, out var velha) ? velha.Ordem : 100 + _lista.Count);
            _lista[id] = new Ferramenta(id, titulo, descricao, icone, criar, o) { CriarCelular = _celular.GetValueOrDefault(id) };
        }
        Mudou?.Invoke();
    }

    // a versao do celular de uma ferramenta (num arquivo proprio, FerramentaXCelular.cs, com o
    // seu ModuleInitializer): a pagina inteira em pe, que o celular poe sob a barra com o
    // titulo e o voltar (TelaInicioCelular, AbrirFerramentasCelular)
    static readonly Dictionary<string, Func<Contexto, Control>> _celular = new();

    public static void RegistrarCelular(string id, Func<Contexto, Control> criar)
    {
        lock (_trava)
        {
            _celular[id] = criar;
            if (_lista.TryGetValue(id, out var f)) _lista[id] = f with { CriarCelular = criar };
        }
        Mudou?.Invoke();
    }

    public static IReadOnlyList<Ferramenta> Todas
    {
        get { lock (_trava) return _lista.Values.OrderBy(f => f.Ordem).ThenBy(f => f.Id, StringComparer.Ordinal).ToList(); }
    }

    public static Ferramenta? Achar(string id)
    {
        lock (_trava) return _lista.GetValueOrDefault(id);
    }
}
