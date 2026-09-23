# Publicação NuGet

| Campo | Valor |
| --- | --- |
| **PackageId** | `McpNotebookLM` |
| **Tipo** | .NET Global Tool (`DotnetTool`) |
| **Comando CLI** | `mcp-notebooklm` |
| **Target** | `net8.0` (login gráfico só no Windows) |

## Consumir

```powershell
dotnet tool install --global McpNotebookLM
```

## Publicar (mantenedor)

```powershell
dotnet pack dotnet/McpNotebookLM/McpNotebookLM.csproj -c Release -o dotnet/nupkg
dotnet nuget push dotnet/nupkg/McpNotebookLM.0.1.0.nupkg `
  --api-key $env:NUGET_API_KEY `
  --source https://api.nuget.org/v3/index.json
```

Ou crie a tag `v0.1.0` — o workflow `.github/workflows/publish-nuget.yml` publica via Trusted Publisher (`matneves`).

Na primeira publicação do PackageId, cadastre o repositório em nuget.org → Trusted Publishing (mesmo fluxo do `McpKubernetes`).

## Versionamento

Atualize `<Version>` em `dotnet/McpNotebookLM/McpNotebookLM.csproj` e crie a tag:

```powershell
git tag v0.1.0
git push origin v0.1.0
```
