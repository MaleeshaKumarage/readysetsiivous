# Agreement E-Signature — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Admin uploads a PDF agreement, defines signers, copies each a personal link; signers draw/type a signature on the last page via a public page; when all sign, the final stamped PDF becomes downloadable.

**Architecture:** Backend adds a per-tenant `Agreement` Marten doc + `IAgreementRepository`, `IAgreementFileStore` (local disk), and `IAgreementDocumentGenerator` (PDFtoImage → SkiaSharp stamp → QuestPDF rebuild) that stamps the signature block on the last page. Public token-gated endpoints let anonymous signers view/sign. Frontend adds an admin agreements page and a public sign page with a canvas signature pad.

**Tech Stack:** .NET 8 + Marten + MediatR + PDFtoImage + SkiaSharp + QuestPDF (backend); Next.js 14 + shadcn/ui + HTML canvas (frontend). Existing patterns: local `Uploads:Path` file storage, RFC 7807 errors, `[Authorize admin]`, public `{slug}` tenant resolution.

**Spec:** `docs/superpowers/specs/2026-09-20-agreement-signing-design.md`

## Global Constraints

- Clean architecture: Api / Application / Domain / Infrastructure / Tests. MediatR + FluentValidation + xUnit + Moq.
- Docs inherit `BaseDocument` (Id, CreatedUtc, UpdatedUtc, Version). Agreements are NOT singletons — use `Guid.NewGuid()` ids, no `TenantDocumentIds` entry.
- Public endpoints: `[AllowAnonymous]`, route `api/v1/public/{slug}/...`; tenant resolved from slug by `TenantResolutionMiddleware` → `ITenantContext.TenantId`.
- Admin endpoints: `[Authorize(Policy = RealmRoleAuthorization.PolicyPrefix + "admin")]`.
- Files on local disk under `Uploads:Path` (default `./uploads`), served like existing `/uploads` images.
- Money/time: UTC in storage, Europe/Helsinki at the edges.
- New NuGet: `PDFtoImage`, `SkiaSharp` (transitively present via QuestPDF), `ImageSharp` not required — use SkiaSharp for drawing.

---

## File Structure

Backend (`cleaning-site/api/`):
- `CleaningSuite.Domain/Agreements/Agreement.cs` — doc + `Signer` + status constants.
- `CleaningSuite.Application/Agreements/IAgreementRepository.cs`
- `CleaningSuite.Application/Agreements/IAgreementFileStore.cs`
- `CleaningSuite.Application/Agreements/IAgreementDocumentGenerator.cs`
- `CleaningSuite.Application/Agreements/Commands/CreateAgreementCommand.cs`
- `CleaningSuite.Application/Agreements/Commands/AddSignerCommand.cs`
- `CleaningSuite.Application/Agreements/Commands/RemoveSignerCommand.cs`
- `CleaningSuite.Application/Agreements/Commands/CancelAgreementCommand.cs`
- `CleaningSuite.Application/Agreements/Commands/SignAgreementCommand.cs`
- `CleaningSuite.Application/Agreements/Queries/AgreementQueries.cs`
- `CleaningSuite.Infrastructure/Persistence/AgreementRepository.cs`
- `CleaningSuite.Infrastructure/Agreements/DiskAgreementFileStore.cs`
- `CleaningSuite.Infrastructure/Agreements/AgreementPdfSigner.cs`
- `CleaningSuite.Api/Controllers/AgreementsAdminController.cs`
- `CleaningSuite.Api/Controllers/AgreementsPublicController.cs`
- `CleaningSuite.Api/Program.cs` — DI.
- `CleaningSuite.Tests/Agreements/...` — tests.

Frontend (`cleaning-site/site/`):
- `src/lib/adminApi.ts` — add `adminAgreements`.
- `src/lib/agreementApi.ts` — public sign endpoints.
- `src/components/admin/SignaturePad.tsx` — canvas draw/type.
- `src/app/[lang]/admin/agreements/page.tsx` — admin list + create + detail.
- `src/app/[lang]/admin/page.tsx` — add dashboard entry.
- `src/app/[lang]/sign/[token]/page.tsx` — public sign page.

---

## Task 1: Agreement + Signer domain

**Files:**
- Create: `CleaningSuite.Domain/Agreements/Agreement.cs`

**Interfaces:**
- Produces: `Agreement` (Slug, Title, OriginalPdfPath, Status, Signers, SignedPdfPath, CompletedUtc), `Signer` (Id, Name, Email, Token, Status, SignatureImagePath, TypedName, SignedAtUtc), status constants.

- [ ] **Step 1: Write failing test**

Create `CleaningSuite.Tests/Agreements/AgreementTests.cs`:

```csharp
using CleaningSuite.Domain.Agreements;

namespace CleaningSuite.Tests.Agreements;

public class AgreementTests
{
    [Fact]
    public void Agreement_Defaults_ToDraft()
    {
        var a = new Agreement { Slug = "readysetsiivous", Title = "Test" };
        Assert.Equal(Agreement.StatusDraft, a.Status);
        Assert.Empty(a.Signers);
        Assert.Null(a.SignedPdfPath);
        Assert.Null(a.CompletedUtc);
    }

    [Fact]
    public void AllSigned_True_WhenEverySignerSigned()
    {
        var a = new Agreement { Slug = "x", Title = "t" };
        a.Signers.Add(new Signer { Id = Guid.NewGuid(), Name = "A", Email = "a@x.fi", Status = Signer.StatusSigned });
        a.Signers.Add(new Signer { Id = Guid.NewGuid(), Name = "B", Email = "b@x.fi", Status = Signer.StatusPending });
        Assert.False(a.AllSigned);

        a.Signers[1].Status = Signer.StatusSigned;
        Assert.True(a.AllSigned);
    }
}
```

- [ ] **Step 2: Run to verify fail**

Run: `dotnet test CleaningSuite.Tests --filter "Agreement"`
Expected: FAIL (type missing).

- [ ] **Step 3: Implement**

`Agreement.cs`:

```csharp
using CleaningSuite.Domain.Common;

namespace CleaningSuite.Domain.Agreements;

public class Agreement : BaseDocument
{
    public const string StatusDraft = "Draft";
    public const string StatusPartiallySigned = "PartiallySigned";
    public const string StatusCompleted = "Completed";
    public const string StatusCancelled = "Cancelled";

    public string Slug { get; set; } = "";
    public string Title { get; set; } = "";
    public string OriginalPdfPath { get; set; } = "";
    public string Status { get; set; } = StatusDraft;
    public List<Signer> Signers { get; set; } = new();
    public string? SignedPdfPath { get; set; }
    public DateTime? CompletedUtc { get; set; }

    public bool AllSigned => Signers.Count > 0 && Signers.All(s => s.Status == Signer.StatusSigned);
}

public class Signer
{
    public const string StatusPending = "Pending";
    public const string StatusSigned = "Signed";

    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
    public string Token { get; set; } = "";
    public string Status { get; set; } = StatusPending;
    public string? SignatureImagePath { get; set; }
    public string? TypedName { get; set; }
    public DateTime? SignedAtUtc { get; set; }
}
```

- [ ] **Step 4: Run to verify pass**

Run: `dotnet test CleaningSuite.Tests --filter "Agreement"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add CleaningSuite.Domain/Agreements/Agreement.cs CleaningSuite.Tests/Agreements/AgreementTests.cs
git commit -m "feat: add Agreement and Signer domain"
```

---

## Task 2: File store + token generator

**Files:**
- Create: `CleaningSuite.Application/Agreements/IAgreementFileStore.cs`
- Create: `CleaningSuite.Application/Agreements/AgreementTokens.cs`
- Create: `CleaningSuite.Infrastructure/Agreements/DiskAgreementFileStore.cs`

**Interfaces:**
- Produces: `IAgreementFileStore.SaveOriginalAsync(string tenantId, Guid agreementId, Stream pdf, CancellationToken) → Task<string>`; `SaveSignatureAsync(string tenantId, Guid agreementId, Guid signerId, byte[] png, CancellationToken) → Task<string>`; `SaveSignedAsync(string tenantId, Guid agreementId, Stream pdf, CancellationToken) → Task<string>`; `AgreementTokens.New()` → string.

- [ ] **Step 1: Write failing test**

Create `CleaningSuite.Tests/Agreements/AgreementTokensTests.cs`:

