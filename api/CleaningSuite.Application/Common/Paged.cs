namespace CleaningSuite.Application.Common;

public record Paged<T>(IReadOnlyList<T> Items, int Total);
