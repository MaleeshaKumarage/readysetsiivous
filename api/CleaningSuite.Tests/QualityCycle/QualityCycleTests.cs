using CleaningSuite.Application.Common;
using CleaningSuite.Application.Employees;
using CleaningSuite.Application.QualityCycle;
using CleaningSuite.Application.Shifts;
using CleaningSuite.Domain.Companies;
using CleaningSuite.Domain.Employees;
using CleaningSuite.Domain.QualityCycle;
using CleaningSuite.Domain.Shifts;
using FluentValidation.TestHelper;
using Moq;
using Xunit;

namespace CleaningSuite.Tests.QualityCycle;

public class QualityCycleTests
{
    // --- DOMAIN TESTS: QualityCycleTemplate ---

    [Fact]
    public void CreateQualityCycleTemplate_WithValidInput_Succeeds()
    {
        var items = new List<string> { "Vacuum floor", "Wipe surfaces", "Empty trash" };
        var companyId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        var template = QualityCycleTemplate.Create("Daily Office Checklist", items, "Standard daily cleaning", companyId, branchId);

        Assert.NotNull(template);
        Assert.Equal("Daily Office Checklist", template.Title);
        Assert.Equal("Standard daily cleaning", template.Description);
        Assert.Equal(3, template.Items.Count);
        Assert.Equal(companyId, template.CompanyId);
        Assert.Equal(branchId, template.BranchId);
        Assert.True(template.IsActive);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void CreateQualityCycleTemplate_WithNullOrWhitespaceTitle_ThrowsArgumentException(string title)
    {
        Assert.Throws<ArgumentException>(() => QualityCycleTemplate.Create(title, new List<string> { "Item 1" }));
    }

    [Fact]
    public void CreateQualityCycleTemplate_WithNullOrEmptyItems_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => QualityCycleTemplate.Create("Title", null!));
        Assert.Throws<ArgumentException>(() => QualityCycleTemplate.Create("Title", new List<string>()));
        Assert.Throws<ArgumentException>(() => QualityCycleTemplate.Create("Title", new List<string> { "   ", "" }));
    }

    [Fact]
    public void UpdateQualityCycleTemplate_WithValidInput_UpdatesProperties()
    {
        var template = QualityCycleTemplate.Create("Title", new List<string> { "Item 1" });
        var newCompanyId = Guid.NewGuid();
        var newBranchId = Guid.NewGuid();

        template.Update("Updated Title", new List<string> { "New Item 1", "  New Item 2  " }, "New Description", false, newCompanyId, newBranchId);

        Assert.Equal("Updated Title", template.Title);
        Assert.Equal("New Description", template.Description);
        Assert.Equal(2, template.Items.Count);
        Assert.Equal("New Item 1", template.Items[0]);
        Assert.Equal("New Item 2", template.Items[1]);
        Assert.False(template.IsActive);
        Assert.Equal(newCompanyId, template.CompanyId);
        Assert.Equal(newBranchId, template.BranchId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void UpdateQualityCycleTemplate_WithInvalidTitle_ThrowsArgumentException(string invalidTitle)
    {
        var template = QualityCycleTemplate.Create("Title", new List<string> { "Item 1" });
        Assert.Throws<ArgumentException>(() => template.Update(invalidTitle, new List<string> { "Item 1" }));
    }

    [Fact]
    public void UpdateQualityCycleTemplate_WithInvalidItems_ThrowsArgumentException()
    {
        var template = QualityCycleTemplate.Create("Title", new List<string> { "Item 1" });
        Assert.Throws<ArgumentException>(() => template.Update("Title", null!));
        Assert.Throws<ArgumentException>(() => template.Update("Title", new List<string>()));
        Assert.Throws<ArgumentException>(() => template.Update("Title", new List<string> { "  " }));
    }

    // --- DOMAIN TESTS: QualityCycleForm ---

    [Fact]
    public void CreateQualityCycleForm_WithValidInput_Succeeds()
    {
        var shiftId = Guid.NewGuid();
        var empId = Guid.NewGuid();
        var templateId = Guid.NewGuid();
        var occurrence = DateTime.UtcNow;
        var items = new List<string> { "  Task 1 ", "Task 2", "  " };

        var form = QualityCycleForm.Create(shiftId, "  Morning Shift  ", empId, "  John Doe  ", templateId, "  Checklist  ", occurrence, items);

        Assert.NotNull(form);
        Assert.Equal(shiftId, form.ShiftId);
        Assert.Equal("Morning Shift", form.ShiftName);
        Assert.Equal(empId, form.EmployeeId);
        Assert.Equal("John Doe", form.EmployeeName);
        Assert.Equal(templateId, form.TemplateId);
        Assert.Equal("Checklist", form.TemplateTitle);
        Assert.Equal(occurrence, form.ShiftOccurrenceUtc);
        Assert.False(string.IsNullOrWhiteSpace(form.Token));
        Assert.Equal(2, form.Items.Count);
        Assert.Equal("Task 1", form.Items[0].ItemText);
        Assert.False(form.Items[0].IsChecked);
        Assert.False(form.IsSubmitted);
        Assert.Null(form.SubmittedUtc);
    }

    [Fact]
    public void CreateQualityCycleForm_DefaultsOccurrenceEndToStart()
    {
        var occurrence = new DateTime(2026, 10, 1, 8, 0, 0, DateTimeKind.Utc);
        var form = QualityCycleForm.Create(Guid.NewGuid(), "Shift", Guid.NewGuid(), "Emp", Guid.NewGuid(), "Tpl", occurrence, new List<string>());
        Assert.Equal(occurrence, form.ShiftOccurrenceUtc);
        Assert.Equal(occurrence, form.ShiftOccurrenceEndUtc);
    }

    [Fact]
    public void CreateQualityCycleForm_StoresOccurrenceEndUtc()
    {
        var start = new DateTime(2026, 10, 1, 8, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(2026, 10, 1, 10, 0, 0, DateTimeKind.Utc);
        var form = QualityCycleForm.Create(Guid.NewGuid(), "Shift", Guid.NewGuid(), "Emp", Guid.NewGuid(), "Tpl", start, new List<string>(), end);
        Assert.Equal(start, form.ShiftOccurrenceUtc);
        Assert.Equal(end, form.ShiftOccurrenceEndUtc);
    }

    [Fact]
    public void CreateQualityCycleForm_WithEmptyShiftOrEmployeeId_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => QualityCycleForm.Create(Guid.Empty, "Shift", Guid.NewGuid(), "Emp", Guid.NewGuid(), "Tpl", DateTime.UtcNow, new List<string>()));
        Assert.Throws<ArgumentException>(() => QualityCycleForm.Create(Guid.NewGuid(), "Shift", Guid.Empty, "Emp", Guid.NewGuid(), "Tpl", DateTime.UtcNow, new List<string>()));
    }

    [Fact]
    public void SubmitQualityCycleForm_UpdatesFormStateAndIsSubmitted()
    {
        var templateItems = new List<string> { "Task 1", "Task 2" };
        var form = QualityCycleForm.Create(
            Guid.NewGuid(), "Morning Shift", Guid.NewGuid(), "John Cleaner", Guid.NewGuid(), "Daily Template", DateTime.UtcNow, templateItems);

        var submissionItems = new List<QualityCycleFormItem>
        {
            new QualityCycleFormItem { ItemText = "Task 1", IsChecked = true },
            new QualityCycleFormItem { ItemText = "Task 2", IsChecked = false }
        };

        form.Submit(submissionItems, new List<string> { "https://example.com/photo1.jpg", "  " }, "  All clear  ");

        Assert.True(form.IsSubmitted);
        Assert.NotNull(form.SubmittedUtc);
        Assert.Equal("All clear", form.CleanerNotes);
        Assert.Single(form.PhotoUrls);
        Assert.True(form.Items[0].IsChecked);
        Assert.False(form.Items[1].IsChecked);
    }

    [Fact]
    public void SubmitQualityCycleForm_WhenAlreadySubmitted_ThrowsInvalidOperationException()
    {
        var form = QualityCycleForm.Create(
            Guid.NewGuid(), "Shift", Guid.NewGuid(), "Cleaner", Guid.NewGuid(), "Template", DateTime.UtcNow, new List<string> { "Task 1" });
        form.Submit(new List<QualityCycleFormItem>(), null, null);

        Assert.Throws<InvalidOperationException>(() => form.Submit(new List<QualityCycleFormItem>(), null, null));
    }

    [Fact]
    public void SubmitQualityCycleForm_FallbackToIndexMatching_WhenItemTextModified()
    {
        var form = QualityCycleForm.Create(
            Guid.NewGuid(), "Shift", Guid.NewGuid(), "Cleaner", Guid.NewGuid(), "Template", DateTime.UtcNow, new List<string> { "Task A", "Task B" });

        var submittedItems = new List<QualityCycleFormItem>
        {
            new QualityCycleFormItem { ItemText = "Modified Text A", IsChecked = true },
            new QualityCycleFormItem { ItemText = "Modified Text B", IsChecked = false }
        };

        form.Submit(submittedItems, null, null);

        Assert.True(form.Items[0].IsChecked);
        Assert.False(form.Items[1].IsChecked);
        Assert.Equal("Task A", form.Items[0].ItemText); // Original text preserved
    }

    [Fact]
    public void Start_RecordsStartedAtUtc()
    {
        var form = QualityCycleForm.Create(Guid.NewGuid(), "Shift", Guid.NewGuid(), "Cleaner", Guid.NewGuid(), "Template", DateTime.UtcNow, new List<string> { "Task A" });

        form.Start();

        Assert.NotNull(form.StartedAtUtc);
        Assert.False(form.IsSubmitted);
    }

    [Fact]
    public void Start_WhenAlreadyStarted_DoesNotOverwrite()
    {
        var form = QualityCycleForm.Create(Guid.NewGuid(), "Shift", Guid.NewGuid(), "Cleaner", Guid.NewGuid(), "Template", DateTime.UtcNow, new List<string> { "Task A" });
        form.Start();
        var first = form.StartedAtUtc;

        form.Start();

        Assert.Equal(first, form.StartedAtUtc);
    }

    [Fact]
    public void Submit_RecordsEndedAtUtc()
    {
        var form = QualityCycleForm.Create(Guid.NewGuid(), "Shift", Guid.NewGuid(), "Cleaner", Guid.NewGuid(), "Template", DateTime.UtcNow, new List<string> { "Task A" });
        form.Start();

        form.Submit(new List<QualityCycleFormItem> { new QualityCycleFormItem { ItemText = "Task A", IsChecked = true } }, null, null);

        Assert.True(form.IsSubmitted);
        Assert.NotNull(form.EndedAtUtc);
        Assert.True(form.EndedAtUtc >= form.StartedAtUtc);
    }

    // --- APPLICATION HANDLERS TESTS ---

    [Fact]
    public async Task CreateQualityCycleTemplateCommandHandler_SavesAndReturnsDto()
    {
        var repoMock = new Mock<IQualityCycleRepository>();
        var handler = new QualityCycleHandlers.CreateQualityCycleTemplateCommandHandler(repoMock.Object);

        var command = new CreateQualityCycleTemplateCommand("Test Template", new List<string> { "Item 1", "Item 2" }, "Description");
        var result = await handler.Handle(command, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("Test Template", result.Title);
        Assert.Equal(2, result.Items.Count);
        repoMock.Verify(x => x.SaveTemplateAsync(It.IsAny<QualityCycleTemplate>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateQualityCycleTemplateCommandHandler_WhenTemplateExists_UpdatesAndSaves()
    {
        var repoMock = new Mock<IQualityCycleRepository>();
        var template = QualityCycleTemplate.Create("Old Title", new List<string> { "Item 1" });
        repoMock.Setup(r => r.GetTemplateAsync(template.Id, It.IsAny<CancellationToken>())).ReturnsAsync(template);

        var handler = new QualityCycleHandlers.UpdateQualityCycleTemplateCommandHandler(repoMock.Object);
        var command = new UpdateQualityCycleTemplateCommand(template.Id, "New Title", new List<string> { "Item 1", "Item 2" }, "New Desc", true);

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.Equal("New Title", result.Title);
        Assert.Equal(2, result.Items.Count);
        repoMock.Verify(r => r.SaveTemplateAsync(template, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateQualityCycleTemplateCommandHandler_WhenTemplateNotFound_ThrowsNotFoundException()
    {
        var repoMock = new Mock<IQualityCycleRepository>();
        repoMock.Setup(r => r.GetTemplateAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((QualityCycleTemplate?)null);

        var handler = new QualityCycleHandlers.UpdateQualityCycleTemplateCommandHandler(repoMock.Object);
        var command = new UpdateQualityCycleTemplateCommand(Guid.NewGuid(), "Title", new List<string> { "Item 1" });

        await Assert.ThrowsAsync<NotFoundException>(() => handler.Handle(command, CancellationToken.None));
    }

    [Fact]
    public async Task DeleteQualityCycleTemplateCommandHandler_CallsRepositoryDelete()
    {
        var repoMock = new Mock<IQualityCycleRepository>();
        var handler = new QualityCycleHandlers.DeleteQualityCycleTemplateCommandHandler(repoMock.Object);
        var id = Guid.NewGuid();

        await handler.Handle(new DeleteQualityCycleTemplateCommand(id), CancellationToken.None);

        repoMock.Verify(r => r.DeleteTemplateAsync(id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetQualityCycleTemplateQueryHandler_WhenExists_ReturnsDto()
    {
        var repoMock = new Mock<IQualityCycleRepository>();
        var template = QualityCycleTemplate.Create("Title", new List<string> { "Item 1" });
        repoMock.Setup(r => r.GetTemplateAsync(template.Id, It.IsAny<CancellationToken>())).ReturnsAsync(template);

        var handler = new QualityCycleHandlers.GetQualityCycleTemplateQueryHandler(repoMock.Object);
        var result = await handler.Handle(new GetQualityCycleTemplateQuery(template.Id), CancellationToken.None);

        Assert.Equal("Title", result.Title);
    }

    [Fact]
    public async Task GetQualityCycleTemplateQueryHandler_WhenNotFound_ThrowsNotFoundException()
    {
        var repoMock = new Mock<IQualityCycleRepository>();
        repoMock.Setup(r => r.GetTemplateAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((QualityCycleTemplate?)null);

        var handler = new QualityCycleHandlers.GetQualityCycleTemplateQueryHandler(repoMock.Object);
        await Assert.ThrowsAsync<NotFoundException>(() => handler.Handle(new GetQualityCycleTemplateQuery(Guid.NewGuid()), CancellationToken.None));
    }

    [Fact]
    public async Task ListQualityCycleTemplatesQueryHandler_ReturnsMappedList()
    {
        var repoMock = new Mock<IQualityCycleRepository>();
        var templates = new List<QualityCycleTemplate>
        {
            QualityCycleTemplate.Create("T1", new List<string> { "I1" }),
            QualityCycleTemplate.Create("T2", new List<string> { "I2" })
        };
        repoMock.Setup(r => r.ListTemplatesAsync(It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>())).ReturnsAsync(templates);

        var handler = new QualityCycleHandlers.ListQualityCycleTemplatesQueryHandler(repoMock.Object);
        var result = await handler.Handle(new ListQualityCycleTemplatesQuery(), CancellationToken.None);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task GetQualityCycleFormByTokenQueryHandler_WhenExists_ReturnsDto()
    {
        var repoMock = new Mock<IQualityCycleRepository>();
        var form = QualityCycleForm.Create(Guid.NewGuid(), "Shift", Guid.NewGuid(), "Cleaner", Guid.NewGuid(), "Template", DateTime.UtcNow, new List<string> { "Item 1" });
        repoMock.Setup(r => r.GetFormByTokenAsync(form.Token, It.IsAny<CancellationToken>())).ReturnsAsync(form);

        var handler = new QualityCycleHandlers.GetQualityCycleFormByTokenQueryHandler(repoMock.Object);
        var result = await handler.Handle(new GetQualityCycleFormByTokenQuery(form.Token), CancellationToken.None);

        Assert.Equal(form.Token, result.Token);
    }

    [Fact]
    public async Task GetQualityCycleFormByTokenQueryHandler_WhenNotFound_ThrowsNotFoundException()
    {
        var repoMock = new Mock<IQualityCycleRepository>();
        repoMock.Setup(r => r.GetFormByTokenAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((QualityCycleForm?)null);

        var handler = new QualityCycleHandlers.GetQualityCycleFormByTokenQueryHandler(repoMock.Object);
        await Assert.ThrowsAsync<NotFoundException>(() => handler.Handle(new GetQualityCycleFormByTokenQuery("invalid_token"), CancellationToken.None));
    }

    [Fact]
    public async Task SubmitQualityCycleFormCommandHandler_UpdatesAndSavesForm()
    {
        var repoMock = new Mock<IQualityCycleRepository>();
        var form = QualityCycleForm.Create(
            Guid.NewGuid(), "Shift 1", Guid.NewGuid(), "Cleaner 1", Guid.NewGuid(), "Template 1", DateTime.UtcNow, new List<string> { "Item 1" });

        repoMock.Setup(x => x.GetFormByTokenAsync(form.Token, It.IsAny<CancellationToken>())).ReturnsAsync(form);

        var handler = new QualityCycleHandlers.SubmitQualityCycleFormCommandHandler(repoMock.Object);
        var command = new SubmitQualityCycleFormCommand(
            form.Token,
            new List<QualityCycleFormItemDto> { new QualityCycleFormItemDto("Item 1", true) },
            new List<string> { "data:image/png;base64,123" },
            "Done!");

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsSubmitted);
        Assert.Equal("Done!", result.CleanerNotes);
        repoMock.Verify(x => x.SaveFormAsync(It.IsAny<QualityCycleForm>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SubmitQualityCycleFormCommandHandler_WhenFormNotFound_ThrowsNotFoundException()
    {
        var repoMock = new Mock<IQualityCycleRepository>();
        repoMock.Setup(r => r.GetFormByTokenAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((QualityCycleForm?)null);

        var handler = new QualityCycleHandlers.SubmitQualityCycleFormCommandHandler(repoMock.Object);
        var command = new SubmitQualityCycleFormCommand("bad_token", new List<QualityCycleFormItemDto>());

        await Assert.ThrowsAsync<NotFoundException>(() => handler.Handle(command, CancellationToken.None));
    }

    [Fact]
    public async Task ListQualityCycleFormsQueryHandler_ReturnsMappedList()
    {
        var repoMock = new Mock<IQualityCycleRepository>();
        var forms = new List<QualityCycleForm>
        {
            QualityCycleForm.Create(Guid.NewGuid(), "S1", Guid.NewGuid(), "E1", Guid.NewGuid(), "T1", DateTime.UtcNow, new List<string> { "I1" })
        };
        repoMock.Setup(r => r.ListFormsAsync(It.IsAny<Guid?>(), It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>())).ReturnsAsync(forms);

        var handler = new QualityCycleHandlers.ListQualityCycleFormsQueryHandler(repoMock.Object);
        var result = await handler.Handle(new ListQualityCycleFormsQuery(), CancellationToken.None);

        Assert.Single(result);
    }

    [Fact]
    public async Task GetQualityCycleSummaryPdfQueryHandler_GeneratesPdf()
    {
        var qcRepoMock = new Mock<IQualityCycleRepository>();
        var shiftRepoMock = new Mock<IShiftRepository>();
        var companyRepoMock = new Mock<CleaningSuite.Application.Companies.ICompanyRepository>();
        var branchRepoMock = new Mock<CleaningSuite.Application.Companies.IBranchRepository>();
        var pdfGenMock = new Mock<IQualityCyclePdfGenerator>();

        var schedule = new ShiftSchedule
        {
            Type = ShiftScheduleType.DailySameTime,
            DailyStart = TimeSpan.Zero,
            DailyEnd = TimeSpan.FromHours(8)
        };
        var shift = Shift.Create(Guid.NewGuid(), Guid.NewGuid(), "Shift 1", schedule, null, null, null);
        var company = Company.Create("1234567-8", "Acme Oy");
        var branch = Branch.Create(company.Id, "HQ");

        shiftRepoMock.Setup(s => s.GetAsync(shift.Id, It.IsAny<CancellationToken>())).ReturnsAsync(shift);
        companyRepoMock.Setup(c => c.GetAsync(shift.CompanyId, It.IsAny<CancellationToken>())).ReturnsAsync(company);
        branchRepoMock.Setup(b => b.GetAsync(shift.BranchId, It.IsAny<CancellationToken>())).ReturnsAsync(branch);
        qcRepoMock.Setup(q => q.ListFormsAsync(shift.Id, It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<QualityCycleForm>());
        pdfGenMock.Setup(p => p.GenerateMonthlySummaryPdf(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<IReadOnlyList<QualityCycleFormDto>>()))
            .Returns(new byte[] { 1, 2, 3 });

        var handler = new QualityCycleHandlers.GetQualityCycleSummaryPdfQueryHandler(
            qcRepoMock.Object, shiftRepoMock.Object, companyRepoMock.Object, branchRepoMock.Object, pdfGenMock.Object);

        var pdf = await handler.Handle(new GetQualityCycleSummaryPdfQuery(shift.Id, 2025, 5), CancellationToken.None);

        Assert.NotNull(pdf);
        Assert.Equal(3, pdf.Length);
    }

    [Fact]
    public async Task GetQualityCycleSummaryPdfQueryHandler_WhenShiftNotFound_ThrowsNotFoundException()
    {
        var shiftRepoMock = new Mock<IShiftRepository>();
        shiftRepoMock.Setup(s => s.GetAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((Shift?)null);

        var handler = new QualityCycleHandlers.GetQualityCycleSummaryPdfQueryHandler(
            new Mock<IQualityCycleRepository>().Object,
            shiftRepoMock.Object,
            new Mock<CleaningSuite.Application.Companies.ICompanyRepository>().Object,
            new Mock<CleaningSuite.Application.Companies.IBranchRepository>().Object,
            new Mock<IQualityCyclePdfGenerator>().Object);

        await Assert.ThrowsAsync<NotFoundException>(() => handler.Handle(new GetQualityCycleSummaryPdfQuery(Guid.NewGuid(), 2025, 5), CancellationToken.None));
    }

    // --- INFRASTRUCTURE TESTS: QualityCyclePdfGenerator ---

    [Fact]
    public void QuestPdfGenerator_GeneratesNonEmptyByteArray()
    {
        var pdfGen = new Infrastructure.QualityCycle.QualityCyclePdfGenerator();
        var form = new QualityCycleFormDto(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Daily Office Shift",
            Guid.NewGuid(),
            "Alex Smith",
            Guid.NewGuid(),
            "Office Checklist",
            DateTime.UtcNow,
            DateTime.UtcNow.AddHours(2),
            "token123",
            new List<QualityCycleFormItemDto> { new QualityCycleFormItemDto("Wipe desks", true), new QualityCycleFormItemDto("Sweep floor", false) },
            new List<string> { "https://example.com/p.jpg" },
            "Cleaned thoroughly",
            true,
            DateTime.UtcNow,
            DateTime.UtcNow,
            DateTime.UtcNow,
            DateTime.UtcNow);

        var pdfBytes = pdfGen.GenerateMonthlySummaryPdf(
            "Daily Office Shift",
            "Acme Corp",
            "Main Branch",
            2025,
            10,
            new List<QualityCycleFormDto> { form });

        Assert.NotNull(pdfBytes);
        Assert.NotEmpty(pdfBytes);
    }

    // --- VALIDATOR TESTS ---

    [Fact]
    public void QualityCycleValidators_ValidatesCommandsCorrectly()
    {
        var createValidator = new QualityCycleValidators.CreateQualityCycleTemplateCommandValidator();
        var updateValidator = new QualityCycleValidators.UpdateQualityCycleTemplateCommandValidator();
        var submitValidator = new QualityCycleValidators.SubmitQualityCycleFormCommandValidator();

        // Create validator
        var createCmdErr = new CreateQualityCycleTemplateCommand("", new List<string>());
        var createResult = createValidator.TestValidate(createCmdErr);
        createResult.ShouldHaveValidationErrorFor(x => x.Title);
        createResult.ShouldHaveValidationErrorFor(x => x.Items);

        // Update validator
        var updateCmdErr = new UpdateQualityCycleTemplateCommand(Guid.Empty, "", new List<string>());
        var updateResult = updateValidator.TestValidate(updateCmdErr);
        updateResult.ShouldHaveValidationErrorFor(x => x.Id);
        updateResult.ShouldHaveValidationErrorFor(x => x.Title);
        updateResult.ShouldHaveValidationErrorFor(x => x.Items);

        // Submit validator
        var submitCmdErr = new SubmitQualityCycleFormCommand("", new List<QualityCycleFormItemDto>());
        var submitResult = submitValidator.TestValidate(submitCmdErr);
        submitResult.ShouldHaveValidationErrorFor(x => x.Token);
    }
}
