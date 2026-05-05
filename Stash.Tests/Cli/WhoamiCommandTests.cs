using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Stash.Cli.PackageManager;
using Xunit;

namespace Stash.Tests.Cli;

[Collection("CliTests")]
public class WhoamiCommandTests
{
    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;
        private readonly string _body;

        public FakeHandler(HttpStatusCode statusCode, string body)
        {
            _statusCode = statusCode;
            _body = body;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(_body, Encoding.UTF8, "application/json")
            };
            return Task.FromResult(response);
        }
    }

    private sealed class NetworkFailureHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            throw new HttpRequestException("connection refused");
        }
    }

    private static RegistryClient BuildClient(HttpMessageHandler handler, string token = "tok")
    {
        var http = new HttpClient(handler);
        return new RegistryClient("https://registry.example.com", http, token);
    }

    [Fact]
    public void WhoamiDetailed_Success_ReturnsAllFields()
    {
        const string json = """{"username":"alice","email":"alice@example.com","role":"admin"}""";
        var client = BuildClient(new FakeHandler(HttpStatusCode.OK, json));

        var info = client.WhoamiDetailed();

        Assert.Equal("alice", info.Username);
        Assert.Equal("alice@example.com", info.Email);
        Assert.Equal("admin", info.Role);
    }

    [Fact]
    public void WhoamiDetailed_Unauthorized_ThrowsNotLoggedIn()
    {
        var client = BuildClient(new FakeHandler(HttpStatusCode.Unauthorized, "{}"), token: "expired");

        var ex = Assert.Throws<InvalidOperationException>(() => client.WhoamiDetailed());
        Assert.Contains("Not logged in", ex.Message);
        Assert.Contains("stash pkg login", ex.Message);
    }

    [Fact]
    public void WhoamiDetailed_MissingUsernameField_Throws()
    {
        const string json = """{"email":"alice@example.com"}""";
        var client = BuildClient(new FakeHandler(HttpStatusCode.OK, json));

        var ex = Assert.Throws<InvalidOperationException>(() => client.WhoamiDetailed());
        Assert.Contains("missing 'username' field", ex.Message);
    }

    [Fact]
    public void WhoamiDetailed_NetworkFailure_ThrowsFailedToReach()
    {
        var client = BuildClient(new NetworkFailureHandler());

        var ex = Assert.Throws<InvalidOperationException>(() => client.WhoamiDetailed());
        Assert.StartsWith("Failed to reach registry:", ex.Message);
    }

    [Fact]
    public void WhoamiDetailed_OtherNonSuccess_ThrowsStatusMessage()
    {
        var client = BuildClient(new FakeHandler(HttpStatusCode.InternalServerError, "{}"));

        var ex = Assert.Throws<InvalidOperationException>(() => client.WhoamiDetailed());
        Assert.Contains("500", ex.Message);
    }

    [Fact]
    public void WhoamiDetailed_NullOptionalFields_ReturnsNulls()
    {
        const string json = """{"username":"bob"}""";
        var client = BuildClient(new FakeHandler(HttpStatusCode.OK, json));

        var info = client.WhoamiDetailed();

        Assert.Equal("bob", info.Username);
        Assert.Null(info.Email);
        Assert.Null(info.Role);
    }
}
