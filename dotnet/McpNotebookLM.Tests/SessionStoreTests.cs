using McpNotebookLM.Services;

namespace McpNotebookLM.Tests;

public class SessionStoreTests
{
    [Fact]
    public void ParseCookieHeader_assigns_google_domain()
    {
        var cookies = SessionStore.ParseCookieHeader("SID=abc; __Secure-1PSIDTS=def");

        Assert.Equal(2, cookies.Count);
        Assert.Equal(".google.com", cookies[0].Domain);
        Assert.True(SessionStore.HasRequiredCookies(cookies));
    }

    [Fact]
    public void BuildCookieHeader_sends_parent_domain_cookie_to_notebook()
    {
        var cookies = SessionStore.ParseCookieHeader("SID=abc");
        var header = SessionStore.BuildCookieHeader(cookies, new Uri("https://notebook.google.com/"));

        Assert.Equal("SID=abc", header);
    }

    [Fact]
    public void ParseStorage_reads_playwright_shape()
    {
        const string json = """
            {"cookies":[{"name":"SID","value":"abc","domain":".google.com","path":"/","secure":true,"httpOnly":true}]}
            """;

        var cookies = SessionStore.ParseStorage(json);

        Assert.Single(cookies);
        Assert.Equal("SID", cookies[0].Name);
        Assert.Equal("abc", cookies[0].Value);
    }
}
