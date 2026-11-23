using AmazonScraper.Update;

namespace AmazonScraper.Update.Tests;

public class ProductionSignatureTests
{
    [Fact]
    public void Verifies_a_manifest_signed_with_the_production_key()
    {
        const string manifest = "{\"schema\":1,\"version\":\"2.26.0\"}";
        const string signature =
            "mI3hJtkV6QYIKAHHRH/FGmONJ5gvJV9Lcm3bLSsUCqNTPbFUUBGhtzONKxTHDZR1fjGZwFl11PFzOnRNyQqwdhnFov7hpRxBUb+qnx/qXTFdet5KxIw+HvcvTQL1F9541mDGdEb+DI+KAqbsdSP3s75ICPSZJhOgIZwUB188BlxZRXZ9O6BcQ8GEf9Z7CZQ650y8UlWRQxXZoKhStbqK4ZcHb12k9YCYslnC3Uh+K0J8OGSJIBehdAlpxSGEJwBGr471rRKi7kz0tmCw9RlQBP2Dn1ILmUqcDgUtdlLSkr7zHwXbq2Q3mLUiH+b92AU0yzdf6MyNPwxNeATw3z9tCE8cQ0nnZzw0gR+TsojL+PKtHVY7kHiAuXzu/N+s2lPtziyTBt4xJUPvlKmlDvNr2xdxurJbTdf1In8nV4SA0jyIjyPK2NK8TZLakdu4oyYFBPpNOQ2CU0dcONdas+wFYvoAg6AhEctYI+XnsG+BJ+ejUKCpfBRCwltSkOgwbZU9IhR8LQMqfpijkUKdcIcZpcC5H1MGr9ZT/SLshc/ZQzFoStsRYJ4dQacZkCWnAv/XsutmlEDNx+kiHZC2A7RvO0rIpKJ/aEQndTIcO9DyQouVIBFu2AEfEmsHlTl1gBu13KZH09rwOIV7+agCIOXgo65fR+v3g2+QSW9j4a3oSDc=";

        ReleaseSignatureVerifier.VerifyOrThrow(
            manifest,
            signature,
            UpdateEndpoints.SigningKeyId);
    }
}