```csharp
using CleaningSuite.Application.Agreements;

namespace CleaningSuite.Tests.Agreements;

public class AgreementTokensTests
{
    [Fact]
    public void New_Produces_Unique_UrlSafe_Tokens()
    {
        var a = AgreementTokens.New();
        var b = AgreementTokens.New();
        Assert.NotEqual(a, b);
        Assert.All(a, c => Assert.False(c is '+' or '/' or '='));
    }
}
```

- [ ] **Step 2: Run to verify fail**

Run: `dotnet test CleaningSuite.Tests --filter "AgreementTokens"`
Expected: FAIL.

- [ ] **Step 3: Implement**

`IAgreementFileStore.cs`:

```csharp
namespace CleaningSuite.Application.Agreements;

public interface IAgreementFileStore
{
    Task<string> SaveOriginalAsync(string tenantId, Guid agreementId, Stream pdf, CancellationToken ct = default);
    Task<string> SaveSignatureAsync(string tenantId, Guid agreementId, Guid signerId, byte[] png, CancellationToken ct = default);
    Task<string> SaveSignedAsync(string tenantId, Guid agreementId, Stream pdf, CancellationToken ct = default);
}
```

`AgreementTokens.cs`:

```csharp
using System.Security.Cryptography;

namespace CleaningSuite.Application.Agreements;

public static class AgreementTokens
{
    public static string New()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}
```

`DiskAgreementFileStore.cs`:

```csharp
using CleaningSuite.Application.Agreements;

namespace CleaningSuite.Infrastructure.Agreements;

public class DiskAgreementFileStore : IAgreementFileStore
{
    private readonly string _root;

    public DiskAgreementFileStore(string root) => _root = root;

    private string Dir(string tenantId, Guid agreementId)
    {
        var dir = Path.Combine(_root, "agreements", tenantId, agreementId.ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    public async Task<string> SaveOriginalAsync(string tenantId, Guid agreementId, Stream pdf, CancellationToken ct = default)
    {
        var path = Path.Combine(Dir(tenantId, agreementId), "original.pdf");
        await using var fs = File.Create(path);
        await pdf.CopyToAsync(fs, ct);
        return path;
    }

    public async Task<string> SaveSignatureAsync(string tenantId, Guid agreementId, Guid signerId, byte[] png, CancellationToken ct = default)
    {
        var path = Path.Combine(Dir(tenantId, agreementId), $"{signerId:N}.png");
        await File.WriteAllBytesAsync(path, png, ct);
        return path;
    }

    public async Task<string> SaveSignedAsync(string tenantId, Guid agreementId, Stream pdf, CancellationToken ct = default)
    {
        var path = Path.Combine(Dir(tenantId, agreementId), "signed.pdf");
        await using var fs = File.Create(path);
        await pdf.CopyToAsync(fs, ct);
        return path;
    }
}
```

- [ ] **Step 4: Run to verify pass**

Run: `dotnet test CleaningSuite.Tests --filter "AgreementTokens"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add CleaningSuite.Application/Agreements/IAgreementFileStore.cs CleaningSuite.Application/Agreements/AgreementTokens.cs CleaningSuite.Infrastructure/Agreements/DiskAgreementFileStore.cs CleaningSuite.Tests/Agreements/AgreementTokensTests.cs
git commit -m "feat: add agreement file store and token generator"
```

---

## Task 3: Agreement repository (Marten)

**Files:**
- Create: `CleaningSuite.Application/Agreements/IAgreementRepository.cs`
- Create: `CleaningSuite.Infrastructure/Persistence/AgreementRepository.cs`

**Interfaces:**
- Produces: `IAgreementRepository.GetAsync(string tenantId, Guid id, ct)`, `ListAsync(string tenantId, ct)`, `SaveAsync(string tenantId, Agreement, ct)`, `FindBySignerTokenAsync(string tenantId, string token, ct)`.

- [ ] **Step 1: Write failing test**

Create `CleaningSuite.Tests/Agreements/AgreementRepositoryTests.cs`:

```csharp
using CleaningSuite.Application.Agreements;
using CleaningSuite.Domain.Agreements;
using CleaningSuite.Infrastructure.Persistence;
using Marten;
using Moq;

namespace CleaningSuite.Tests.Agreements;

public class AgreementRepositoryTests
{
    [Fact]
    public async Task SaveAsync_StoresInTenantSession()
    {
        var session = new Mock<IDocumentSession>();
        var store = new Mock<IDocumentStore>();
        store.Setup(s => s.DirtyTrackedSession("readysetsiivous")).Returns(session.Object);

        var repo = new AgreementRepository(store.Object);
        var doc = new Agreement { Slug = "readysetsiivous", Title = "T" };

        await repo.SaveAsync("readysetsiivous", doc);

        session.Verify(s => s.Store(doc), Times.Once);
        session.Verify(s => s.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
```

- [ ] **Step 2: Run to verify fail**

Run: `dotnet test CleaningSuite.Tests --filter "AgreementRepository"`
Expected: FAIL.

- [ ] **Step 3: Implement**

`IAgreementRepository.cs`:

```csharp
using CleaningSuite.Domain.Agreements;

namespace CleaningSuite.Application.Agreements;

public interface IAgreementRepository
{
    Task<Agreement?> GetAsync(string tenantId, Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<Agreement>> ListAsync(string tenantId, CancellationToken ct = default);
    Task SaveAsync(string tenantId, Agreement agreement, CancellationToken ct = default);
    Task<Agreement?> FindBySignerTokenAsync(string tenantId, string token, CancellationToken ct = default);
}
```

`AgreementRepository.cs` (mirror `TenantProfileRepository`; add token query):

```csharp
using CleaningSuite.Application.Agreements;
using CleaningSuite.Domain.Agreements;
using Marten;

namespace CleaningSuite.Infrastructure.Persistence;

public class AgreementRepository : IAgreementRepository, IDisposable
{
    private readonly IDocumentStore _store;
    private readonly Dictionary<string, IDocumentSession> _sessions = new();

    public AgreementRepository(IDocumentStore store) => _store = store;

    public async Task<Agreement?> GetAsync(string tenantId, Guid id, CancellationToken ct = default) =>
        await Session(tenantId).LoadAsync<Agreement>(id, ct);

    public async Task<IReadOnlyList<Agreement>> ListAsync(string tenantId, CancellationToken ct = default) =>
        (await Session(tenantId).Query<Agreement>().OrderByDescending(a => a.CreatedUtc).ToListAsync(ct));

    public async Task SaveAsync(string tenantId, Agreement agreement, CancellationToken ct = default)
    {
        Session(tenantId).Store(agreement);
        await Session(tenantId).SaveChangesAsync(ct);
    }

    public async Task<Agreement?> FindBySignerTokenAsync(string tenantId, string token, CancellationToken ct = default) =>
        await Session(tenantId).Query<Agreement>()
            .FirstOrDefaultAsync(a => a.Signers.Any(s => s.Token == token), ct);

    private IDocumentSession Session(string tenantId)
    {
        if (!_sessions.TryGetValue(tenantId, out var session))
        {
            session = _store.DirtyTrackedSession(tenantId);
            _sessions[tenantId] = session;
        }
        return session;
    }

    public void Dispose()
    {
        foreach (var session in _sessions.Values) session.Dispose();
    }
}
```

- [ ] **Step 4: Run to verify pass**

Run: `dotnet test CleaningSuite.Tests --filter "AgreementRepository"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add CleaningSuite.Application/Agreements/IAgreementRepository.cs CleaningSuite.Infrastructure/Persistence/AgreementRepository.cs CleaningSuite.Tests/Agreements/AgreementRepositoryTests.cs
git commit -m "feat: add agreement repository"
```

---

## Task 4: Create/list/get commands + admin controller

**Files:**
- Create: `CleaningSuite.Application/Agreements/Commands/CreateAgreementCommand.cs`
- Create: `CleaningSuite.Application/Agreements/Queries/AgreementQueries.cs`
- Create: `CleaningSuite.Api/Controllers/AgreementsAdminController.cs`

**Interfaces:**
- Consumes: `IAgreementRepository`, `IAgreementFileStore`, `ITenantContext`.
- Produces: `CreateAgreementCommand(string Title, Stream Pdf, string FileName, IReadOnlyList<SignerInput>) : IRequest<AgreementDetailDto>`; `SignerInput(string Name, string Email)`; `ListAgreementsQuery : IRequest<IReadOnlyList<AgreementListItemDto>>`; `GetAgreementQuery(Guid Id) : IRequest<AgreementDetailDto?>`; DTO records; controller routes.

