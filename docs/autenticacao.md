# Autenticação

O NotebookLM autentica com a **sessão do site**, não com um OAuth de API
pública. A conta pode ser Google Workspace: o login acontece no Edge, com o
SSO da empresa.

## Fluxo

```powershell
mcp-notebooklm login
```

A janela usa um perfil WebView2 separado em
`%USERPROFILE%\.mcp-notebooklm\webview2`. Ela não lê os cookies do Chrome ou
do Edge que você usa no dia a dia.

Quando o host for `notebook.google.com` ou `notebooklm.google.com`, clique em
**Salvar sessão**. O arquivo `storage_state.json` fica ao lado, com permissão
do seu usuário do Windows.

## Sessão expirada

`StatusAutenticacao` pede um novo login quando a página cai em
`accounts.google.com` ou o token CSRF some. Rode `mcp-notebooklm login` de novo.

## O que não colocar no git

- `%USERPROFILE%\.mcp-notebooklm\`
- `NOTEBOOKLM_AUTH_JSON`
- `NOTEBOOKLM_COOKIE`
