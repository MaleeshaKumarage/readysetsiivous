namespace CleaningSuite.Application.Companies;

public class CompanyConflictException() : Exception("A company with this business ID already exists in this tenant. Check the business ID, or open the existing company.");
