using System.Reflection;
using EvangelionERPV2.Shared.DTOs;
using EvangelionERPV2.Worker.EmailModule.EmailWorker;

namespace EvangelionERPV2.Test.Workers
{
    /// <summary>
    /// The worker logs in as itself with a secret out of Secrets Manager, and the secret has been
    /// written four different ways over time. This parser is what turns any of them into a
    /// credential — it had no test, and a silent failure here does not look like a parse error: the
    /// worker logs "credentials are missing or invalid" and the queue simply stops draining.
    /// </summary>
    public class EmailConsumerCredentialParsingTests
    {
        private static LoginRequestDTO? Parse(string key)
        {
            var method = typeof(EmailConsumerWorker).GetMethod(
                "BuildLoginRequest", BindingFlags.NonPublic | BindingFlags.Static);

            Assert.NotNull(method);
            return (LoginRequestDTO?)method!.Invoke(null, [key]);
        }

        /// <summary>
        /// Every shape the secret has been written in, in one place: JSON under each key spelling,
        /// key=value pairs, and a bare separated pair. They are one theory rather than three
        /// because the shape is what the data row says — the assertion is identical for all of them.
        /// </summary>
        [Theory]
        // JSON, each key spelling the secret has used
        [InlineData("""{"username":"worker","password":"s3nh4"}""")]
        [InlineData("""{"user":"worker","pass":"s3nh4"}""")]
        [InlineData("""{"login":"worker","pwd":"s3nh4"}""")]
        [InlineData("""{"email":"worker","password":"s3nh4"}""")]
        // key=value pairs, on each supported separator
        [InlineData("username=worker;password=s3nh4")]
        [InlineData("user=worker|pass=s3nh4")]
        [InlineData("login=worker,pwd=s3nh4")]
        [InlineData("  username = worker ; password = s3nh4  ")]
        // a bare pair, on each supported separator
        [InlineData("worker|s3nh4")]
        [InlineData("worker;s3nh4")]
        [InlineData("worker:s3nh4")]
        [InlineData("worker/s3nh4")]
        [InlineData("  worker | s3nh4  ")]
        public void Parse_ReadsEverySecretShapeThatHasEverBeenStored(string secret)
        {
            var credentials = Parse(secret);

            Assert.NotNull(credentials);
            Assert.Equal("worker", credentials!.UserName);
            Assert.Equal("s3nh4", credentials.Password);
        }

        [Fact]
        public void Parse_IgnoresJsonKeysItDoesNotRecognise()
        {
            var credentials = Parse("""{"tenant":"acme","username":"worker","password":"s3nh4"}""");

            Assert.NotNull(credentials);
            Assert.Equal("worker", credentials!.UserName);
        }

        /// <summary>Casing in the secret's keys is the operator's choice, not a contract.</summary>
        [Fact]
        public void Parse_ReadsJsonKeysWhateverTheirCasing()
        {
            var credentials = Parse("""{"UserName":"worker","PASSWORD":"s3nh4"}""");

            Assert.NotNull(credentials);
            Assert.Equal("worker", credentials!.UserName);
            Assert.Equal("s3nh4", credentials.Password);
        }

        /// <summary>
        /// A non-string value is skipped rather than crashing the parse — a secret carrying a
        /// numeric field alongside the credentials must still log the worker in.
        /// </summary>
        [Fact]
        public void Parse_SkipsJsonValuesThatAreNotStrings()
        {
            var credentials = Parse("""{"port":5672,"username":"worker","password":"s3nh4"}""");

            Assert.NotNull(credentials);
            Assert.Equal("worker", credentials!.UserName);
        }

        /// <summary>
        /// The separators are tried in a fixed order, so a password containing an *earlier*
        /// separator than the one that divides the pair splits in the wrong place. `|` wins over
        /// `:`, which means `worker:pa|ss` parses as user `worker:pa`. This is the parser's real
        /// behaviour and the reason a JSON or key=value secret is the safe shape to store.
        /// </summary>
        [Fact]
        public void Parse_SplitsOnTheFirstSeparatorInItsOwnOrderNotTheIntendedOne()
        {
            var credentials = Parse("worker:pa|ss");

            Assert.NotNull(credentials);
            Assert.Equal("worker:pa", credentials!.UserName);
            Assert.Equal("ss", credentials.Password);
        }

        /// <summary>A password carrying a later separator is safe, which is the other half of the rule.</summary>
        [Fact]
        public void Parse_KeepsAPasswordThatContainsALaterSeparator()
        {
            var credentials = Parse("worker|pa:ss");

            Assert.NotNull(credentials);
            Assert.Equal("worker", credentials!.UserName);
            Assert.Equal("pa:ss", credentials.Password);
        }

        /// <summary>JSON is tried first, so a password full of separators survives it intact.</summary>
        [Fact]
        public void Parse_KeepsAJsonPasswordContainingEverySeparator()
        {
            var credentials = Parse("""{"username":"worker","password":"a|b;c:d/e"}""");

            Assert.NotNull(credentials);
            Assert.Equal("a|b;c:d/e", credentials!.Password);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData(null)]
        public void Parse_WithNoSecretAtAll_AnswersNull(string? secret)
        {
            Assert.Null(Parse(secret!));
        }

        /// <summary>
        /// Malformed JSON falls through to the other shapes rather than throwing — but a brace with
        /// nothing parseable behind it ends as no credential, which is the answer the caller logs.
        /// </summary>
        [Theory]
        [InlineData("{not json")]
        [InlineData("{}")]
        [InlineData("""{"username":"worker"}""")]
        [InlineData("""{"password":"s3nh4"}""")]
        public void Parse_WithJsonThatCarriesNoUsablePair_AnswersNull(string secret)
        {
            Assert.Null(Parse(secret));
        }

        [Theory]
        [InlineData("justonevalue")]
        [InlineData("username=worker")]
        [InlineData("password=s3nh4")]
        public void Parse_WithHalfACredential_AnswersNull(string secret)
        {
            Assert.Null(Parse(secret));
        }

        /// <summary>
        /// A separator at either end leaves one side empty, which is not a credential — better no
        /// login than a login attempt with a blank password.
        /// </summary>
        [Theory]
        [InlineData("|s3nh4")]
        [InlineData("worker|")]
        [InlineData("|")]
        public void Parse_WithASeparatorAtTheEdge_AnswersNull(string secret)
        {
            Assert.Null(Parse(secret));
        }

        /// <summary>
        /// A JSON secret is preferred over the bare-pair reading of the same string, so a JSON
        /// value containing a colon is never mistaken for a `user:pass` pair.
        /// </summary>
        [Fact]
        public void Parse_PrefersJsonOverTheBarePairReadingOfTheSameString()
        {
            var credentials = Parse("""{"username":"worker","password":"s3nh4"}""");

            Assert.NotNull(credentials);
            Assert.Equal("worker", credentials!.UserName);
            Assert.DoesNotContain("{", credentials.UserName);
        }
    }
}
