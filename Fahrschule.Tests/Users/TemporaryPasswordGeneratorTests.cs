using Fahrschule.Application.Users;

namespace Fahrschule.Tests.Users;

/// <summary>
/// Tests for the temporary password generator. It must always satisfy the
/// password policy (Identity settings), otherwise creating a user would fail.
/// </summary>
public class TemporaryPasswordGeneratorTests
{
    [Fact]
    public void Generated_password_satisfies_the_policy()
    {
        // Run many times - the generator is random, so check a large sample.
        for (var i = 0; i < 500; i++)
        {
            var password = TemporaryPasswordGenerator.Generate();

            Assert.True(password.Length >= 10, $"too short: {password}");
            Assert.Contains(password, char.IsUpper);
            Assert.Contains(password, char.IsLower);
            Assert.Contains(password, char.IsDigit);
        }
    }

    [Fact]
    public void Generated_passwords_are_not_all_identical()
    {
        var values = Enumerable.Range(0, 50).Select(_ => TemporaryPasswordGenerator.Generate()).ToHashSet();

        // Not a strict randomness test, just a sanity check against a constant.
        Assert.True(values.Count > 1);
    }

    [Fact]
    public void Generated_password_avoids_ambiguous_characters()
    {
        for (var i = 0; i < 200; i++)
        {
            var password = TemporaryPasswordGenerator.Generate();

            // No 0/1 digits and no lowercase l (easy to confuse when read aloud).
            Assert.DoesNotContain('0', password);
            Assert.DoesNotContain('1', password);
            Assert.DoesNotContain('l', password);
        }
    }
}
