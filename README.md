# McpNotebookLM

Servidor **MCP** para criar notebooks e enviar documentação ao
[NotebookLM](https://notebook.google.com/) com a **conta Google corporativa**
de quem está usando a máquina.

Conecte **Gemini CLI**, **VS Code**, **Claude Desktop** ou qualquer cliente MCP.
O pacote **não embute** cookie, senha nem token: a sessão fica só no perfil do
usuário, depois de um login no Edge.

Repositório: https://github.com/SouzaMatheus-dev/mcp-notebooklm

O NotebookLM não publica uma API oficial. Este servidor fala com o mesmo
endpoint interno que o site usa (`batchexecute`). O Google pode mudar esse
contrato sem aviso. Não há vínculo com o Google.

---

## O que este pacote faz

- Abre o login Google (Workspace / SSO) numa janela Edge (WebView2)
- Lista e cria notebooks
- Envia documentação em texto/Markdown, arquivo local ou URL
- Substitui uma fonte pelo mesmo título quando a RFC ou o ADR muda
- Devolve o link `https://notebook.google.com/notebook/{id}`

Arquivos aceitos: `.pdf`, `.txt`, `.md`, `.docx`, `.html`, `.csv`, `.epub`.

## O que este pacote não faz

- Não apaga notebook. `atualizar_fonte` remove só a fonte do título informado e envia o conteúdo novo
- Não lê o cofre de cookies do Chrome/Edge do dia a dia
- Não imprime cookie, `SAPISID` nem `NOTEBOOKLM_AUTH_JSON`

---

## Requisitos

| Item | Detalhe |
| --- | --- |
| Sistema | Windows 10/11 |
| .NET | **8+** |
| Login | [WebView2 Runtime](https://developer.microsoft.com/microsoft-edge/webview2/) (já vem no Windows 11) |
| Conta | Google Workspace com acesso ao NotebookLM |

---

## Instalação

```powershell
dotnet tool install --global McpNotebookLM
dotnet tool update --global McpNotebookLM
```

O comando instalado é `mcp-notebooklm`.

Para uma pasta grande, fora do timeout do agente:

```powershell
mcp-notebooklm publicar --pasta C:\docs\adrs --titulo "ADRs" --lote 8
mcp-notebooklm publicar --pasta C:\docs\adrs --notebook <id>
```

A repetição pula o arquivo cujo conteúdo não mudou e troca o que mudou.

### Login corporativo

```powershell
mcp-notebooklm login
```

1. A janela abre `https://notebook.google.com/`.
2. Entre com a conta da empresa (SSO, MFA, o que o Workspace exigir).
3. Quando o NotebookLM carregar, clique em **Salvar sessão**.

A sessão vai para `%USERPROFILE%\.mcp-notebooklm\storage_state.json`.
Trate esse arquivo como senha: não commite, não cole em chat.

Se o acesso condicional bloquear o WebView2, exporte a sessão de um navegador
já autenticado e aponte um destes (de preferência em arquivo local, fora do git):

| Variável | Uso |
| --- | --- |
| `NOTEBOOKLM_STORAGE_STATE` | Caminho de um `storage_state.json` no formato Playwright |
| `NOTEBOOKLM_AUTH_JSON` | O mesmo JSON inline |
| `NOTEBOOKLM_COOKIE` | Header `Cookie` copiado do DevTools, só da sua sessão |
| `NOTEBOOKLM_BASE_URL` | `https://notebook.google.com` ou `https://notebooklm.google.com` |
| `NOTEBOOKLM_AUTHUSER` | Índice da conta Google, padrão `0` |

---

## Configuração no Gemini CLI

Arquivo do usuário: `%USERPROFILE%\.gemini\settings.json`

```json
{
  "mcpServers": {
    "notebooklm": {
      "command": "mcp-notebooklm",
      "timeout": 180000,
      "trust": false
    }
  }
}
```

Não coloque cookie nesse JSON versionado. Se precisar de variável secreta, use
`.gemini/settings.local.json`.

Exemplo: [docs/examples/gemini.json](docs/examples/gemini.json).

---

## Tools

Os nomes abaixo são os do protocolo MCP. Uma chamada com o nome em PascalCase
responde `Unknown tool`.

| Tool | Efeito |
| --- | --- |
| `status_autenticacao` | Diz se a sessão abre o NotebookLM, sem mostrar segredo |
| `listar_notebooks` | Lista id, título e link |
| `criar_notebook` | Cria um notebook e devolve o id que não existia antes da chamada |
| `listar_fontes` | Lista as fontes de um notebook |
| `adicionar_documento_texto` | Cola Markdown ou texto |
| `adicionar_documento_arquivo` | Envia um arquivo local |
| `adicionar_documento_url` | Adiciona página ou YouTube |
| `atualizar_fonte` | Troca a fonte de mesmo título: remove a anterior e envia o texto ou arquivo novo |
| `publicar_documentacao` | Publica um conjunto de fontes. Com `notebookId`, usa o notebook que já existe. Com `substituir=true`, troca as de mesmo título |

Falha de negócio (`NotebookLmException`) ou HTTP volta com `isError: true`.
Uma lista vazia ("Nenhum notebook encontrado") continua sendo sucesso.

`publicar_documentacao` aceita, na mesma chamada:

- `texto` e `tituloTexto` para um único documento
- `textos`: JSON `[{"titulo":"...","conteudo":"..."}]`
- `pasta`: diretório com `.md` ou `.markdown` (o título é o caminho relativo, sem extensão)
- `arquivos` e `urls`, vários itens separados por `|` ou quebra de linha

Sem `notebookId`, `titulo` é obrigatório e um notebook novo é criado. Cada
chamada envia no máximo `lote` fontes (padrão 8, teto 20). A resposta traz o
`notebookId` e, se ainda houver arquivo, pede a mesma chamada de novo. Na
repetição, o título que já está no notebook é pulado. Com `substituir=true`,
use o `inicio` devolvido para o lote seguinte.

Se uma fonte falhar no meio, a resposta traz o id. Repetir a chamada continua
do que falta.

`atualizar_fonte` recebe `notebookId` e um dos dois: `conteudo` com `titulo`,
ou `caminho` do arquivo. O nome comparado é o título da fonte (no arquivo, o
nome do arquivo). Várias fontes com o mesmo título interrompem a chamada.

---

## Limites

- Texto: 450 mil caracteres (`NOTEBOOKLM_MAX_TEXT_CHARS`)
- Arquivo: 50 MB (`NOTEBOOKLM_MAX_FILE_BYTES`)
- Até 300 fontes por publicação, em lotes de 8 (`lote`, no máximo 20)

O caminho do arquivo é resolvido no diretório de trabalho do cliente MCP.
Prefira caminho absoluto.
