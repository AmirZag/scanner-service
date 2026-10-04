using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ScannerService.Application.DTOs;
using ScannerService.Application.Interfaces;

namespace ScannerService.UnitTests.Infrastructure;

/// <summary>
/// Hand-rolled <see cref="IScannerQueries"/> fake that counts enumeration calls, returns a
/// fixed scanner list (fresh copy per call), and can be switched to throw so the decorator's
/// failure semantics can be pinned without any real scanner driver.
/// </summary>
internal sealed class CountingScannerQueriesFake : IScannerQueries
{
    private readonly List<ScannerDto> _result = new List<ScannerDto>
    {
        new ScannerDto("fake-scanner-1", "Fake Scanner", "Wia"),
        new ScannerDto("fake-scanner-2", "Second Fake Scanner", "Escl")
    };

    public int CallCount { get; private set; }

    public Exception? FailureToThrow { get; set; }

    public Task<List<ScannerDto>> GetScannersListAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        CallCount++;

        Exception? failure = FailureToThrow;
        if (failure is not null)
        {
            return Task.FromException<List<ScannerDto>>(failure);
        }

        return Task.FromResult(new List<ScannerDto>(_result));
    }
}