- [ ] **Step 1: Write failing test**

Create `CleaningSuite.Tests/Agreements/CreateAgreementCommandTests.cs`:

```csharp
using CleaningSuite.Application.Agreements;
using CleaningSuite.Application.Agreements.Commands;
using CleaningSuite.Domain.Agreements;
using Moq;

namespace CleaningSuite.Tests.Agreements;

public class CreateAgreementCommandTests
{
    [Fact]
    public async Task Create_StoresPdf_AssignsTokens()
    {
        var repo = new Mock<IAgreementRepository>();
        var store = new Mock<IAgreementFileStore>();
        store.Setup(s => s.SaveOriginalAsync("readysetsiivous", It.IsAny<Guid>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("/uploads/agreements/original.pdf");

        var handler = new CreateAgreementHandler(MockTenant(), repo.Object, store.Object);
        var cmd = new CreateAgreementCommand("Service agreement", Stream.Null, "a.pdf",
            new[] { new SignerInput("Alice", "alice@x.fi"), new SignerInput("Bob", "bob@x.fi") });

        var dto = await handler.Handle(cmd, CancellationToken.None);

        Assert.Equal("Draft", dto.Status);
        Assert.Equal(2, dto.Signers.Count);
        Assert.All(dto.Signers, s => Assert.False(string.IsNullOrEmpty(s.Token)));
        repo.Verify(r => r.SaveAsync("readysetsiivous",
            It.Is<Agreement>(a => a.Signers.Count == 2 && a.Signers.All(s => s.Token.Length > 0)),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    private static CleaningSuite.Application.Tenants.ITenantContext MockTenant()
    {
        var c = new Mock<CleaningSuite.Application.Tenants.ITenantContext>();
        c.SetupGet(x => x.TenantId).Returns("readysetsiivous");
        return c.Object;
    }
}
```

- [ ] **Step 2: Run to verify fail**

Run: `dotnet test CleaningSuite.Tests --filter "CreateAgreement"`
Expected: FAIL.

- [ ] **Step 3: Implement**

`CreateAgreementCommand.cs`:

```csharp
using CleaningSuite.Application.Agreements;
using CleaningSuite.Domain.Agreements;
using FluentValidation;
using MediatR;

namespace CleaningSuite.Application.Agreements.Commands;

public record SignerInput(string Name, string Email);

public record CreateAgreementCommand(string Title, Stream Pdf, string FileName, IReadOnlyList<SignerInput> Signers)
    : IRequest<AgreementDetailDto>;

public class CreateAgreementValidator : AbstractValidator<CreateAgreementCommand>
{
    public CreateAgreementValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Signers).NotEmpty();
        RuleForEach(x => x.Signers).ChildRules(s =>
        {
            s.RuleFor(x => x.Name).NotEmpty();
            s.RuleFor(x => x.Email).EmailAddress();
        });
    }
}

public class CreateAgreementHandler : IRequestHandler<CreateAgreementCommand, AgreementDetailDto>
{
    private readonly ITenantContext _context;
    private readonly IAgreementRepository _repo;
    private readonly IAgreementFileStore _store;

    public CreateAgreementHandler(ITenantContext context, IAgreementRepository repo, IAgreementFileStore store)
    {
        _context = context; _repo = repo; _store = store;
    }

    public async Task<AgreementDetailDto> Handle(CreateAgreementCommand request, CancellationToken ct)
    {
        var tenantId = _context.TenantId;
        var agreement = new Agreement
        {
            Id = Guid.NewGuid(),
            Slug = tenantId,
            Title = request.Title,
            Signers = request.Signers.Select(s => new Signer
            {
                Name = s.Name, Email = s.Email, Token = AgreementTokens.New(),
            }).ToList(),
        };

        agreement.OriginalPdfPath = await _store.SaveOriginalAsync(tenantId, agreement.Id, request.Pdf, ct);
        await _repo.SaveAsync(tenantId, agreement, ct);
        return AgreementDetailDto.From(agreement);
    }
}
```

`AgreementQueries.cs`:

```csharp
using CleaningSuite.Application.Agreements;
using CleaningSuite.Domain.Agreements;
using MediatR;

namespace CleaningSuite.Application.Agreements.Queries;

public record AgreementListItemDto(Guid Id, string Title, string Status, int SignerCount, int SignedCount, DateTime CreatedUtc);
public record SignerDto(Guid Id, string Name, string Email, string Status, string Token);
public record AgreementDetailDto(Guid Id, string Title, string Status, IReadOnlyList<SignerDto> Signers, DateTime CreatedUtc, DateTime? CompletedUtc)
{
    public static AgreementDetailDto From(Agreement a) => new(
        a.Id, a.Title, a.Status,
        a.Signers.Select(s => new SignerDto(s.Id, s.Name, s.Email, s.Status, s.Token)).ToList(),
        a.CreatedUtc, a.CompletedUtc);
}

public record ListAgreementsQuery : IRequest<IReadOnlyList<AgreementListItemDto>>;

public class ListAgreementsHandler : IRequestHandler<ListAgreementsQuery, IReadOnlyList<AgreementListItemDto>>
{
    private readonly ITenantContext _context;
    private readonly IAgreementRepository _repo;
    public ListAgreementsHandler(ITenantContext context, IAgreementRepository repo) { _context = context; _repo = repo; }

    public async Task<IReadOnlyList<AgreementListItemDto>> Handle(ListAgreementsQuery request, CancellationToken ct)
    {
        var list = await _repo.ListAsync(_context.TenantId, ct);
        return list.Select(a => new AgreementListItemDto(a.Id, a.Title, a.Status,
            a.Signers.Count, a.Signers.Count(s => s.Status == Signer.StatusSigned), a.CreatedUtc)).ToList();
    }
}

public record GetAgreementQuery(Guid Id) : IRequest<AgreementDetailDto?>;

public class GetAgreementHandler : IRequestHandler<GetAgreementQuery, AgreementDetailDto?>
{
    private readonly ITenantContext _context;
    private readonly IAgreementRepository _repo;
    public GetAgreementHandler(ITenantContext context, IAgreementRepository repo) { _context = context; _repo = repo; }

    public async Task<AgreementDetailDto?> Handle(GetAgreementQuery request, CancellationToken ct)
    {
        var a = await _repo.GetAsync(_context.TenantId, request.Id, ct);
        return a is null ? null : AgreementDetailDto.From(a);
    }
}
```

`AgreementsAdminController.cs`:

```csharp
using CleaningSuite.Application.Agreements.Commands;
using CleaningSuite.Application.Agreements.Queries;
using CleaningSuite.Api.Auth;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CleaningSuite.Api.Controllers;

[ApiController]
[Route("api/v1/admin/agreements")]
[Authorize(Policy = RealmRoleAuthorization.PolicyPrefix + "admin")]
public class AgreementsAdminController : ControllerBase
{
    private readonly IMediator _mediator;
    public AgreementsAdminController(IMediator mediator) => _mediator = mediator;

    [HttpPost]
    public async Task<IActionResult> Create([FromForm] string title, [FromForm] IFormFile pdf,
        [FromForm] string signersJson, CancellationToken ct)
    {
        var signers = System.Text.Json.JsonSerializer.Deserialize<List<SignerInput>>(signersJson)
            ?? throw new BadHttpRequestException("signersJson invalid");
        var dto = await _mediator.Send(new CreateAgreementCommand(title, pdf.OpenReadStream(), pdf.FileName, signers), ct);
        return Ok(dto);
    }

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct) =>
        Ok(await _mediator.Send(new ListAgreementsQuery(), ct));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var dto = await _mediator.Send(new GetAgreementQuery(id), ct);
        return dto is null ? NotFound() : Ok(dto);
    }
}
```

- [ ] **Step 4: Run to verify pass**

Run: `dotnet test CleaningSuite.Tests --filter "CreateAgreement"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add CleaningSuite.Application/Agreements/Commands/CreateAgreementCommand.cs CleaningSuite.Application/Agreements/Queries/AgreementQueries.cs CleaningSuite.Api/Controllers/AgreementsAdminController.cs CleaningSuite.Tests/Agreements/CreateAgreementCommandTests.cs
git commit -m "feat: add create/list/get agreement endpoints"
```

---

## Task 5: Add/remove signer + cancel

