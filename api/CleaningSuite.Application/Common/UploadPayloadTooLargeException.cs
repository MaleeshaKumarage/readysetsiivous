namespace CleaningSuite.Application.Common;

public sealed class UploadPayloadTooLargeException(string message) : Exception(message);
