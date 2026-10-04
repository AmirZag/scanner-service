using System;
using System.Reflection;
using ScannerService.Application.Common;
using Xunit;

namespace ScannerService.UnitTests.Application;

public class ResultTests
{
    [Fact]
    public void Success_HasNoError_AndBothFlagsConsistent()
    {
        Result result = Result.Success();

        Assert.True(result.IsSuccess);
        Assert.False(result.IsFailure);
        Assert.Null(result.Error);
    }

    [Fact]
    public void Failure_CarriesTheError_AndBothFlagsConsistent()
    {
        Result result = Result.Failure("device offline");

        Assert.False(result.IsSuccess);
        Assert.True(result.IsFailure);
        Assert.Equal("device offline", result.Error);
    }

    [Fact]
    public void Failure_WithNullError_IsRejectedByTheInvariant()
    {
        Assert.Throws<InvalidOperationException>(() => Result.Failure(null!));
    }

    [Fact]
    public void ConstructingSuccessWithError_IsRejectedByTheInvariant()
    {
        Assert.Throws<InvalidOperationException>(() => TestableResult.Create(true, "contradiction"));
    }

    [Fact]
    public void Generic_Success_ReturnsTheValue_WithNullError()
    {
        Result<string> result = Result<string>.Success("scan_20261003_142530.pdf");

        Assert.True(result.IsSuccess);
        Assert.False(result.IsFailure);
        Assert.Equal("scan_20261003_142530.pdf", result.Value);
        Assert.Null(result.Error);
    }

    [Fact]
    public void Generic_Failure_HasDefaultPinnedAsValue_AndCarriesTheError()
    {
        Result<string> referenceFailure = Result<string>.Failure("disk full");
        Result<int> valueFailure = Result<int>.Failure("disk full");

        Assert.False(referenceFailure.IsSuccess);
        Assert.Null(referenceFailure.Value);
        Assert.Equal("disk full", referenceFailure.Error);
        Assert.Equal(0, valueFailure.Value);
        Assert.Equal("disk full", valueFailure.Error);
    }

    [Fact]
    public void Generic_Failure_WithNullError_IsRejectedByTheInvariant()
    {
        Assert.Throws<InvalidOperationException>(() => Result<string>.Failure(null!));
    }

    // The Result<T> constructor is private and both factories pass consistent flags, so the
    // success-with-error invariant is only reachable through direct construction. Reflection
    // is the only seam (a derived record cannot call a private base constructor), and the
    // pragma follows the production pattern for justified Sonar suppressions.
#pragma warning disable S3011 // Reflection should not be used to increase usability of coded tests
    [Fact]
    public void Generic_ConstructingSuccessWithError_IsRejectedByTheInvariant()
    {
        TargetInvocationException exception = Assert.Throws<TargetInvocationException>(() =>
            Activator.CreateInstance(
                typeof(Result<string>),
                BindingFlags.NonPublic | BindingFlags.Instance,
                binder: null,
                args: new object[] { true, "some value", "contradictory error" },
                culture: null));

        InvalidOperationException innerException = Assert.IsType<InvalidOperationException>(exception.InnerException);
        Assert.Equal("Success result cannot have an error.", innerException.Message);
    }
#pragma warning restore S3011 // Reflection should not be used to increase usability of coded tests

    [Fact]
    public void ImplicitBool_ConversionReflectsIsSuccess()
    {
        Result<int> success = Result<int>.Success(7);
        Result<int> failure = Result<int>.Failure("device offline");

        bool successAsBool = success;
        bool failureAsBool = failure;

        Assert.True(successAsBool);
        Assert.False(failureAsBool);
    }

    private sealed record TestableResult : Result
    {
        public TestableResult(bool isSuccess, string? error)
            : base(isSuccess, error)
        {
        }

        public static TestableResult Create(bool isSuccess, string? error)
        {
            return new TestableResult(isSuccess, error);
        }
    }
}
