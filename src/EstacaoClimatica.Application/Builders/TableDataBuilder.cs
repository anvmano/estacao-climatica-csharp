using System.Globalization;
using System.Text.Json;
using EstacaoClimatica.Application.Configuration;
using EstacaoClimatica.Application.Utilities;
using EstacaoClimatica.Domain;

namespace EstacaoClimatica.Application.Builders;

internal static class MontadorDadosTabela
{
    public static TableData Montar(JsonElement? dados, IReadOnlyList<string> cabecalhos)
    {
        // A tabela mostra só os 24 registros mais recentes para manter a tela leve e equivalente ao original.
        var registros = LeitorRegistrosFirebase.EnumerarRegistros(dados, maisRecentesPrimeiro: true).Take(24).ToList();
        var campos = cabecalhos.Skip(2).ToArray();
        var analises = campos.ToDictionary(campo => campo, campo => MontadorQualidadeDados.AnalisarSerie(dados, campo), StringComparer.Ordinal);
        var linhas = registros
            .Select(registro =>
            {
                var linha = new List<string> { registro.Data.Replace("-", "/"), LeitorRegistrosFirebase.FormatarRotuloHora(registro.Horario) };
                linha.AddRange(cabecalhos.Skip(2).Select(chave =>
                    FormatarNullable(NormalizadorMedicoesAquario.NormalizarValorMedicao(chave, LeitorNumerosJson.ObterNumero(registro.Item, chave)))));
                return (IReadOnlyList<string>)linha;
            })
            .ToList();

        var qualidades = registros.Select(registro => (IReadOnlyList<string?>)new string?[] { null, null }
            .Concat(campos.Select(campo =>
            {
                var analise = analises[campo];
                return analise.NiveisPorLeitura.GetValueOrDefault(MontadorQualidadeDados.CriarChaveLeitura(registro, campo)) is { } nivel && nivel != "normal"
                    ? nivel
                    : null;
            })).ToArray()).ToList();

        return new TableData(cabecalhos.Select(ParaRotuloCabecalho).ToList(), linhas, qualidades);
    }

    private static string FormatarNullable(double? valor)
    {
        // "--" é o marcador visual usado pelo frontend para leituras ausentes.
        return valor is null ? "--" : valor.Value.ToString("0.00", CultureInfo.InvariantCulture);
    }

    private static string ParaRotuloCabecalho(string chave)
    {
        return chave switch
        {
            CamposFirebase.Aquario.Temperatura => "Temperatura",
            CamposFirebase.Quarto.SensacaoTermica => "Sensação térmica",
            CamposFirebase.Aquario.Ph => "pH",
            CamposFirebase.Sala.Acetona => "Acetona",
            CamposFirebase.Sala.Alcool => "Álcool",
            CamposFirebase.Sala.Amonia => "Amônia",
            CamposFirebase.Sala.Tolueno => "Tolueno",
            _ => chave
        };
    }
}
