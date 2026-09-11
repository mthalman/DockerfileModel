using Valleysoft.DockerfileModel.Tokens;

namespace Valleysoft.DockerfileModel.Tests;

public class GuardTests
{
    [Fact]
    public void NotNull()
    {
        Assert.Throws<ArgumentNullException>("input", () => Guard.NotNull<object>(null, "input"));
        Guard.NotNull(new object(), "input");
        Guard.NotNull("", "input");
        Guard.NotNull(Array.Empty<string>(), "input");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("\0")]
    [InlineData("\0value")]
    public void StringGuardsRejectNullEmptyAndLeadingNull(string? value)
    {
        Type expectedType = value is null ? typeof(ArgumentNullException) : typeof(ArgumentException);
        AssertArgument(expectedType, () => Guard.NotNullOrEmpty(value, "input"));
        AssertArgument(expectedType, () => Guard.NotNullOrWhiteSpace(value, "input"));
    }

    [Theory]
    [InlineData(" ")]
    [InlineData("\t\r\n")]
    [InlineData("\u00a0")]
    [InlineData("\u2003")]
    public void StringGuardsDistinguishWhitespace(string value)
    {
        Guard.NotNullOrEmpty(value, "input");
        Assert.Throws<ArgumentException>("input", () => Guard.NotNullOrWhiteSpace(value, "input"));
    }

    [Theory]
    [InlineData("value")]
    [InlineData(" value ")]
    [InlineData("value\0")]
    public void StringGuardsAcceptNonEmptyValues(string value)
    {
        Guard.NotNullOrEmpty(value, "input");
        Guard.NotNullOrWhiteSpace(value, "input");
    }

    [Fact]
    public void EnumerableGuards()
    {
        Assert.Throws<ArgumentNullException>("input", () => Guard.NotNullOrEmpty<string>(null, "input"));
        Assert.Throws<ArgumentNullException>("input", () => Guard.NotNullEmptyOrNullElements<string>(null, "input"));

        foreach (IEnumerable<string> values in new IEnumerable<string>[] { Array.Empty<string>(), new List<string>() })
        {
            Assert.Throws<ArgumentException>("input", () => Guard.NotNullOrEmpty(values, "input"));
            Assert.Throws<ArgumentException>("input", () => Guard.NotNullEmptyOrNullElements(values, "input"));
        }

        Guard.NotNullOrEmpty(new Dictionary<string, string?> { ["KEY"] = null }, "input");
        Guard.NotNullOrEmpty(new string?[] { null }, "input");
        Guard.NotNullEmptyOrNullElements(new[] { "", " ", "\0", "value" }, "input");
        Guard.NotNullEmptyOrNullElements(new Token[] { new StringToken("value") }, "input");
        Assert.Throws<ArgumentException>("input", () => Guard.NotNullEmptyOrNullElements(new Token?[] { null }, "input"));
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(false, 1)]
    [InlineData(false, 2)]
    [InlineData(true, 0)]
    [InlineData(true, 1)]
    [InlineData(true, 2)]
    public void EnumerableGuardsDisposeIterators(bool validateElements, int input)
    {
        bool disposed = false;
        IEnumerable<string?> Values()
        {
            try
            {
                if (input > 0)
                {
                    yield return "value";
                }
                if (input == 2)
                {
                    yield return null;
                }
            }
            finally
            {
                disposed = true;
            }
        }

        void Validate()
        {
            if (validateElements)
            {
                Guard.NotNullEmptyOrNullElements(Values(), "input");
            }
            else
            {
                Guard.NotNullOrEmpty(Values(), "input");
            }
        }

        if (input == 0 || (validateElements && input == 2))
        {
            Assert.Throws<ArgumentException>("input", Validate);
        }
        else
        {
            Validate();
        }

        Assert.True(disposed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EnumerableGuardsPropagateIteratorExceptions(bool validateElements)
    {
        bool disposed = false;
        InvalidOperationException failure = new("Iterator failed.");
        IEnumerable<string> Values()
        {
            try
            {
                if (validateElements)
                {
                    yield return "value";
                }
                throw failure;
            }
            finally
            {
                disposed = true;
            }
        }

        InvalidOperationException actual = Assert.Throws<InvalidOperationException>(() =>
        {
            if (validateElements)
            {
                Guard.NotNullEmptyOrNullElements(Values(), "input");
            }
            else
            {
                Guard.NotNullOrEmpty(Values(), "input");
            }
        });

        Assert.Same(failure, actual);
        Assert.True(disposed);
    }

    [Fact]
    public void NullElementGuardStopsAtFirstInvalidElement()
    {
        IEnumerable<string?> Values()
        {
            yield return null;
            throw new InvalidOperationException("Enumeration continued after a null element.");
        }

        Assert.Throws<ArgumentException>("input", () => Guard.NotNullEmptyOrNullElements(Values(), "input"));
    }

    [Fact]
    public void Operation()
    {
        Guard.Operation(true, "message");
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => Guard.Operation(false, "message"));
        Assert.Equal("message", exception.Message);
    }

    private static void AssertArgument(Type expectedType, Action action)
    {
        ArgumentException exception = Assert.IsAssignableFrom<ArgumentException>(Assert.Throws(expectedType, action));
        Assert.Equal("input", exception.ParamName);
    }
}
