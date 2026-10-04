using System;
using System.Collections.Generic;
using ScannerService.Application.Common;
using Xunit;

namespace ScannerService.UnitTests.Application;

public class ResultExtensionsTests
{
    [Fact]
    public void Map_TransformsTheValueOnSuccess_AndPassesTheErrorThroughOnFailure()
    {
        Result<int> success = Result<int>.Success(21);
        Result<int> failure = Result<int>.Failure("device offline");
        int transformInvocations = 0;

        Result<string> mappedSuccess = success.Map(value =>
        {
            transformInvocations++;
            return $"{value} pages";
        });
        Result<string> mappedFailure = failure.Map(value =>
        {
            transformInvocations++;
            return $"{value} pages";
        });

        Assert.True(mappedSuccess.IsSuccess);
        Assert.Equal("21 pages", mappedSuccess.Value);
        Assert.True(mappedFailure.IsFailure);
        Assert.Equal("device offline", mappedFailure.Error);
        Assert.Equal(1, transformInvocations);
    }

    [Fact]
    public void Bind_RunsTheChainedOperationOnSuccess_AndShortCircuitsOnFailure()
    {
        Result<int> success = Result<int>.Success(2);
        Result<int> failure = Result<int>.Failure("scanner busy");
        int bindInvocations = 0;

        Result<int> boundSuccess = success.Bind(value =>
        {
            bindInvocations++;
            return Result<int>.Success(value * 100);
        });
        Result<int> boundFailure = failure.Bind(value =>
        {
            bindInvocations++;
            return Result<int>.Success(value * 100);
        });

        Assert.True(boundSuccess.IsSuccess);
        Assert.Equal(200, boundSuccess.Value);
        Assert.True(boundFailure.IsFailure);
        Assert.Equal("scanner busy", boundFailure.Error);
        Assert.Equal(1, bindInvocations);
    }

    [Fact]
    public void Bind_PropagatesAFailureReturnedByTheChainedOperation()
    {
        Result<int> success = Result<int>.Success(2);

        Result<int> bound = success.Bind(_ => Result<int>.Failure("resolution unsupported"));

        Assert.True(bound.IsFailure);
        Assert.Equal("resolution unsupported", bound.Error);
    }

    [Fact]
    public void Tap_RunsOnlyOnSuccess_AndReturnsTheOriginalResultInstance()
    {
        Result<string> success = Result<string>.Success("page1.png");
        Result<string> failure = Result<string>.Failure("disk full");
        List<string> observed = new List<string>();

        Result<string> tappedSuccess = success.Tap(value => observed.Add(value));
        Result<string> tappedFailure = failure.Tap(value => observed.Add(value));

        Assert.Same(success, tappedSuccess);
        Assert.Same(failure, tappedFailure);
        Assert.Single(observed);
        Assert.Equal("page1.png", observed[0]);
    }

    [Fact]
    public void OnFailure_RunsOnlyOnFailure_AndReturnsTheOriginalResultInstance()
    {
        Result<string> success = Result<string>.Success("page1.png");
        Result<string> failure = Result<string>.Failure("disk full");
        List<string> observedErrors = new List<string>();

        Result<string> tappedSuccess = success.OnFailure(error => observedErrors.Add(error));
        Result<string> tappedFailure = failure.OnFailure(error => observedErrors.Add(error));

        Assert.Same(success, tappedSuccess);
        Assert.Same(failure, tappedFailure);
        Assert.Single(observedErrors);
        Assert.Equal("disk full", observedErrors[0]);
    }

    [Fact]
    public void GetValueOrDefault_ReturnsTheValueOnSuccess_AndTheFallbackOnFailure()
    {
        Result<string> success = Result<string>.Success("scan.pdf");
        Result<string> failure = Result<string>.Failure("not found");

        string? successValue = success.GetValueOrDefault("fallback.pdf");
        string? failureValue = failure.GetValueOrDefault("fallback.pdf");
        string? failureWithoutFallback = failure.GetValueOrDefault();

        Assert.Equal("scan.pdf", successValue);
        Assert.Equal("fallback.pdf", failureValue);
        Assert.Null(failureWithoutFallback);
    }

    [Fact]
    public void Combine_AllSuccess_ReturnsSuccess()
    {
        Result combined = ResultExtensions.Combine(Result.Success(), Result.Success());

        Assert.True(combined.IsSuccess);
    }

    [Fact]
    public void Combine_AnyFailure_ReturnsTheFirstFailureError()
    {
        Result first = Result.Success();
        Result second = Result.Failure("scanner busy");
        Result third = Result.Failure("disk full");

        Result combined = ResultExtensions.Combine(first, second, third);

        Assert.True(combined.IsFailure);
        Assert.Equal("scanner busy", combined.Error);
    }

    [Fact]
    public void Combine_WithNoResults_ReturnsSuccess()
    {
        Result[] noResults = Array.Empty<Result>();

        Result combined = ResultExtensions.Combine(noResults);

        Assert.True(combined.IsSuccess);
    }

    [Fact]
    public void ToResult_OnReferenceTypes_WrapsNonNullAsSuccess_AndNullAsFailure()
    {
        string? present = "profile-42";
        string? missing = null;

        Result<string> success = present.ToResult("profile does not exist");
        Result<string> failure = missing.ToResult("profile does not exist");

        Assert.True(success.IsSuccess);
        Assert.Equal("profile-42", success.Value);
        Assert.True(failure.IsFailure);
        Assert.Equal("profile does not exist", failure.Error);
    }

    [Fact]
    public void ToResult_OnNullableValueTypes_WrapsHasValueAsSuccess_AndNullAsFailure()
    {
        int? present = 300;
        int? missing = null;

        Result<int> success = present.ToResult("resolution missing");
        Result<int> failure = missing.ToResult("resolution missing");

        Assert.True(success.IsSuccess);
        Assert.Equal(300, success.Value);
        Assert.True(failure.IsFailure);
        Assert.Equal(0, failure.Value);
        Assert.Equal("resolution missing", failure.Error);
    }
}