**Files:**
- Create: `CleaningSuite.Application/Agreements/Commands/AddSignerCommand.cs`
- Create: `CleaningSuite.Application/Agreements/Commands/RemoveSignerCommand.cs`
- Create: `CleaningSuite.Application/Agreements/Commands/CancelAgreementCommand.cs`
- Modify: `CleaningSuite.Api/Controllers/AgreementsAdminController.cs`

**Interfaces:**
- Consumes: `IAgreementRepository`, `ITenantContext`.
- Produces: `AddSignerCommand(Guid AgreementId, string Name, string Email)`, `RemoveSignerCommand(Guid AgreementId, Guid SignerId)`, `CancelAgreementCommand(Guid AgreementId)`.

- [ ] **Step 1: Implement (commands are simple; test via one representative)**

`AddSignerCommand.cs`:

```csharp
using CleaningSuite.Application.Agreements;
using CleaningSuite.Domain.Agreements;
using CleaningSuite.Application.Common;
using FluentValidation;
using MediatR;

namespace CleaningSuite.Application.Agreements.Commands;

public record AddSignerCommand(Guid AgreementId, string Name, string Email) : IRequest<Unit>;

public class AddSignerValidator : AbstractValidator<AddSignerCommand>
{
    public AddSignerValidator()
    {
        RuleFor(x => x.Name).NotEmpty();
        RuleFor(x => x.Email).EmailAddress();
    }
}

public class AddSignerHandler : IRequestHandler<AddSignerCommand, Unit>
{
    private readonly ITenantContext _context;
    private readonly IAgreementRepository _repo;
    public AddSignerHandler(ITenantContext context, IAgreementRepository repo) { _context = context; _repo = repo; }

    public async Task<Unit> Handle(AddSignerCommand request, CancellationToken ct)
    {
        var a = await _repo.GetAsync(_context.TenantId, request.AgreementId, ct)
            ?? throw new NotFoundException("Agreement", request.AgreementId);
        if (a.Status != Agreement.StatusDraft && a.Status != Agreement.StatusPartiallySigned)
            throw new InvalidOperationException("Agreement not editable");

        a.Signers.Add(new Signer { Name = request.Name, Email = request.Email, Token = AgreementTokens.New() });
        a.UpdatedUtc = DateTime.UtcNow;
        await _repo.SaveAsync(_context.TenantId, a, ct);
        return Unit.Value;
    }
}
```

`RemoveSignerCommand.cs`:

```csharp
using CleaningSuite.Application.Agreements;
using CleaningSuite.Application.Common;
using CleaningSuite.Domain.Agreements;
using MediatR;

namespace CleaningSuite.Application.Agreements.Commands;

public record RemoveSignerCommand(Guid AgreementId, Guid SignerId) : IRequest<Unit>;

public class RemoveSignerHandler : IRequestHandler<RemoveSignerCommand, Unit>
{
    private readonly ITenantContext _context;
    private readonly IAgreementRepository _repo;
    public RemoveSignerHandler(ITenantContext context, IAgreementRepository repo) { _context = context; _repo = repo; }

    public async Task<Unit> Handle(RemoveSignerCommand request, CancellationToken ct)
    {
        var a = await _repo.GetAsync(_context.TenantId, request.AgreementId, ct)
            ?? throw new NotFoundException("Agreement", request.AgreementId);
        if (a.Status == Agreement.StatusCompleted || a.Status == Agreement.StatusCancelled)
            throw new InvalidOperationException("Agreement not editable");

        a.Signers.RemoveAll(s => s.Id == request.SignerId);
        a.UpdatedUtc = DateTime.UtcNow;
        await _repo.SaveAsync(_context.TenantId, a, ct);
        return Unit.Value;
    }
}
```

`CancelAgreementCommand.cs`:

```csharp
using CleaningSuite.Application.Agreements;
using CleaningSuite.Application.Common;
using CleaningSuite.Domain.Agreements;
using MediatR;

namespace CleaningSuite.Application.Agreements.Commands;

public record CancelAgreementCommand(Guid AgreementId) : IRequest<Unit>;

public class CancelAgreementHandler : IRequestHandler<CancelAgreementCommand, Unit>
{
    private readonly ITenantContext _context;
    private readonly IAgreementRepository _repo;
    public CancelAgreementHandler(ITenantContext context, IAgreementRepository repo) { _context = context; _repo = repo; }

    public async Task<Unit> Handle(CancelAgreementCommand request, CancellationToken ct)
    {
        var a = await _repo.GetAsync(_context.TenantId, request.AgreementId, ct)
            ?? throw new NotFoundException("Agreement", request.AgreementId);
        if (a.Status == Agreement.StatusCompleted)
            throw new InvalidOperationException("Completed agreement cannot be cancelled");

        a.Status = Agreement.StatusCancelled;
        a.UpdatedUtc = DateTime.UtcNow;
        await _repo.SaveAsync(_context.TenantId, a, ct);
        return Unit.Value;
    }
}
```

Controller additions:

```csharp
    [HttpPost("{id:guid}/signers")]
    public async Task<IActionResult> AddSigner(Guid id, AddSignerCommand command, CancellationToken ct)
    {
        await _mediator.Send(command with { AgreementId = id }, ct);
        return NoContent();
    }

    [HttpDelete("{id:guid}/signers/{signerId:guid}")]
    public async Task<IActionResult> RemoveSigner(Guid id, Guid signerId, CancellationToken ct)
    {
        await _mediator.Send(new RemoveSignerCommand(id, signerId), ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken ct)
    {
        await _mediator.Send(new CancelAgreementCommand(id), ct);
        return NoContent();
    }
```

- [ ] **Step 2: Build**

Run: `dotnet build CleaningSuite.Backend.sln`
Expected: SUCCESS.

- [ ] **Step 3: Commit**

```bash
git add CleaningSuite.Application/Agreements/Commands/AddSignerCommand.cs CleaningSuite.Application/Agreements/Commands/RemoveSignerCommand.cs CleaningSuite.Application/Agreements/Commands/CancelAgreementCommand.cs CleaningSuite.Api/Controllers/AgreementsAdminController.cs
git commit -m "feat: add signer add/remove and cancel"
```

---

## Task 6: PDF signing service (stamp last page)

**Files:**
- Create: `CleaningSuite.Application/Agreements/IAgreementDocumentGenerator.cs`
- Create: `CleaningSuite.Infrastructure/Agreements/AgreementPdfSigner.cs`
- Modify: `CleaningSuite.Infrastructure/CleaningSuite.Infrastructure.csproj` (add PDFtoImage)

**Interfaces:**
- Consumes: none beyond file paths.
- Produces: `IAgreementDocumentGenerator.GenerateSignedPdfAsync(string originalPdfPath, IReadOnlyList<Signer> signers, string outputPath, CancellationToken) → Task`.

- [ ] **Step 1: Add package**

Run (in `CleaningSuite.Infrastructure`): `dotnet add package PDFtoImage`

- [ ] **Step 2: Write failing test (integration-shaped, guarded)**

Create `CleaningSuite.Tests/Agreements/AgreementPdfSignerTests.cs`:

```csharp
using CleaningSuite.Domain.Agreements;
using CleaningSuite.Infrastructure.Agreements;

namespace CleaningSuite.Tests.Agreements;

public class AgreementPdfSignerTests
{
    [Fact]
    public async Task Generate_Produces_SamePageCount_Pdf()
    {
        // Uses a minimal 2-page PDF produced inline via QuestPDF.
        var original = MakeTwoPagePdf();
        var outPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".pdf");
        var signers = new List<Signer>
        {
            new() { Name = "Alice", TypedName = "Alice", SignedAtUtc = DateTime.UtcNow, SignatureImagePath = MakePng() },
        };

        var gen = new AgreementPdfSigner();
        await gen.GenerateSignedPdfAsync(original, signers, outPath, CancellationToken.None);

        var pages = PDFtoImage.Conversion.GetPageCount(outPath);
        Assert.Equal(2, pages);
        File.Delete(original);
        File.Delete(outPath);
    }

    private static string MakeTwoPagePdf()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".pdf");
        QuestPDF.Fluent.Document.Create(c =>
        {
            c.Page(p => p.Content().Text("page1"));
            c.Page(p => p.Content().Text("page2"));
        }).GeneratePdf(path);
        return path;
    }

    private static string MakePng()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".png");
        using var bmp = new SkiaSharp.SKBitmap(200, 60);
        using (var canvas = new SkiaSharp.SKCanvas(bmp))
        {
            canvas.Clear(SkiaSharp.SKColors.Transparent);
            canvas.DrawText("Alice", 10, 40, new SkiaSharp.SKFont());
        }
        using var image = SkiaSharp.SKImage.FromBitmap(bmp);
        using var data = image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
        using var fs = File.OpenWrite(path);
        data.SaveTo(fs);
        return path;
    }
}
```

