namespace EstacaoClimatica.Application.Models;

internal sealed record RegistroClimatico(string DataFirebase, DateTime Data, int Hora, double Valor);
