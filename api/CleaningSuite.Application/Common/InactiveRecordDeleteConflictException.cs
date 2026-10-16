namespace CleaningSuite.Application.Common;

public sealed class InactiveRecordDeleteConflictException(string message) : Exception(message);
