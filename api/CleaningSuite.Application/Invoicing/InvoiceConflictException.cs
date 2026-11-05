namespace CleaningSuite.Application.Invoicing;

// Raised when an invoice transition is not allowed by policy (for example voiding or re-paying a paid invoice). Maps to 409.
public class InvoiceConflictException(string message) : Exception(message);
