using CleaningSuite.Application.Companies;
using CleaningSuite.Application.QualityCycle;
using CleaningSuite.Domain.Companies;
using CleaningSuite.Domain.QualityCycle;
using Moq;

namespace CleaningSuite.Tests.Companies;

public sealed class CompanyQualityCycleTests
{
    private static QualityCycleTemplate Template(Guid? companyId = null, bool active = true)
    {
        var template = QualityCycleTemplate.Create("Monthly checklist", ["Corridor", "Gym"], companyId: companyId);
        template.Id = Guid.NewGuid(); template.IsActive = active;
        return template;
    }

    [Fact]
    public async Task Creating_a_company_with_quality_cycle_stores_the_chosen_active_template()
    {
        var template = Template();
        var saved = (Company?)null;
        var companies = new Mock<ICompanyRepository>();
        companies.Setup(r => r.SaveAsync(It.IsAny<Company>(), default)).Callback<Company, CancellationToken>((c, _) => saved = c).Returns(Task.CompletedTask);
        var qualityCycles = new Mock<IQualityCycleRepository>();
        qualityCycles.Setup(r => r.GetTemplateAsync(template.Id, default)).ReturnsAsync(template);

        var result = await new CompanyHandlers.CreateCompanyCommandHandler(companies.Object, qualityCycles.Object)
            .Handle(new CreateCompanyCommand("1234567-8", "Lake Owners Oy", IsPartnerEnabled: true, IsQualityCycleEnabled: true, QualityCycleTemplateId: template.Id), default);

        Assert.True(result.IsQualityCycleEnabled);
        Assert.Equal(template.Id, result.QualityCycleTemplateId);
        Assert.NotNull(saved);
        Assert.True(saved!.IsQualityCycleEnabled);
        Assert.Equal(template.Id, saved.QualityCycleTemplateId);
    }

    [Fact]
    public async Task Enabling_quality_cycle_without_a_template_is_refused()
    {
        var companies = new Mock<ICompanyRepository>();
        var handler = new CompanyHandlers.CreateCompanyCommandHandler(companies.Object, new Mock<IQualityCycleRepository>().Object);

        await Assert.ThrowsAsync<FluentValidation.ValidationException>(() =>
            handler.Handle(new CreateCompanyCommand("1234567-8", "Lake Owners Oy", IsQualityCycleEnabled: true), default));
        companies.Verify(r => r.SaveAsync(It.IsAny<Company>(), default), Times.Never);
    }

    [Fact]
    public async Task A_template_owned_by_another_company_or_inactive_is_refused()
    {
        var other = Template(companyId: Guid.NewGuid());
        var inactive = Template(active: false);
        var qualityCycles = new Mock<IQualityCycleRepository>();
        qualityCycles.Setup(r => r.GetTemplateAsync(other.Id, default)).ReturnsAsync(other);
        qualityCycles.Setup(r => r.GetTemplateAsync(inactive.Id, default)).ReturnsAsync(inactive);
        var handler = new CompanyHandlers.CreateCompanyCommandHandler(new Mock<ICompanyRepository>().Object, qualityCycles.Object);

        await Assert.ThrowsAsync<FluentValidation.ValidationException>(() =>
            handler.Handle(new CreateCompanyCommand("1234567-8", "Lake Owners Oy", IsQualityCycleEnabled: true, QualityCycleTemplateId: other.Id), default));
        await Assert.ThrowsAsync<FluentValidation.ValidationException>(() =>
            handler.Handle(new CreateCompanyCommand("1234567-8", "Lake Owners Oy", IsQualityCycleEnabled: true, QualityCycleTemplateId: inactive.Id), default));
    }

    [Fact]
    public async Task Update_without_the_quality_cycle_fields_keeps_the_saved_choice()
    {
        var template = Template();
        var company = Company.Create("1234567-8", "Lake Owners Oy");
        company.IsQualityCycleEnabled = true; company.QualityCycleTemplateId = template.Id;
        var companies = new Mock<ICompanyRepository>();
        companies.Setup(r => r.GetAsync(company.Id, default)).ReturnsAsync(company);
        var handler = new CompanyHandlers.UpdateCompanyCommandHandler(companies.Object, null, new Mock<IQualityCycleRepository>().Object);

        var result = await handler.Handle(new UpdateCompanyCommand(company.Id, "1234567-8", "Lake Owners Oy"), default);

        Assert.True(result.IsQualityCycleEnabled);
        Assert.Equal(template.Id, result.QualityCycleTemplateId);
    }

    [Fact]
    public async Task Update_that_turns_quality_cycle_off_clears_the_template()
    {
        var template = Template();
        var company = Company.Create("1234567-8", "Lake Owners Oy");
        company.IsQualityCycleEnabled = true; company.QualityCycleTemplateId = template.Id;
        var companies = new Mock<ICompanyRepository>();
        companies.Setup(r => r.GetAsync(company.Id, default)).ReturnsAsync(company);
        var handler = new CompanyHandlers.UpdateCompanyCommandHandler(companies.Object, null, new Mock<IQualityCycleRepository>().Object);

        var result = await handler.Handle(new UpdateCompanyCommand(company.Id, "1234567-8", "Lake Owners Oy", IsQualityCycleEnabled: false), default);

        Assert.False(result.IsQualityCycleEnabled);
        Assert.Null(result.QualityCycleTemplateId);
    }
}
