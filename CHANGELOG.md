# Changelog

## 0.1.0

- Primeira versão: logs, erros com a pilha do frame mais interno para fora, o código em volta e a cadeia de causas, contexto por requisição atravessando threads e `async`, usuário, `Track`, check-ins de tarefas (`RunJob` e `CheckIn`) e captura dos erros não tratados.
- Do .NET Framework 3.5 ao .NET 10: builds `net35`, `net45` e `netstandard2.0`, TLS 1.2 ligado no .NET Framework e configuração pelo `appSettings`.
- ASP.NET Core 2.1+: `AddRadar`, `x-request-id`, log `http.request` com rota, status e duração, e os erros que escapam do pipeline ou que o `UseExceptionHandler` trata.
- Microsoft.Extensions.Logging, Serilog, NLog e log4net: as propriedades viram atributos e a exceção vira ocorrência com `logMessage`.
- O mesmo protocolo e o mesmo mascaramento dos SDKs de Node e Python, com a mesma impressão digital.