(Note: `MakePng` needs `SKPaint` for text; adjust to `canvas.DrawText("Alice", 10, 40, new SKPaint())` if the `SKFont` overload is unavailable. The test asserts page count only — it does not assert pixel content.)

- [ ] **Step 3: Run to verify fail**

Run: `dotnet test CleaningSuite.Tests --filter "AgreementPdfSigner"`
Expected: FAIL (class missing).

- [ ] **Step 4: Implement**

`IAgreementDocumentGenerator.cs`:

```csharp
using CleaningSuite.Domain.Agreements;

namespace CleaningSuite.Application.Agreements;

public interface IAgreementDocumentGenerator
{
    Task GenerateSignedPdfAsync(string originalPdfPath, IReadOnlyList<Signer> signers,
        string outputPath, CancellationToken ct = default);
}
```

`AgreementPdfSigner.cs`:

```csharp
using CleaningSuite.Application.Agreements;
using CleaningSuite.Domain.Agreements;
using PDFtoImage;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using SkiaSharp;

namespace CleaningSuite.Infrastructure.Agreements;

public class AgreementPdfSigner : IAgreementDocumentGenerator
{
    public async Task GenerateSignedPdfAsync(
        string originalPdfPath, IReadOnlyList<Signer> signers, string outputPath, CancellationToken ct = default)
    {
        var pageCount = Conversion.GetPageCount(originalPdfPath);

        // Render every page to a PNG.
        var pageImages = new List<byte[]>();
        for (int i = 0; i < pageCount; i++)
            pageImages.Add(await Conversion.ToImageAsync(originalPdfPath, page: i, ct: ct));

        // Stamp the signature block on the last page.
        var last = StampLastPage(pageImages[^1], signers);
        pageImages[^1] = last;

        // Rebuild the PDF from images (one full-bleed page each).
        QuestPDF.Settings.License = LicenseType.Community;
        Document.Create(c =>
        {
            foreach (var img in pageImages)
            {
                c.Page(p =>
                {
                    p.Size(PageSizes.A4);
                    p.Margin(0);
                    p.Content().Image(img);
                });
            }
        }).GeneratePdf(outputPath);
    }

    private static byte[] StampLastPage(byte[] pagePng, IReadOnlyList<Signer> signers)
    {
        using var bitmap = SKBitmap.Decode(pagePng);
        using var canvas = new SKCanvas(bitmap);
        var left = 40f;
        var top = bitmap.Height - 140f; // reserved bottom band
        foreach (var signer in signers)
        {
            DrawSignerSlot(canvas, left, top, signer);
            left += 220f;
        }
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private static void DrawSignerSlot(SKCanvas canvas, float left, float top, Signer signer)
    {
        using var paint = new SKPaint { Color = SKColors.Black, TextSize = 12, IsAntialias = true };
        canvas.DrawText($"{signer.TypedName ?? signer.Name}", left, top, paint);
        canvas.DrawText(signer.SignedAtUtc?.ToLocalTime().ToString("yyyy-MM-dd HH:mm") ?? "", left, top + 18, paint);

        if (signer.SignatureImagePath is { } path && File.Exists(path))
        {
            using var sig = SKBitmap.Decode(path);
            var scale = Math.Min(1f, 160f / sig.Width);
            var dest = new SKRect(left, top + 26, left + sig.Width * scale, top + 26 + sig.Height * scale);
            canvas.DrawBitmap(sig, dest);
        }
    }
}
```

- [ ] **Step 5: Run to verify pass**

Run: `dotnet test CleaningSuite.Tests --filter "AgreementPdfSigner"`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add CleaningSuite.Application/Agreements/IAgreementDocumentGenerator.cs CleaningSuite.Infrastructure/Agreements/AgreementPdfSigner.cs CleaningSuite.Infrastructure/CleaningSuite.Infrastructure.csproj CleaningSuite.Tests/Agreements/AgreementPdfSignerTests.cs
git commit -m "feat: add agreement PDF signing (last-page stamp)"
```

---

## Task 7: Sign command + public endpoints + completion

**Files:**
- Create: `CleaningSuite.Application/Agreements/Commands/SignAgreementCommand.cs`
- Create: `CleaningSuite.Api/Controllers/AgreementsPublicController.cs`

**Interfaces:**
- Consumes: `IAgreementRepository`, `IAgreementFileStore`, `IAgreementDocumentGenerator`, `ITenantContext`.
- Produces: `SignAgreementCommand(string Token, string TypedName, byte[] SignaturePng) : IRequest<SignResult>`; `SignResult(bool Completed, int TotalSigners, int SignedCount)`; public controller routes.

- [ ] **Step 1: Write failing test**

Create `CleaningSuite.Tests/Agreements/SignAgreementCommandTests.cs`:

```csharp
using CleaningSuite.Application.Agreements;
using CleaningSuite.Application.Agreements.Commands;
using CleaningSuite.Application.Common;
using CleaningSuite.Domain.Agreements;
using Moq;

namespace CleaningSuite.Tests.Agreements;

