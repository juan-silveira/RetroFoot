namespace RetroFoot.App;

// o minuto de um lance ou do relogio: "45'" ou, nos acrescimos (Jornada.Acrescimos), "45'+2'"
static class MinutoTexto
{
    public static string Texto(int minuto, int acrescimo) => acrescimo > 0 ? $"{minuto}'+{acrescimo}'" : $"{minuto}'";

    // sem a aspa do fim, para as traducoes com "{1}'"
    public static string SemAspa(int minuto, int acrescimo) => acrescimo > 0 ? $"{minuto}'+{acrescimo}" : minuto.ToString();
}
