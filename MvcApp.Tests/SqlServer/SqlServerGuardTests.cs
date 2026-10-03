using Xunit;
using Xunit.Sdk;

namespace MvcApp.Tests.SqlServer;

/// <summary>
/// Tests of the safety rules themselves. They need no database, but carry the SqlServer trait so they run with the
/// SQL Server job and stay out of the regular build-and-test job (whose test count must not change).
/// </summary>
[Trait("Category", "SqlServer")]
public class SqlServerGuardTests
{
    [Theory]
    [InlineData("Server=localhost;User Id=sa;Password=x")]
    [InlineData("Server=localhost,1433;User Id=sa;Password=x")]
    [InlineData("Server=LOCALHOST,1433;User Id=sa;Password=x")]
    [InlineData("Server=127.0.0.1,1433;User Id=sa;Password=x")]
    [InlineData("Server=tcp:localhost,1433;User Id=sa;Password=x")]
    [InlineData("Data Source=127.0.0.1;User Id=sa;Password=x")]
    public void LocalServers_AreAccepted(string connectionString) =>
        SqlServerTestConfig.AssertSafeServer(connectionString);

    [Theory]
    [InlineData("Server=db.example.com,1433;User Id=sa;Password=x")]
    [InlineData("Server=mssql1234.monsterasp.net;User Id=u;Password=x")]
    [InlineData("Server=localhost.evil.example.com;User Id=sa;Password=x")]
    [InlineData("Server=notlocalhost;User Id=sa;Password=x")]
    [InlineData("Server=192.168.1.10;User Id=sa;Password=x")]
    [InlineData("Server=10.0.0.5,1433;User Id=sa;Password=x")]
    [InlineData("Server=(localdb)\\MSSQLLocalDB;Trusted_Connection=True")]
    [InlineData("Server=localhost\\SQLEXPRESS;Trusted_Connection=True")]
    [InlineData("Server=0.0.0.0;User Id=sa;Password=x")]
    public void NonLocalServers_AreRejected(string connectionString) =>
        Assert.Throws<InvalidOperationException>(() => SqlServerTestConfig.AssertSafeServer(connectionString));

    [Theory]
    [InlineData("mvcapp_test_0123456789abcdef")]
    [InlineData("mvcapp_test_a")]
    [InlineData("mvcapp_test_A_b_9")]
    public void TestDatabaseNames_AreAccepted(string name) =>
        SqlServerTestConfig.AssertSafeDatabaseName(name);

    [Theory]
    [InlineData("")]
    [InlineData("master")]
    [InlineData("manfoods")]
    [InlineData("mvcapp_test_")]                          // prefix only
    [InlineData("MVCAPP_TEST_abc")]                       // the prefix is case-sensitive
    [InlineData("xmvcapp_test_abc")]
    [InlineData("mvcapp_test_abc]; DROP DATABASE manfoods; --")]
    [InlineData("mvcapp_test_abc def")]
    [InlineData("mvcapp_test_abc-def")]
    public void AnythingElse_IsRejected(string name) =>
        Assert.Throws<InvalidOperationException>(() => SqlServerTestConfig.AssertSafeDatabaseName(name));

    [Fact]
    public void ForDatabase_AppliesBothGuards_AndReplacesTheCatalog()
    {
        var cs = SqlServerTestConfig.ForDatabase("Server=localhost,1433;Database=master;User Id=sa;Password=x", "mvcapp_test_abc");
        Assert.Contains("mvcapp_test_abc", cs);
        Assert.DoesNotContain("master", cs);

        Assert.Throws<InvalidOperationException>(() => SqlServerTestConfig.ForDatabase("Server=prod.example.com;Database=master", "mvcapp_test_abc"));
        Assert.Throws<InvalidOperationException>(() => SqlServerTestConfig.ForDatabase("Server=localhost;Database=master", "manfoods"));
    }

    [Fact]
    public void AssertSafeConnection_RequiresBothAGuardedServerAndAGuardedDatabase()
    {
        SqlServerTestConfig.AssertSafeConnection("Server=localhost,1433;Database=mvcapp_test_abc;User Id=sa;Password=x");
        Assert.Throws<InvalidOperationException>(() => SqlServerTestConfig.AssertSafeConnection("Server=localhost,1433;Database=manfoods;User Id=sa;Password=x"));
        Assert.Throws<InvalidOperationException>(() => SqlServerTestConfig.AssertSafeConnection("Server=prod.example.com;Database=mvcapp_test_abc;User Id=sa;Password=x"));
        Assert.Throws<InvalidOperationException>(() => SqlServerTestConfig.AssertSafeConnection("Server=localhost,1433;User Id=sa;Password=x")); // no database at all
    }

    [Theory]
    [InlineData(true, false, SqlServerTestConfig.Decision.Run)]
    [InlineData(true, true, SqlServerTestConfig.Decision.Run)]
    [InlineData(false, false, SqlServerTestConfig.Decision.Skip)]          // local run without a database: skip
    [InlineData(false, true, SqlServerTestConfig.Decision.FailMissing)]    // sql-tests job without a connection string: FAIL, never skip
    public void SkipPolicy_FailsLoudlyWhenRequiredAndMissing(bool available, bool required, SqlServerTestConfig.Decision expected) =>
        Assert.Equal(expected, SqlServerTestConfig.Decide(available, required));

    [Fact]
    public void WhenRequired_TheConnectionStringMustBePresent()
    {
        // In the sql-tests job (REQUIRE_SQLSERVER_TESTS=true) this is the explicit, readable failure for a missing variable.
        if (SqlServerTestConfig.Current == SqlServerTestConfig.Decision.FailMissing)
            throw new XunitException(
                $"{SqlServerTestConfig.RequireVariable}=true but {SqlServerTestConfig.ConnectionStringVariable} is not set.");
    }
}
