using ScannerService.Application.Common;
using ScannerService.Application.DTOs;
using ScannerService.Application.Interfaces;

namespace ScannerService.UnitTests.ScanJobs;

/// <summary>
/// Hand-rolled IScannerService fake: writes real small files with distinctive content into the
/// configured export directory and returns their paths. Outcomes are programmable per test:
/// written files, a fixed failure, a thrown exception, or an OperationCanceledException on a
/// cancelled token. OmitWriting lets a test return a path whose file does not exist on disk.
/// </summary>
internal sealed class FakeScannerService : IScannerService
{
    private readonly List<string> _writtenFilePaths = [];
    private readonly List<string> _omittedFileNames = [];

    public ScanJobConfiguration? LastConfiguration { get; private set; }

    public int ExecuteCallCount { get; private set; }

    public IReadOnlyList<string> FileNamesToWrite { get; set; } = [];

    public IReadOnlyList<string> WrittenFilePaths => _writtenFilePaths;

    public Result<List<string>>? ProgrammedResult { get; set; }

    public Exception? ProgrammedException { get; set; }

    public bool ThrowOperationCanceledWhenTokenCancelled { get; set; }

    public void OmitWriting(string fileName) => _omittedFileNames.Add(fileName);

    public Task<Result<List<string>>> ExecuteScanAsync(ScanJobConfiguration scanJobConfiguration, CancellationToken cancellationToken = default)
    {
        LastConfiguration = scanJobConfiguration;
        ExecuteCallCount++;

        if (ThrowOperationCanceledWhenTokenCancelled && cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(cancellationToken);
        }

        if (ProgrammedException is not null)
        {
            throw ProgrammedException;
        }

        if (ProgrammedResult is not null)
        {
            return Task.FromResult(ProgrammedResult);
        }

        List<string> returnedPaths = [];

        foreach (string fileName in FileNamesToWrite)
        {
            string filePath = Path.Combine(scanJobConfiguration.ExportPath, fileName);

            if (!_omittedFileNames.Contains(fileName))
            {
                File.WriteAllText(filePath, "FAKE-SCAN-CONTENT:" + fileName);
                _writtenFilePaths.Add(filePath);
            }

            returnedPaths.Add(filePath);
        }

        return Task.FromResult(Result<List<string>>.Success(returnedPaths));
    }
}
