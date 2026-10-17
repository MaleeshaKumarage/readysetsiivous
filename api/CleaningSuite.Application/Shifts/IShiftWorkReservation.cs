namespace CleaningSuite.Application.Shifts;

/// <summary>Shared reservation for roster changes and actual shift starts.</summary>
public interface IShiftWorkReservation
{
    Task<IAsyncDisposable> AcquireAsync(CancellationToken ct = default);
}