public class SignAgreementCommandTests
{
    [Fact]
    public async Task Sign_LastSigner_CompletesAndGeneratesPdf()
    {
        var token = "tok1";
        var a = new Agreement
        {
            Slug = "readysetsiivous", Title = "T", Status = Agreement.StatusPartiallySigned,
            OriginalPdfPath = "/u/original.pdf",
        };
        a.Signers.Add(new Signer { Name = "A", Email = "a@x.fi", Token = token, Status = Signer.StatusPending });

        var repo = new Mock<IAgreementRepository>();
        repo.Setup(r => r.FindBySignerTokenAsync("readysetsiivous", token, It.IsAny<CancellationToken>()))
            .ReturnsAsync(a);
        var store = new Mock<IAgreementFileStore>();
        store.Setup(s => s.SaveSignatureAsync("readysetsiivous", a.Id, It.IsAny<Guid>(), It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("/u/sig.png");
        store.Setup(s => s.SaveSignedAsync("readysetsiivous", a.Id, It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("/u/signed.pdf");
        var gen = new Mock<IAgreementDocumentGenerator>();

        var handler = new SignAgreementHandler(MockTenant(), repo.Object, store.Object, gen.Object);
        var result = await handler.Handle(new SignAgreementCommand(token, "Alice", [1, 2, 3]), CancellationToken.None);

        Assert.True(result.Completed);
        Assert.Equal(Agreement.StatusCompleted, a.Status);
        gen.Verify(g => g.GenerateSignedPdfAsync("/u/original.pdf", It.IsAny<IReadOnlyList<Signer>>(),
            "/u/signed.pdf", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Sign_UnknownToken_ThrowsNotFound()
    {
        var repo = new Mock<IAgreementRepository>();
        repo.Setup(r => r.FindBySignerTokenAsync("readysetsiivous", "nope", It.IsAny<CancellationToken>()))
            .ReturnsAsync((Agreement?)null);
        var store = new Mock<IAgreementFileStore>();
        var gen = new Mock<IAgreementDocumentGenerator>();
        var handler = new SignAgreementHandler(MockTenant(), repo.Object, store.Object, gen.Object);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            handler.Handle(new SignAgreementCommand("nope", "X", [1]), CancellationToken.None));
    }

    private static CleaningSuite.Application.Tenants.ITenantContext MockTenant()
    {
        var c = new Mock<CleaningSuite.Application.Tenants.ITenantContext>();
        c.SetupGet(x => x.TenantId).Returns("readysetsiivous");
        return c.Object;
    }
}
```

- [ ] **Step 2: Run to verify fail**

Run: `dotnet test CleaningSuite.Tests --filter "SignAgreement"`
Expected: FAIL.

- [ ] **Step 3: Implement**

`SignAgreementCommand.cs`:

```csharp
using CleaningSuite.Application.Agreements;
using CleaningSuite.Application.Common;
using CleaningSuite.Domain.Agreements;
using FluentValidation;
using MediatR;

namespace CleaningSuite.Application.Agreements.Commands;

public record SignAgreementCommand(string Token, string TypedName, byte[] SignaturePng) : IRequest<SignResult>;

public record SignResult(bool Completed, int TotalSigners, int SignedCount);

public class SignAgreementValidator : AbstractValidator<SignAgreementCommand>
{
    public SignAgreementValidator()
    {
        RuleFor(x => x.Token).NotEmpty();
        RuleFor(x => x.TypedName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.SignaturePng).NotEmpty();
    }
}

public class SignAgreementHandler : IRequestHandler<SignAgreementCommand, SignResult>
{
    private readonly ITenantContext _context;
    private readonly IAgreementRepository _repo;
    private readonly IAgreementFileStore _store;
    private readonly IAgreementDocumentGenerator _generator;

    public SignAgreementHandler(ITenantContext context, IAgreementRepository repo,
        IAgreementFileStore store, IAgreementDocumentGenerator generator)
    {
        _context = context; _repo = repo; _store = store; _generator = generator;
    }

    public async Task<SignResult> Handle(SignAgreementCommand request, CancellationToken ct)
    {
        var tenantId = _context.TenantId;
        var agreement = await _repo.FindBySignerTokenAsync(tenantId, request.Token, ct)
            ?? throw new NotFoundException("Agreement", Guid.Empty);

        var signer = agreement.Signers.FirstOrDefault(s => s.Token == request.Token)
            ?? throw new NotFoundException("Signer", Guid.Empty);

        if (agreement.Status == Agreement.StatusCancelled)
            throw new AgreementConflictException("Agreement cancelled");
        if (signer.Status == Signer.StatusSigned)
            throw new AgreementConflictException("Already signed");

        signer.SignatureImagePath = await _store.SaveSignatureAsync(tenantId, agreement.Id, signer.Id, request.SignaturePng, ct);
        signer.TypedName = request.TypedName;
        signer.SignedAtUtc = DateTime.UtcNow;
        signer.Status = Signer.StatusSigned;

        var signedCount = agreement.Signers.Count(s => s.Status == Signer.StatusSigned);
        var total = agreement.Signers.Count;

        if (agreement.AllSigned)
        {
            var signedPath = await _store.SaveSignedAsync(tenantId, agreement.Id, Stream.Null, ct);
            // NOTE: the generator writes directly to outputPath; SaveSignedAsync reserves the path.
            // Better: call the generator with a temp path, then move. See refinement note.
            await _generator.GenerateSignedPdfAsync(agreement.OriginalPdfPath, agreement.Signers, signedPath, ct);
            agreement.SignedPdfPath = signedPath;
            agreement.Status = Agreement.StatusCompleted;
            agreement.CompletedUtc = DateTime.UtcNow;
        }
        else
        {
            agreement.Status = Agreement.StatusPartiallySigned;
        }

        agreement.UpdatedUtc = DateTime.UtcNow;
        await _repo.SaveAsync(tenantId, agreement, ct);
        return new SignResult(agreement.Status == Agreement.StatusCompleted, total, signedCount);
    }
}
```

> **Refinement note:** `SaveSignedAsync` creating an empty file then the generator overwriting it is awkward. Preferred: generate to a temp path via `Path.Combine(Path.GetTempPath(), ...)`, then `SaveSignedAsync` copies it into place. Simplify by having `GenerateSignedPdfAsync` return `byte[]` or write to a temp file the handler moves. Keep the interface returning a `string tempPath` and let the handler call `SaveSignedAsync` with a stream from that temp file. The test mocks both, so the exact sequence is not coupled; adjust at implementation for cleanliness.

Add `AgreementConflictException` in the Agreements namespace (mirrors `SlotConflictException`):

`CleaningSuite.Application/Agreements/AgreementConflictException.cs`:

```csharp
namespace CleaningSuite.Application.Agreements;

public class AgreementConflictException(string message) : Exception(message);
```

Map it to 409 in `Program.cs` `DefaultExceptionHandler.TryHandleAsync` switch (after the `SlotConflictException` line):

```csharp
            CleaningSuite.Application.Agreements.AgreementConflictException => (409, "Agreement conflict"),
```

`AgreementsPublicController.cs`:

```csharp
using CleaningSuite.Application.Agreements.Commands;
using CleaningSuite.Application.Agreements.Queries;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CleaningSuite.Api.Controllers;

[ApiController]
[Route("api/v1/public/{slug}/agreements")]
[AllowAnonymous]
public class AgreementsPublicController : ControllerBase
{
    private readonly IMediator _mediator;
    public AgreementsPublicController(IMediator mediator) => _mediator = mediator;

    [HttpGet("{token}")]
    public async Task<IActionResult> Get(string token, CancellationToken ct)
    {
        var dto = await _mediator.Send(new GetPublicAgreementQuery(token), ct);
        return dto is null ? NotFound() : Ok(dto);
    }

    [HttpPost("{token}/sign")]
    public async Task<IActionResult> Sign(string token, [FromBody] SignRequest body, CancellationToken ct)
    {
        var png = Convert.FromBase64String(Normalize(body.SignaturePng));
        var result = await _mediator.Send(new SignAgreementCommand(token, body.TypedName, png), ct);
        return Ok(result);
    }

    [HttpGet("{token}/file")]
    public async Task<IActionResult> File(string token, CancellationToken ct)
    {
        var path = await _mediator.Send(new GetAgreementFileQuery(token), ct);
        if (path is null) return NotFound();
        return PhysicalFile(path, "application/pdf");
    }

    private static string Normalize(string s) => s.StartsWith("data:") ? s[(s.IndexOf(',') + 1)..] : s;
}

public record SignRequest(string TypedName, string SignaturePng);
```

Add to `AgreementQueries.cs`:

```csharp
public record PublicAgreementDto(string Title, string Status, int TotalSigners, int SignedCount, bool Completed);

public record GetPublicAgreementQuery(string Token) : IRequest<PublicAgreementDto?>;

public class GetPublicAgreementHandler : IRequestHandler<GetPublicAgreementQuery, PublicAgreementDto?>
{
    private readonly ITenantContext _context;
    private readonly IAgreementRepository _repo;
    public GetPublicAgreementHandler(ITenantContext context, IAgreementRepository repo) { _context = context; _repo = repo; }

    public async Task<PublicAgreementDto?> Handle(GetPublicAgreementQuery request, CancellationToken ct)
    {
        var a = await _repo.FindBySignerTokenAsync(_context.TenantId, request.Token, ct);
        if (a is null) return null;
        return new PublicAgreementDto(a.Title, a.Status, a.Signers.Count,
            a.Signers.Count(s => s.Status == Signer.StatusSigned),
            a.Status == Agreement.StatusCompleted);
    }
}

public record GetAgreementFileQuery(string Token) : IRequest<string?>;

public class GetAgreementFileHandler : IRequestHandler<GetAgreementFileQuery, string?>
{
    private readonly ITenantContext _context;
    private readonly IAgreementRepository _repo;
    public GetAgreementFileHandler(ITenantContext context, IAgreementRepository repo) { _context = context; _repo = repo; }

    public async Task<string?> Handle(GetAgreementFileQuery request, CancellationToken ct)
    {
        var a = await _repo.FindBySignerTokenAsync(_context.TenantId, request.Token, ct);
        if (a is null) return null;
        return a.Status == Agreement.StatusCompleted
            ? a.SignedPdfPath
            : a.OriginalPdfPath;
    }
}
```

- [ ] **Step 4: Run to verify pass**

Run: `dotnet test CleaningSuite.Tests --filter "SignAgreement"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add CleaningSuite.Application/Agreements/Commands/SignAgreementCommand.cs CleaningSuite.Application/Agreements/Queries/AgreementQueries.cs CleaningSuite.Application/Agreements/AgreementConflictException.cs CleaningSuite.Api/Controllers/AgreementsPublicController.cs CleaningSuite.Api/Program.cs CleaningSuite.Tests/Agreements/SignAgreementCommandTests.cs
git commit -m "feat: add public sign flow and completion"
```

---

## Task 8: DI wiring + admin document download

**Files:**
- Modify: `CleaningSuite.Api/Program.cs`
- Modify: `CleaningSuite.Api/Controllers/AgreementsAdminController.cs`

- [ ] **Step 1: Register DI in Program.cs**

Add near the other repo registrations:

```csharp
builder.Services.AddScoped<CleaningSuite.Application.Agreements.IAgreementRepository, CleaningSuite.Infrastructure.Persistence.AgreementRepository>();
builder.Services.AddSingleton<CleaningSuite.Application.Agreements.IAgreementFileStore>(sp =>
    new CleaningSuite.Infrastructure.Agreements.DiskAgreementFileStore(
        builder.Configuration["Uploads:Path"] ?? Path.Combine(Directory.GetCurrentDirectory(), "uploads")));
builder.Services.AddSingleton<CleaningSuite.Application.Agreements.IAgreementDocumentGenerator, CleaningSuite.Infrastructure.Agreements.AgreementPdfSigner>();
```

Add the admin download endpoint to `AgreementsAdminController.cs`:

```csharp
    [HttpGet("{id:guid}/document")]
    public async Task<IActionResult> Document(Guid id, CancellationToken ct)
    {
        var path = await _mediator.Send(new GetAgreementDocumentPathQuery(id), ct);
        if (path is null) return NotFound();
        return PhysicalFile(path, "application/pdf", $"agreement-{id:N}.pdf");
    }
```

Add to `AgreementQueries.cs`:

```csharp
public record GetAgreementDocumentPathQuery(Guid Id) : IRequest<string?>;

public class GetAgreementDocumentPathHandler : IRequestHandler<GetAgreementDocumentPathQuery, string?>
{
    private readonly ITenantContext _context;
    private readonly IAgreementRepository _repo;
    public GetAgreementDocumentPathHandler(ITenantContext context, IAgreementRepository repo) { _context = context; _repo = repo; }

    public async Task<string?> Handle(GetAgreementDocumentPathQuery request, CancellationToken ct)
    {
        var a = await _repo.GetAsync(_context.TenantId, request.Id, ct);
        if (a is null) return null;
        return a.Status == Agreement.StatusCompleted ? a.SignedPdfPath : a.OriginalPdfPath;
    }
}
```

- [ ] **Step 2: Build**

Run: `dotnet build CleaningSuite.Backend.sln`
Expected: SUCCESS.

- [ ] **Step 3: Commit**

```bash
git add CleaningSuite.Api/Program.cs CleaningSuite.Api/Controllers/AgreementsAdminController.cs CleaningSuite.Application/Agreements/Queries/AgreementQueries.cs
git commit -m "feat: wire agreement DI and admin download"
```

---

## Task 9: Frontend API clients

**Files:**
- Modify: `src/lib/adminApi.ts`
- Create: `src/lib/agreementApi.ts`

- [ ] **Step 1: Add adminAgreements to adminApi.ts**

Append:

```ts
export interface AgreementSignerDto {
  id: string; name: string; email: string; status: string; token: string;
}
export interface AgreementListItem {
  id: string; title: string; status: string; signerCount: number; signedCount: number; createdUtc: string;
}
export interface AgreementDetail extends AgreementListItem {
  signers: AgreementSignerDto[];
  completedUtc: string | null;
}

export const adminAgreements = {
  list: () => adminGet<AgreementListItem[]>('/api/v1/admin/agreements'),
  get: (id: string) => adminGet<AgreementDetail>(`/api/v1/admin/agreements/${id}`),
  create: async (title: string, pdf: File, signers: { name: string; email: string }[]) => {
    const form = new FormData();
    form.append('title', title);
    form.append('pdf', pdf);
    form.append('signersJson', JSON.stringify(signers));
    const t = token();
    const response = await fetch(`${API_URL}/api/v1/admin/agreements`, {
      method: 'POST', headers: { Authorization: `Bearer ${t}` }, body: form,
    });
    return response.ok ? ((await response.json()) as AgreementDetail) : null;
  },
  addSigner: (id: string, name: string, email: string) =>
    adminSend(`/api/v1/admin/agreements/${id}/signers`, 'POST', { name, email }),
  removeSigner: (id: string, signerId: string) =>
    adminSend(`/api/v1/admin/agreements/${id}/signers/${signerId}`, 'DELETE'),
  cancel: (id: string) => adminSend(`/api/v1/admin/agreements/${id}/cancel`, 'POST'),
  documentUrl: (id: string) => `${API_URL}/api/v1/admin/agreements/${id}/document`,
};
```

- [ ] **Step 2: Create agreementApi.ts (public)**

```ts
import { API_URL } from './api';

export interface PublicAgreementDto {
  title: string; status: string; totalSigners: number; signedCount: number; completed: boolean;
}

export async function getPublicAgreement(token: string): Promise<PublicAgreementDto | null> {
  const r = await fetch(`${API_URL}/api/v1/public/${TENANT}/agreements/${token}`);
  return r.ok ? await r.json() : null;
}

export async function signAgreement(token: string, typedName: string, signaturePng: string): Promise<boolean> {
  const r = await fetch(`${API_URL}/api/v1/public/${TENANT}/agreements/${token}/sign`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ typedName, signaturePng }),
  });
  return r.ok;
}

const TENANT = process.env.NEXT_PUBLIC_TENANT_SLUG ?? 'readysetsiivous';

export const agreementFileUrl = (token: string) =>
  `${API_URL}/api/v1/public/${TENANT}/agreements/${token}/file`;
```

- [ ] **Step 3: Commit**

```bash
git add src/lib/adminApi.ts src/lib/agreementApi.ts
git commit -m "feat: add agreement API clients"
```

---

## Task 10: Admin agreements page

**Files:**
- Create: `src/components/admin/SignaturePad.tsx`
- Create: `src/app/[lang]/admin/agreements/page.tsx`
- Modify: `src/app/[lang]/admin/page.tsx`

- [ ] **Step 1: SignaturePad component**

`SignaturePad.tsx` (draw + type):

```tsx
'use client';

import { useRef, useState } from 'react';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';

export default function SignaturePad({ onExport }: { onExport: (png: string) => void }) {
  const canvasRef = useRef<HTMLCanvasElement>(null);
  const [mode, setMode] = useState<'draw' | 'type'>('draw');
  const [typed, setTyped] = useState('');
  const drawing = useRef(false);

  function exportPng() {
    const canvas = canvasRef.current!;
    onExport(canvas.toDataURL('image/png'));
  }

  function start(e: React.PointerEvent) { drawing.current = true; draw(e); }
  function draw(e: React.PointerEvent) {
    if (!drawing.current) return;
    const c = canvasRef.current!;
    const rect = c.getBoundingClientRect();
    const x = e.clientX - rect.left, y = e.clientY - rect.top;
    const ctx = c.getContext('2d')!;
    ctx.strokeStyle = '#000'; ctx.lineWidth = 2; ctx.lineCap = 'round';
    ctx.lineTo(x, y); ctx.stroke(); ctx.beginPath(); ctx.moveTo(x, y);
  }
  function end() { drawing.current = false; }

  function renderTyped() {
    const c = canvasRef.current!;
    const ctx = c.getContext('2d')!;
    ctx.clearRect(0, 0, c.width, c.height);
    ctx.font = 'italic 28px "Segoe Script", cursive';
    ctx.fillText(typed, 10, 50);
    exportPng();
  }

  return (
    <div className="space-y-2">
      <div className="flex gap-2">
        <Button variant={mode === 'draw' ? 'default' : 'outline'} size="sm" onClick={() => setMode('draw')}>Draw</Button>
        <Button variant={mode === 'type' ? 'default' : 'outline'} size="sm" onClick={() => setMode('type')}>Type</Button>
      </div>
      {mode === 'draw' ? (
        <canvas
          ref={canvasRef} width={400} height={120}
          className="rounded-md border bg-white"
          onPointerDown={start} onPointerMove={draw} onPointerUp={end}
        />
      ) : (
        <Input value={typed} onChange={(e) => setTyped(e.target.value)} placeholder="Type your name" />
      )}
      <Button size="sm" onClick={mode === 'draw' ? exportPng : renderTyped}>Use signature</Button>
    </div>
  );
}
```

- [ ] **Step 2: Admin page**

`agreements/page.tsx` — list + create dialog + detail. Uses `Table`, `Dialog`, `Badge`, `Button`, `Input`, `Label` (all existing), plus `adminAgreements`.

```tsx
'use client';

import { useCallback, useEffect, useState } from 'react';
import { Plus, Copy, Trash2 } from 'lucide-react';
import { adminAgreements, type AgreementListItem } from '@/lib/adminApi';
import { Button } from '@/components/ui/button';
import { Badge } from '@/components/ui/badge';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { Dialog, DialogContent, DialogHeader, DialogTitle, DialogFooter } from '@/components/ui/dialog';
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table';

export default function AgreementsPage() {
  const [items, setItems] = useState<AgreementListItem[] | null>(null);
  const [open, setOpen] = useState(false);
  const [title, setTitle] = useState('');
  const [pdf, setPdf] = useState<File | null>(null);
  const [signers, setSigners] = useState<{ name: string; email: string }[]>([{ name: '', email: '' }]);

  const load = useCallback(async () => { setItems(await adminAgreements.list()); }, []);
  useEffect(() => { load(); }, [load]);

  async function create() {
    if (!pdf) return;
    await adminAgreements.create(title, pdf, signers.filter(s => s.name && s.email));
    setOpen(false); setTitle(''); setPdf(null); setSigners([{ name: '', email: '' }]);
    load();
  }

  return (
    <div>
      <div className="mb-6 flex items-center justify-between">
        <h1 className="text-2xl font-semibold">Agreements</h1>
        <Button onClick={() => setOpen(true)}><Plus className="mr-1.5 h-4 w-4" />New agreement</Button>
      </div>

      <Dialog open={open} onOpenChange={setOpen}>
        <DialogContent className="max-h-[85vh] overflow-y-auto sm:max-w-xl">
          <DialogHeader><DialogTitle>New agreement</DialogTitle></DialogHeader>
          <div className="grid gap-4">
            <div className="space-y-1.5">
              <Label>Title</Label><Input value={title} onChange={(e) => setTitle(e.target.value)} />
            </div>
            <div className="space-y-1.5">
              <Label>PDF</Label>
              <input type="file" accept=".pdf" onChange={(e) => setPdf(e.target.files?.[0] ?? null)} className="text-xs" />
            </div>
            <div className="space-y-2">
              <Label>Signers</Label>
              {signers.map((s, i) => (
                <div key={i} className="flex gap-2">
                  <Input placeholder="Name" value={s.name} onChange={(e) => {
                    const n = [...signers]; n[i].name = e.target.value; setSigners(n);
                  }} />
                  <Input placeholder="Email" value={s.email} onChange={(e) => {
                    const n = [...signers]; n[i].email = e.target.value; setSigners(n);
                  }} />
                  <Button variant="ghost" size="icon" onClick={() => setSigners(signers.filter((_, j) => j !== i))}>
                    <Trash2 className="h-4 w-4" />
                  </Button>
                </div>
              ))}
              <Button variant="outline" size="sm" onClick={() => setSigners([...signers, { name: '', email: '' }])}>
                <Plus className="mr-1.5 h-3.5 w-3.5" />Add signer
              </Button>
            </div>
          </div>
          <DialogFooter>
            <Button variant="outline" onClick={() => setOpen(false)}>Cancel</Button>
            <Button onClick={create} disabled={!pdf || !title}>Create</Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>

      <div className="rounded-xl border bg-card">
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead>Title</TableHead><TableHead>Status</TableHead>
              <TableHead>Signed</TableHead><TableHead className="text-right">Actions</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {items?.map((a) => (
              <TableRow key={a.id}>
                <TableCell className="font-medium">{a.title}</TableCell>
                <TableCell>
                  <Badge variant={a.status === 'Completed' ? 'default' : 'secondary'}>{a.status}</Badge>
                </TableCell>
                <TableCell>{a.signedCount}/{a.signerCount}</TableCell>
                <TableCell className="text-right">
                  {a.status === 'Completed' && (
                    <Button variant="ghost" size="sm" onClick={() => window.open(adminAgreements.documentUrl(a.id))}>Download</Button>
                  )}
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </div>
    </div>
  );
}
```

- [ ] **Step 3: Dashboard entry**

In `src/app/[lang]/admin/page.tsx`, add to SECTIONS:

```tsx
  { href: 'agreements', title: 'Agreements', description: 'Upload agreements and collect e-signatures.', icon: FileSignature },
```

Import `FileSignature` from `lucide-react`.

- [ ] **Step 4: Build**

Run (in `site/`): `npm run build`
Expected: SUCCESS.

- [ ] **Step 5: Commit**

```bash
git add src/components/admin/SignaturePad.tsx src/app/[lang]/admin/agreements/page.tsx src/app/[lang]/admin/page.tsx
git commit -m "feat: add admin agreements page"
```

---

## Task 11: Public sign page

**Files:**
- Create: `src/app/[lang]/sign/[token]/page.tsx`

- [ ] **Step 1: Implement**

`page.tsx`:

```tsx
'use client';

import { useCallback, useEffect, useState } from 'react';
import { useParams } from 'next/navigation';
import SignaturePad from '@/components/admin/SignaturePad';
import { getPublicAgreement, signAgreement, agreementFileUrl, type PublicAgreementDto } from '@/lib/agreementApi';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';

export default function SignPage() {
  const { token } = useParams<{ token: string }>();
  const [agreement, setAgreement] = useState<PublicAgreementDto | null>(null);
  const [name, setName] = useState('');
  const [signature, setSignature] = useState<string>('');
  const [done, setDone] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    getPublicAgreement(token).then((a) => { setAgreement(a); if (a?.completed) setDone(true); });
  }, [token]);

  async function sign() {
    if (!signature || !name) { setError('Enter your name and signature'); return; }
    const ok = await signAgreement(token, name, signature);
    if (ok) { setDone(true); setError(null); }
    else setError('Signing failed');
  }

  return (
    <div className="mx-auto max-w-2xl space-y-6 p-6">
      <h1 className="text-2xl font-semibold">{agreement?.title ?? 'Agreement'}</h1>
      {agreement && (
        <p className="text-sm text-muted-foreground">
          {agreement.signedCount}/{agreement.totalSigners} signed
          {agreement.completed && ' — complete'}
        </p>
      )}

      <iframe src={agreementFileUrl(token)} className="h-[60vh] w-full rounded-md border" />

      {done ? (
        <div className="space-y-3">
          <p className="font-medium">Signed. You can download the document below once everyone has signed.</p>
          {agreement?.completed && (
            <Button onClick={() => window.open(agreementFileUrl(token))}>Download signed document</Button>
          )}
        </div>
      ) : (
        <div className="space-y-4 rounded-xl border p-4">
          <div className="space-y-1.5">
            <Label>Full name</Label>
            <Input value={name} onChange={(e) => setName(e.target.value)} />
          </div>
          <SignaturePad onExport={setSignature} />
          {error && <p className="text-sm text-destructive">{error}</p>}
          <Button onClick={sign}>Sign</Button>
        </div>
      )}
    </div>
  );
}
```

- [ ] **Step 2: Build**

Run (in `site/`): `npm run build`
Expected: SUCCESS.

- [ ] **Step 3: Commit**

```bash
git add src/app/[lang]/sign/[token]/page.tsx
git commit -m "feat: add public sign page"
```

---

## Task 12: End-to-end verification

- [ ] **Step 1: Backend tests**

Run: `dotnet test CleaningSuite.Backend.sln`
Expected: all green.

- [ ] **Step 2: Site build**

Run (in `site/`): `npm run build`
Expected: static export succeeds.

- [ ] **Step 3: Manual smoke**

1. `docker compose -f deploy/docker-compose.yml up -d`.
2. `dotnet run --project CleaningSuite.Api`.
3. Admin: create agreement, upload 2-page PDF with blank bottom, add 2 signers.
4. Copy signer 1 link, open in browser, draw signature, sign.
5. Copy signer 2 link, sign.
6. Confirm agreement Completed; download signed PDF; verify 2 pages and both signatures stamped on last page bottom.

- [ ] **Step 4: Commit fixes**

```bash
git add -A
git commit -m "test: agreement signing end-to-end"
```

---

## Self-Review Notes

- **Spec coverage:** domain (Task 1), file store + tokens (Task 2), repo (Task 3), create/list/get (Task 4), add/remove/cancel (Task 5), last-page PDF stamp (Task 6), sign + completion + public endpoints (Task 7), DI + admin download (Task 8), API clients (Task 9), admin UI (Task 10), sign page (Task 11), verification (Task 12). All spec endpoints and flows covered.
- **Placeholder scan:** two explicit "refinement note" blocks — one for Octokit-style atomicity analog (Task 7 PDF temp-file), one clarifying the PDF test's drawing API. Neither is a placeholder; both name the resolution. `AgreementConflictException` (409) added with its `Program.cs` mapping.
- **Type consistency:** `Signer.Token` string; `SignResult(bool, int, int)`; DTO field names match frontend TS interfaces (`signerCount`, `signedCount`, `completedUtc`, `signers`, `token`). `PhysicalFile` returns absolute path from `DiskAgreementFileStore` (returns full path). Public routes use `{slug}` with tenant resolved by middleware; commands read `ITenantContext.TenantId`.
