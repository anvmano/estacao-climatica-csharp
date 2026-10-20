# Estação Climática — C# / .NET

Dashboard web para acompanhamento de dados climáticos da Estação, Sala, Quarto, Aquário e eventos solares. Desenvolvido em C# com ASP.NET Core e .NET 8, consulta o Firebase Realtime Database pelo backend e apresenta gráficos, estatísticas, tabelas, análises ambientais e exportação de dados.

O sistema também oferece um modo público sem login para consultar o clima por CEP, cidade brasileira ou localização do navegador. O dashboard interno usa login Google pelo Firebase Authentication e acesso restrito aos e-mails autorizados.

## Funcionalidades

- Dashboard com abas Estação, Sala, Quarto e Aquário.
- Histórico de medições, médias, mínimos, máximos e tendências.
- Gráficos interativos, calendário mensal e mapas de calor.
- Indicadores de qualidade do ar, ciclo solar, estação do ano e fase da lua.
- Previsão pública de 15 dias, com dados de temperatura, chuva e índice UV.
- Assistente climático com cálculos locais em C# e integração opcional com IA.
- Exportação de relatórios em PDF e dados em JSON e CSV.
- Interface responsiva com navegação acessível.

## Requisitos

- .NET SDK 8 compatível com o projeto.
- Navegador moderno.
- Acesso à internet para consultar Firebase e serviços de clima público.
- Configuração do Firebase para acessar os dados e o login do dashboard interno.

## Como executar

Na pasta raiz do projeto:

```powershell
dotnet restore EstacaoClimatica.sln
dotnet run --project src/EstacaoClimatica.Web/EstacaoClimatica.Web.csproj --launch-profile http
```

Abra [http://localhost:5285](http://localhost:5285) no navegador. Esse endereço corresponde ao perfil `http` de `launchSettings.json`.

## Configuração

Esta é uma cópia preparada para publicação. A chave Firebase, os identificadores do projeto e os e-mails autorizados foram substituídos por valores de exemplo. Para conectar uma instalação própria, preencha esses valores em `appsettings.json`, `InternalAccessOptions.cs` e `wwwroot/scripts/auth.js`. Não envie as configurações reais ao repositório público.

As configurações ficam em `src/EstacaoClimatica.Web/appsettings.json`:

- `Firebase`: endereço do banco, caminhos de leitura e cache.
- `InternalAccess`: controle de acesso e e-mails autorizados.
- `PublicWeather`: serviços de consulta por CEP, cidade e localização.
- `ArtificialIntelligence`: integração opcional do assistente com IA.

Configure chaves secretas por variáveis de ambiente, User Secrets ou cofre externo. Não inclua segredos em arquivos versionados nem em `wwwroot`.

Consulte a [documentação detalhada](docs/README.md) para conhecer as opções e o funcionamento das integrações.

## Estrutura do projeto

```text
src/
  EstacaoClimatica.Domain/          Modelos e DTOs de domínio
  EstacaoClimatica.Application/     Regras de negócio e serviços
  EstacaoClimatica.Infrastructure/  Integrações com Firebase e serviços externos
  EstacaoClimatica.Web/             API ASP.NET Core e frontend em wwwroot
tests/
  EstacaoClimatica.Tests/           Testes automatizados
docs/                              Documentação técnica
```

## Compilação e testes

```powershell
dotnet build EstacaoClimatica.sln
dotnet test EstacaoClimatica.sln
```

Os testes de frontend com Playwright precisam de Microsoft Edge/Chromium disponível.

## Documentação

- [Guia detalhado do projeto](docs/README.md)
- [Arquitetura](docs/ARCHITECTURE_AI.md)
- [Regras de negócio](docs/BUSINESS_RULES.md)
- [API de consultas climáticas](docs/API_CONSULTAS.md)
- [Contrato do modo público](docs/CONTRATO_MODO_PUBLICO.md)
