# Radar para .NET

SDK do [Radar](https://radar.oconde.dev) para C# e .NET: erros com a pilha e o código em volta, logs, um log `http.request` por requisição, contexto por requisição, tarefas agendadas e mascaramento de dados sensíveis. Funciona do .NET Framework 3.5 ao .NET 10.

**Documentação completa: https://radar.oconde.dev/docs/sdk/dotnet/**

| Pacote | Para |
|---|---|
| `Oconde.Radar` | Qualquer app .NET (núcleo, sem dependências) |
| `Oconde.Radar.AspNetCore` | ASP.NET Core 2.1+ |
| `Oconde.Radar.Extensions.Logging` | Microsoft.Extensions.Logging (`ILogger`) |
| `Oconde.Radar.Serilog` | Serilog |
| `Oconde.Radar.NLog` | NLog |
| `Oconde.Radar.Log4Net` | log4net |

## Instalação

```bash
dotnet add package Oconde.Radar
```

Gere a chave do projeto no painel do Radar e ponha em `RADAR_KEY`. O `Radar.Init()` lê `RADAR_KEY`, `RADAR_ENVIRONMENT`, `RADAR_RELEASE` e `RADAR_ENDPOINT` quando você não passa os valores; no .NET Framework, também o `appSettings` (`Radar:Key`, `Radar:Environment`, `Radar:Release`, `Radar:Endpoint`).

```csharp
using Oconde.Radar;

Radar.Init(new RadarOptions { Release = "1.4.0" });

Radar.Info("pedido.pago", new { orderId, total });
Radar.CaptureException(error, new { orderId });
Radar.RunJob("backup-noturno", () => Backup());
```

## ASP.NET Core

```bash
dotnet add package Oconde.Radar.AspNetCore
dotnet add package Oconde.Radar.Extensions.Logging
```

```csharp
builder.Services.AddRadar(builder.Configuration);
builder.Logging.AddRadar();
```

Cada requisição ganha um `requestId` (o do cabeçalho `x-request-id`, quando vem um, que volta na resposta), os logs e erros dela ficam juntos na linha do tempo, os erros que escapam do pipeline (ou que o `UseExceptionHandler` trata) viram ocorrências e cada resposta grava um log `http.request` com método, rota, status e duração.

## Serilog, NLog e log4net

```csharp
Log.Logger = new LoggerConfiguration().WriteTo.Radar().CreateLogger();
```

```xml
<extensions><add assembly="Oconde.Radar.NLog" /></extensions>
<targets><target xsi:type="Radar" name="radar" /></targets>
```

```xml
<appender name="radar" type="Oconde.Radar.Log4Net.RadarAppender, Oconde.Radar.Log4Net" />
```

Chame `Radar.Init()` uma vez na partida do app. As propriedades do evento viram atributos e a exceção vira ocorrência.

## Licença

MIT
