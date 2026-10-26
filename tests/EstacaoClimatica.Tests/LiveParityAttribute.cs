namespace EstacaoClimatica.Tests;

public sealed class ParidadeAoVivoAttribute : FactAttribute
{
    public ParidadeAoVivoAttribute()
    {
        if (Environment.GetEnvironmentVariable("RUN_LIVE_PARITY") != "1")
            Skip = "Paridade ao vivo requer RUN_LIVE_PARITY=1 e URLs dos dois projetos.";
    }
}
