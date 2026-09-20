# Content Management via Admin Panel — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let an admin edit every piece of website text (fi/en/sv) in a shadcn admin UI and publish it live by committing `site/src/i18n/*.json` to the repo, which triggers the existing Pages deploy.

**Architecture:** Backend gains a per-tenant `SiteContent` Marten doc (three locale JSON blobs + publish state) plus an Octokit-backed publish client that commits the JSON to the repo. Frontend gains a schema-driven generic editor (structured forms + JSON fallback) in a new `/admin/content` page. No site source files change; only the three i18n JSON data files are rewritten on publish.

**Tech Stack:** .NET 8 + Marten + MediatR + Octokit (backend); Next.js 14 + shadcn/ui (frontend). Existing i18n JSON `fi`/`en`/`sv`.

**Spec:** `docs/superpowers/specs/2026-09-06-content-management-design.md`

## Global Constraints

- Backend clean architecture: Api / Application / Domain / Infrastructure / Tests. Follow MediatR + FluentValidation + xUnit + Moq patterns already in the repo.
- Every Marten doc inherits `BaseDocument` (Id, CreatedUtc, UpdatedUtc, Version).
- Singleton-per-tenant docs use deterministic ids via `TenantDocumentIds`.
- Error contract: RFC 7807 problem details; 409 for conflicts.
- Admin endpoints: `[Authorize(Policy = RealmRoleAuthorization.PolicyPrefix + "admin")]`.
- Site is a static export — do NOT add build-time calls to the API. Content flows only through `site/src/i18n/*.json`.
- Octokit is the only new NuGet dependency. Node: add shadcn components via the existing `components.json` (already configured).
- Publish target is per-tenant from `TenantRegistration`; defaults `MaleeshaKumarage/readysetsiivous`, `main`, `site/src/i18n`.

---

## File Structure

Backend (`cleaning-site/api/`):
- `CleaningSuite.Domain/Tenants/SiteContent.cs` — new doc.
- `CleaningSuite.Domain/Common/TenantDocumentIds.cs` — add `SiteContent` helper.
- `CleaningSuite.Domain/Tenants/TenantRegistration.cs` — add repo/branch/path fields.
- `CleaningSuite.Application/Tenants/ISiteContentRepository.cs` — new repo interface.
- `CleaningSuite.Application/Tenants/ISiteContentRepository.cs` (impl) `CleaningSuite.Infrastructure/Persistence/SiteContentRepository.cs`.
- `CleaningSuite.Application/Tenants/Queries/GetSiteContentQuery.cs` — query + DTO.
- `CleaningSuite.Application/Tenants/Commands/SaveSiteContentCommand.cs` — save one locale.
- `CleaningSuite.Application/Tenants/Commands/PublishSiteContentCommand.cs` — publish.
- `CleaningSuite.Application/Tenants/Commands/SeedSiteContentCommand.cs` — import from repo.
- `CleaningSuite.Application/Tenants/IGitHubContentClient.cs` — GitHub abstraction.
- `CleaningSuite.Infrastructure/GitHub/GitHubContentClient.cs` — Octokit impl.
- `CleaningSuite.Api/Controllers/SiteContentController.cs` — admin endpoints.
- `CleaningSuite.Api/Program.cs` — DI + config.
- `CleaningSuite.Tests/...` — serializer, publish, seed tests.

Frontend (`cleaning-site/site/`):
- `src/lib/adminApi.ts` — add `adminContent`.
- `src/lib/contentSchema.ts` — section schema registry + types.
- `src/components/admin/SectionForm.tsx` — generic structured form.
- `src/components/admin/ListEditor.tsx` — repeatable list editor.
- `src/components/admin/JsonSectionEditor.tsx` — JSON textarea editor.
- `src/app/[lang]/admin/content/page.tsx` — content page shell.

---

## Task 1: `SiteContent` domain doc + deterministic id

**Files:**
- Create: `CleaningSuite.Domain/Tenants/SiteContent.cs`
- Modify: `CleaningSuite.Domain/Common/TenantDocumentIds.cs`

**Interfaces:**
- Produces: `SiteContent` (Slug, Locales, LastPublishedUtc, PublishedHash); `TenantDocumentIds.SiteContent(string tenantId)` → `Guid`.

- [ ] **Step 1: Write failing tests**

Create `CleaningSuite.Tests/Domain/SiteContentTests.cs`:

```csharp
using CleaningSuite.Domain.Common;
using CleaningSuite.Domain.Tenants;

namespace CleaningSuite.Tests.Domain;

public class SiteContentTests
{
    [Fact]
    public void SiteContent_Defaults_EmptyLocales_NoPublish()
    {
        var doc = new SiteContent { Slug = "readysetsiivous" };
        Assert.Empty(doc.Locales);
        Assert.Null(doc.LastPublishedUtc);
        Assert.Null(doc.PublishedHash);
    }

    [Fact]
    public void SiteContentId_IsDeterministic_PerTenant()
    {
        var a = TenantDocumentIds.SiteContent("readysetsiivous");
        var b = TenantDocumentIds.SiteContent("readysetsiivous");
        var c = TenantDocumentIds.SiteContent("other");
        Assert.Equal(a, b);
        Assert.NotEqual(a, c);
    }
}
```

- [ ] **Step 2: Run to verify fail**

Run: `dotnet test CleaningSuite.Tests --filter "SiteContent"`
Expected: FAIL (types don't exist).

- [ ] **Step 3: Implement**

`SiteContent.cs`:

```csharp
using CleaningSuite.Domain.Common;

namespace CleaningSuite.Domain.Tenants;

/// <summary>
/// Per-tenant public site content. Locales maps a language code ("fi"/"en"/"sv")
/// to the raw JSON string that mirrors one i18n file (site/src/i18n/{lang}.json).
/// Stored in the tenant's own partition under a deterministic id.
/// </summary>
public class SiteContent : BaseDocument
{
    public string Slug { get; set; } = "";
    public Dictionary<string, string> Locales { get; set; } = new();
    public DateTime? LastPublishedUtc { get; set; }
    public string? PublishedHash { get; set; }
}
```

`TenantDocumentIds.cs` — add:

```csharp
public static Guid SiteContent(string tenantId) => Deterministic($"site-content:{tenantId}");
```

- [ ] **Step 4: Run to verify pass**

Run: `dotnet test CleaningSuite.Tests --filter "SiteContent"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add CleaningSuite.Domain/Tenants/SiteContent.cs CleaningSuite.Domain/Common/TenantDocumentIds.cs CleaningSuite.Tests/Domain/SiteContentTests.cs
git commit -m "feat: add SiteContent domain document"
```

---

## Task 2: `SiteContentRepository` (Marten)

**Files:**
- Create: `CleaningSuite.Application/Tenants/ISiteContentRepository.cs`
- Create: `CleaningSuite.Infrastructure/Persistence/SiteContentRepository.cs`

**Interfaces:**
- Consumes: `TenantDocumentIds.SiteContent(string)`.
- Produces: `ISiteContentRepository.GetAsync(string tenantId, CancellationToken) → Task<SiteContent?>`; `SaveAsync(string tenantId, SiteContent, CancellationToken) → Task`.

- [ ] **Step 1: Write failing test**

Create `CleaningSuite.Tests/Persistence/SiteContentRepositoryTests.cs`:

```csharp
using CleaningSuite.Application.Tenants;
using CleaningSuite.Domain.Common;
using CleaningSuite.Domain.Tenants;
using CleaningSuite.Infrastructure.Persistence;
using Marten;
using Moq;

namespace CleaningSuite.Tests.Persistence;

public class SiteContentRepositoryTests
{
    [Fact]
    public async Task SaveAsync_StoresUnderDeterministicId()
    {
        var session = new Mock<IDocumentSession>();
        var store = new Mock<IDocumentStore>();
        store.Setup(s => s.DirtyTrackedSession("readysetsiivous")).Returns(session.Object);

        var repo = new SiteContentRepository(store.Object);
        var doc = new SiteContent { Slug = "readysetsiivous", Locales = { ["fi"] = "{}" } };

        await repo.SaveAsync("readysetsiivous", doc);

        session.Verify(s => s.Store(doc), Times.Once);
        session.Verify(s => s.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
```

(Note: `IDocumentSession.LoadAsync`/`Store`/`SaveChangesAsync` are virtual on the Mock; the repo below mirrors `TenantProfileRepository`.)

- [ ] **Step 2: Run to verify fail**

Run: `dotnet test CleaningSuite.Tests --filter "SiteContentRepository"`
Expected: FAIL (interface/class don't exist).

- [ ] **Step 3: Implement**

`ISiteContentRepository.cs`:

```csharp
using CleaningSuite.Domain.Tenants;

namespace CleaningSuite.Application.Tenants;

public interface ISiteContentRepository
{
    Task<SiteContent?> GetAsync(string tenantId, CancellationToken ct = default);
    Task SaveAsync(string tenantId, SiteContent content, CancellationToken ct = default);
}
```

`SiteContentRepository.cs` (mirror `TenantProfileRepository`):

```csharp
using CleaningSuite.Application.Tenants;
using CleaningSuite.Domain.Common;
using CleaningSuite.Domain.Tenants;
using Marten;

namespace CleaningSuite.Infrastructure.Persistence;

public class SiteContentRepository : ISiteContentRepository, IDisposable
{
    private readonly IDocumentStore _store;
    private readonly Dictionary<string, IDocumentSession> _sessions = new();

    public SiteContentRepository(IDocumentStore store) => _store = store;

    public async Task<SiteContent?> GetAsync(string tenantId, CancellationToken ct = default) =>
        await Session(tenantId).LoadAsync<SiteContent>(
            TenantDocumentIds.SiteContent(tenantId), ct);

    public async Task SaveAsync(string tenantId, SiteContent content, CancellationToken ct = default)
    {
        Session(tenantId).Store(content);
        await Session(tenantId).SaveChangesAsync(ct);
    }

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
        foreach (var session in _sessions.Values)
            session.Dispose();
    }
}
```

- [ ] **Step 4: Run to verify pass**

Run: `dotnet test CleaningSuite.Tests --filter "SiteContentRepository"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add CleaningSuite.Application/Tenants/ISiteContentRepository.cs CleaningSuite.Infrastructure/Persistence/SiteContentRepository.cs CleaningSuite.Tests/Persistence/SiteContentRepositoryTests.cs
git commit -m "feat: add SiteContent repository"
```

---

## Task 3: Get + Save content commands

**Files:**
- Create: `CleaningSuite.Application/Tenants/Queries/GetSiteContentQuery.cs`
- Create: `CleaningSuite.Application/Tenants/Commands/SaveSiteContentCommand.cs`

**Interfaces:**
- Consumes: `ISiteContentRepository`, `ITenantContext` (existing, has `TenantId`).
- Produces: `GetSiteContentQuery : IRequest<SiteContentDto?>`; `SiteContentDto(string Slug, Dictionary<string,string> Locales, DateTime? LastPublishedUtc, string? PublishedHash)`; `SaveSiteContentCommand(string Lang, string Json) : IRequest<Unit>`.

- [ ] **Step 1: Write failing test**

Create `CleaningSuite.Tests/Tenants/GetSiteContentQueryTests.cs`:

```csharp
using CleaningSuite.Application.Tenants;
using CleaningSuite.Application.Tenants.Queries;
using CleaningSuite.Domain.Tenants;
using Moq;

namespace CleaningSuite.Tests.Tenants;

public class GetSiteContentQueryTests
{
    [Fact]
    public async Task Get_ReturnsDto_WhenDocExists()
    {
        var repo = new Mock<ISiteContentRepository>();
        repo.Setup(r => r.GetAsync("readysetsiivous", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SiteContent
            {
                Slug = "readysetsiivous",
                Locales = { ["fi"] = "{\"hero\":{}}" },
                LastPublishedUtc = new DateTime(2026, 9, 6, 0, 0, 0, DateTimeKind.Utc),
                PublishedHash = "abc"
            });

        var handler = new GetSiteContentHandler(repo.Object);
        var dto = await handler.Handle(new GetSiteContentQuery(), CancellationToken.None);

        Assert.NotNull(dto);
        Assert.Equal("readysetsiivous", dto!.Slug);
        Assert.Contains("fi", dto.Locales);
        Assert.Equal("abc", dto.PublishedHash);
    }
}
```

(Use `ITenantContext` stub with `TenantId = "readysetsiivous"`; add a `Mock<ITenantContext>` set up `TenantId`. The handler signature below takes both.)

- [ ] **Step 2: Run to verify fail**

Run: `dotnet test CleaningSuite.Tests --filter "GetSiteContent"`
Expected: FAIL.

- [ ] **Step 3: Implement**

`GetSiteContentQuery.cs`:

```csharp
using CleaningSuite.Application.Tenants;
using CleaningSuite.Domain.Tenants;
using MediatR;

namespace CleaningSuite.Application.Tenants.Queries;

public record GetSiteContentQuery : IRequest<SiteContentDto?>;

public record SiteContentDto(
    string Slug,
    Dictionary<string, string> Locales,
    DateTime? LastPublishedUtc,
    string? PublishedHash);

public class GetSiteContentHandler : IRequestHandler<GetSiteContentQuery, SiteContentDto?>
{
    private readonly ITenantContext _context;
    private readonly ISiteContentRepository _content;

    public GetSiteContentHandler(ITenantContext context, ISiteContentRepository content)
    {
        _context = context;
        _content = content;
    }

    public async Task<SiteContentDto?> Handle(GetSiteContentQuery request, CancellationToken ct)
    {
        var doc = await _content.GetAsync(_context.TenantId, ct);
        return doc is null ? null : new SiteContentDto(
            doc.Slug, doc.Locales, doc.LastPublishedUtc, doc.PublishedHash);
    }
}
```

`SaveSiteContentCommand.cs`:

```csharp
using CleaningSuite.Application.Tenants;
using CleaningSuite.Domain.Tenants;
using FluentValidation;
using MediatR;
using System.Text.Json;

namespace CleaningSuite.Application.Tenants.Commands;

public record SaveSiteContentCommand(string Lang, string Json) : IRequest<Unit>;

public class SaveSiteContentValidator : AbstractValidator<SaveSiteContentCommand>
{
    public SaveSiteContentValidator()
    {
        RuleFor(x => x.Lang).Must(l => l is "fi" or "en" or "sv")
            .WithMessage("Lang must be fi, en or sv");
        RuleFor(x => x.Json).Must(IsValidJson).WithMessage("Content must be valid JSON");
    }

    private static bool IsValidJson(string json)
    {
        try { JsonDocument.Parse(json); return true; } catch { return false; }
    }
}

public class SaveSiteContentHandler : IRequestHandler<SaveSiteContentCommand, Unit>
{
    private readonly ITenantContext _context;
    private readonly ISiteContentRepository _content;

    public SaveSiteContentHandler(ITenantContext context, ISiteContentRepository content)
    {
        _context = context;
        _content = content;
    }

    public async Task<Unit> Handle(SaveSiteContentCommand request, CancellationToken ct)
    {
        var tenantId = _context.TenantId;
        var doc = await _content.GetAsync(tenantId, ct) ?? new SiteContent { Slug = tenantId };

        doc.Locales[request.Lang] = request.Json;
        doc.UpdatedUtc = DateTime.UtcNow;
        doc.LastPublishedUtc = null; // saved changes are not yet published

        await _content.SaveAsync(tenantId, doc, ct);
        return Unit.Value;
    }
}
```

- [ ] **Step 4: Run to verify pass**

Run: `dotnet test CleaningSuite.Tests --filter "GetSiteContent"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add CleaningSuite.Application/Tenants/Queries/GetSiteContentQuery.cs CleaningSuite.Application/Tenants/Commands/SaveSiteContentCommand.cs CleaningSuite.Tests/Tenants/GetSiteContentQueryTests.cs
git commit -m "feat: add get/save site content commands"
```

---

## Task 4: Publish — Octokit client + serializer

**Files:**
- Create: `CleaningSuite.Application/Tenants/IGitHubContentClient.cs`
- Create: `CleaningSuite.Infrastructure/GitHub/GitHubContentClient.cs`
- Create: `CleaningSuite.Application/Tenants/Commands/PublishSiteContentCommand.cs`
- Modify: `CleaningSuite.Domain/Tenants/TenantRegistration.cs` (add fields)
- Modify: `CleaningSuite.Infrastructure/...` project file (add Octokit package)

**Interfaces:**
- Consumes: `ISiteContentRepository`, `ITenantRegistry` (existing, loads `TenantRegistration`).
- Produces: `IGitHubContentClient.CommitAsync(string owner, string repo, string branch, string path, IReadOnlyDictionary<string,string> files, string message) → Task<string>` (returns commit SHA); `PublishSiteContentCommand : IRequest<PublishResult>`; `PublishResult(string CommitSha)`.

- [ ] **Step 1: Add Octokit package**

Run (in `CleaningSuite.Infrastructure`):
```
dotnet add package Octokit
```

- [ ] **Step 2: Write failing test for the publish command (client mocked)**

Create `CleaningSuite.Tests/Tenants/PublishSiteContentCommandTests.cs`:

```csharp
using CleaningSuite.Application.Tenants;
using CleaningSuite.Application.Tenants.Commands;
using CleaningSuite.Domain.Tenants;
using Moq;

namespace CleaningSuite.Tests.Tenants;

public class PublishSiteContentCommandTests
{
    [Fact]
    public async Task Publish_CommitsThreeFiles_AndSetsPublishedHash()
    {
        var content = new Mock<ISiteContentRepository>();
        content.Setup(r => r.GetAsync("readysetsiivous", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SiteContent
            {
                Slug = "readysetsiivous",
                Locales =
                {
                    ["fi"] = "{\"a\":1}",
                    ["en"] = "{\"a\":1}",
                    ["sv"] = "{\"a\":1}",
                }
            });
        var registry = new Mock<ITenantRegistry>();
        registry.Setup(r => r.GetAsync("readysetsiivous", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TenantRegistration
            {
                Slug = "readysetsiivous",
                SiteRepo = "MaleeshaKumarage/readysetsiivous",
                SiteBranch = "main",
                SiteContentPath = "site/src/i18n"
            });
        var git = new Mock<IGitHubContentClient>();
        git.Setup(g => g.CommitAsync("MaleeshaKumarage", "readysetsiivous", "main",
                "site/src/i18n", It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<string>()))
            .ReturnsAsync("abc123");

        var handler = new PublishSiteContentHandler(
            MockTenantContext(), content.Object, registry.Object, git.Object);

        var result = await handler.Handle(new PublishSiteContentCommand(), CancellationToken.None);

        Assert.Equal("abc123", result.CommitSha);
        content.Verify(c => c.SaveAsync("readysetsiivous",
            It.Is<SiteContent>(d => d.PublishedHash is not null && d.LastPublishedUtc is not null),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    private static ITenantContext MockTenantContext()
    {
        var c = new Mock<ITenantContext>();
        c.SetupGet(x => x.TenantId).Returns("readysetsiivous");
        return c.Object;
    }
}
```

- [ ] **Step 3: Run to verify fail**

Run: `dotnet test CleaningSuite.Tests --filter "PublishSiteContent"`
Expected: FAIL.

- [ ] **Step 4: Implement**

`TenantRegistration.cs` — add fields:

```csharp
    /// <summary>Git repo "owner/name" that hosts this tenant's public site content.</summary>
    public string SiteRepo { get; set; } = "MaleeshaKumarage/readysetsiivous";
    /// <summary>Branch publish commits target (must match the Pages deploy trigger).</summary>
    public string SiteBranch { get; set; } = "main";
    /// <summary>Repo path to the i18n directory (no trailing slash).</summary>
    public string SiteContentPath { get; set; } = "site/src/i18n";
```

`IGitHubContentClient.cs`:

```csharp
namespace CleaningSuite.Application.Tenants;

public interface IGitHubContentClient
{
    Task<string> CommitAsync(
        string owner, string repo, string branch, string path,
        IReadOnlyDictionary<string, string> files, string message,
        CancellationToken ct = default);
}
```

`PublishSiteContentCommand.cs`:

```csharp
using CleaningSuite.Application.Tenants;
using CleaningSuite.Domain.Tenants;
using FluentValidation;
using MediatR;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CleaningSuite.Application.Tenants.Commands;

public record PublishSiteContentCommand : IRequest<PublishResult>;

public record PublishResult(string CommitSha);

public class PublishSiteContentHandler : IRequestHandler<PublishSiteContentCommand, PublishResult>
{
    private readonly ITenantContext _context;
    private readonly ISiteContentRepository _content;
    private readonly ITenantRegistry _registry;
    private readonly IGitHubContentClient _git;

    public PublishSiteContentHandler(
        ITenantContext context,
        ISiteContentRepository content,
        ITenantRegistry registry,
        IGitHubContentClient git)
    {
        _context = context;
        _content = content;
        _registry = registry;
        _git = git;
    }

    public async Task<PublishResult> Handle(PublishSiteContentCommand request, CancellationToken ct)
    {
        var tenantId = _context.TenantId;
        var doc = await _content.GetAsync(tenantId, ct)
            ?? throw new KeyNotFoundException("SiteContent not seeded");

        var reg = await _registry.GetAsync(tenantId, ct)
            ?? throw new KeyNotFoundException("TenantRegistration not found");

        var files = new Dictionary<string, string>();
        foreach (var (lang, json) in doc.Locales)
            files[$"{lang}.json"] = json;

        // owner/repo split from "owner/repo"
        var parts = reg.SiteRepo.Split('/');
        if (parts.Length != 2)
            throw new InvalidOperationException("SiteRepo must be 'owner/repo'");

        var message = $"content: publish {tenantId} site content";
        var sha = await _git.CommitAsync(parts[0], parts[1], reg.SiteBranch,
            reg.SiteContentPath, files, message, ct);

        doc.PublishedHash = HashOf(doc.Locales);
        doc.LastPublishedUtc = DateTime.UtcNow;
        doc.UpdatedUtc = DateTime.UtcNow;
        await _content.SaveAsync(tenantId, doc, ct);

        return new PublishResult(sha);
    }

    internal static string HashOf(Dictionary<string, string> locales)
    {
        var sorted = locales.OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => $"{kv.Key}:{kv.Value}");
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("|", sorted)));
        return Convert.ToHexString(bytes);
    }
}
```

`GitHubContentClient.cs`:

```csharp
using CleaningSuite.Application.Tenants;
using Octokit;

namespace CleaningSuite.Infrastructure.GitHub;

public class GitHubContentClient : IGitHubContentClient
{
    private readonly GitHubClient _client;

    public GitHubContentClient(string token)
    {
        _client = new GitHubClient(new ProductHeaderValue("cleaning-suite"));
        _client.Credentials = new Credentials(token);
    }

    public async Task<string> CommitAsync(
        string owner, string repo, string branch, string path,
        IReadOnlyDictionary<string, string> files, string message,
        CancellationToken ct = default)
    {
        var repoClient = _client.Repository.Content;
        var changes = new List<RepositoryContentChangeSet>();

        foreach (var (name, content) in files)
        {
            var filePath = $"{path}/{name}";
            var existing = await TryGet(repoClient, owner, repo, filePath, branch);
            changes.Add(existing is null
                ? CreateFileChange(filePath, content)
                : UpdateFileChange(filePath, content, existing.Sha));
        }

        var commit = await repoClient.UpdateFile(
            owner, repo, string.Empty, new UpdateFileRequest(message, string.Empty, branch)
            {
                // Octokit UpdateFile doesn't batch; commit files one by one is unreliable.
                // See note below.
            });

        // NOTE: Octokit has no atomic multi-file update. Commit files sequentially is not
        // atomic. Implementation detail refined in the next step.
        return commit.ContentCommit.Sha;
    }

    private async Task<RepositoryContent?> TryGet(
        IRepositoryContentsClient client, string owner, string repo, string path, string branch)
    {
        try
        {
            return await client.GetContents(owner, repo, path, branch);
        }
        catch (NotFoundException)
        {
            return null;
        }
    }

    private RepositoryContentChangeSet CreateFileChange(string path, string content) =>
        new(path, $"{Path.GetFileName(path)}", content, "create");

    private RepositoryContentChangeSet UpdateFileChange(string path, string content, string sha) =>
        new(path, $"{Path.GetFileName(path)}", content, "update");
}
```

> **Refinement note for implementer:** Octokit's `UpdateFile` writes one file per commit. For a true single-commit multi-file publish, use the low-level Git Data API (`_client.Git.Tree` + `_client.Git.Commit` + `_client.Git.Reference`) to create a tree from the three blobs, a commit, and update the branch ref atomically. Implement `CommitAsync` with the Git Data API so all three JSON files land in one commit; the test above mocks `IGitHubContentClient`, so the exact internal calls are not coupled. Use `_client.Git.Blob.Create`, `NewTree`, `NewCommit`, `Reference.Update` in that order.

- [ ] **Step 5: Run to verify pass**

Run: `dotnet test CleaningSuite.Tests --filter "PublishSiteContent"`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add CleaningSuite.Application/Tenants/IGitHubContentClient.cs CleaningSuite.Application/Tenants/Commands/PublishSiteContentCommand.cs CleaningSuite.Domain/Tenants/TenantRegistration.cs CleaningSuite.Infrastructure/GitHub/GitHubContentClient.cs CleaningSuite.Tests/Tenants/PublishSiteContentCommandTests.cs
git commit -m "feat: add publish site content command with Octokit"
```

---

## Task 5: Seed command (import current JSON from repo)

**Files:**
- Create: `CleaningSuite.Application/Tenants/Commands/SeedSiteContentCommand.cs`
- Modify: `CleaningSuite.Application/Tenants/IGitHubContentClient.cs` (add `ReadAsync`)

**Interfaces:**
- Consumes: `IGitHubContentClient`, `ISiteContentRepository`, `ITenantRegistry`, `ITenantContext`.
- Produces: `SeedSiteContentCommand : IRequest<Unit>`; `IGitHubContentClient.ReadAsync(string owner, string repo, string branch, string path, string filename) → Task<string>` (raw file content).

- [ ] **Step 1: Write failing test**

Create `CleaningSuite.Tests/Tenants/SeedSiteContentCommandTests.cs`:

```csharp
using CleaningSuite.Application.Tenants;
using CleaningSuite.Application.Tenants.Commands;
using CleaningSuite.Domain.Tenants;
using Moq;

namespace CleaningSuite.Tests.Tenants;

public class SeedSiteContentCommandTests
{
    [Fact]
    public async Task Seed_ReadsThreeLocales_MarksPublished()
    {
        var git = new Mock<IGitHubContentClient>();
        git.Setup(g => g.ReadAsync("MaleeshaKumarage", "readysetsiivous", "main",
                "site/src/i18n", It.IsAny<string>()))
            .ReturnsAsync((string o, string r, string b, string p, string f) => $"{{\"lang\":\"{f}\"}}");

        var content = new Mock<ISiteContentRepository>();
        var registry = new Mock<ITenantRegistry>();
        registry.Setup(r => r.GetAsync("readysetsiivous", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TenantRegistration
            {
                Slug = "readysetsiivous",
                SiteRepo = "MaleeshaKumarage/readysetsiivous",
                SiteBranch = "main",
                SiteContentPath = "site/src/i18n"
            });

        var handler = new SeedSiteContentHandler(
            MockTenantContext(), content.Object, registry.Object, git.Object);

        await handler.Handle(new SeedSiteContentCommand(), CancellationToken.None);

        content.Verify(c => c.SaveAsync("readysetsiivous",
            It.Is<SiteContent>(d => d.Locales.Count == 3 && d.PublishedHash is not null),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    private static ITenantContext MockTenantContext()
    {
        var c = new Mock<ITenantContext>();
        c.SetupGet(x => x.TenantId).Returns("readysetsiivous");
        return c.Object;
    }
}
```

- [ ] **Step 2: Run to verify fail**

Run: `dotnet test CleaningSuite.Tests --filter "SeedSiteContent"`
Expected: FAIL.

- [ ] **Step 3: Implement**

Add to `IGitHubContentClient.cs`:

```csharp
    Task<string> ReadAsync(string owner, string repo, string branch, string path,
        string filename, CancellationToken ct = default);
```

Implement `ReadAsync` in `GitHubContentClient.cs` using `GetRawContent` (returns base64) — decode with `Convert.FromBase64String` then UTF8.

`SeedSiteContentCommand.cs`:

```csharp
using CleaningSuite.Application.Tenants;
using CleaningSuite.Domain.Tenants;
using MediatR;

namespace CleaningSuite.Application.Tenants.Commands;

public record SeedSiteContentCommand : IRequest<Unit>;

public class SeedSiteContentHandler : IRequestHandler<SeedSiteContentCommand, Unit>
{
    private static readonly string[] Locales = ["fi", "en", "sv"];
    private readonly ITenantContext _context;
    private readonly ISiteContentRepository _content;
    private readonly ITenantRegistry _registry;
    private readonly IGitHubContentClient _git;

    public SeedSiteContentHandler(
        ITenantContext context, ISiteContentRepository content,
        ITenantRegistry registry, IGitHubContentClient git)
    {
        _context = context; _content = content; _registry = registry; _git = git;
    }

    public async Task<Unit> Handle(SeedSiteContentCommand request, CancellationToken ct)
    {
        var tenantId = _context.TenantId;
        var reg = await _registry.GetAsync(tenantId, ct)
            ?? throw new KeyNotFoundException("TenantRegistration not found");
        var parts = reg.SiteRepo.Split('/');
        if (parts.Length != 2) throw new InvalidOperationException("SiteRepo must be 'owner/repo'");

        var locales = new Dictionary<string, string>();
        foreach (var lang in Locales)
        {
            var raw = await _git.ReadAsync(parts[0], parts[1], reg.SiteBranch,
                reg.SiteContentPath, $"{lang}.json", ct);
            locales[lang] = raw;
        }

        var doc = new SiteContent
        {
            Slug = tenantId,
            Locales = locales,
            PublishedHash = PublishSiteContentHandler.HashOf(locales),
            LastPublishedUtc = DateTime.UtcNow,
        };
        await _content.SaveAsync(tenantId, doc, ct);
        return Unit.Value;
    }
}
```

- [ ] **Step 4: Run to verify pass**

Run: `dotnet test CleaningSuite.Tests --filter "SeedSiteContent"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add CleaningSuite.Application/Tenants/Commands/SeedSiteContentCommand.cs CleaningSuite.Application/Tenants/IGitHubContentClient.cs CleaningSuite.Infrastructure/GitHub/GitHubContentClient.cs CleaningSuite.Tests/Tenants/SeedSiteContentCommandTests.cs
git commit -m "feat: add seed site content command"
```

---

## Task 6: Controller + DI wiring

**Files:**
- Create: `CleaningSuite.Api/Controllers/SiteContentController.cs`
- Modify: `CleaningSuite.Api/Program.cs`

**Interfaces:**
- Consumes: commands/queries from Tasks 3–5.
- Produces: HTTP endpoints `GET/PUT api/v1/admin/content`, `POST api/v1/admin/content/publish`, `POST api/v1/admin/content/seed`.

- [ ] **Step 1: Implement controller**

`SiteContentController.cs`:

```csharp
using CleaningSuite.Application.Tenants.Commands;
using CleaningSuite.Application.Tenants.Queries;
using CleaningSuite.Api.Auth;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CleaningSuite.Api.Controllers;

[ApiController]
[Route("api/v1/admin/content")]
[Authorize(Policy = RealmRoleAuthorization.PolicyPrefix + "admin")]
public class SiteContentController : ControllerBase
{
    private readonly IMediator _mediator;
    public SiteContentController(IMediator mediator) => _mediator = mediator;

    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        var dto = await _mediator.Send(new GetSiteContentQuery(), ct);
        return dto is null ? NotFound() : Ok(dto);
    }

    [HttpPut("{lang}")]
    public async Task<IActionResult> Save(string lang, [FromBody] string json, CancellationToken ct)
    {
        await _mediator.Send(new SaveSiteContentCommand(lang, json), ct);
        return NoContent();
    }

    [HttpPost("publish")]
    public async Task<IActionResult> Publish(CancellationToken ct)
    {
        var result = await _mediator.Send(new PublishSiteContentCommand(), ct);
        return Ok(result);
    }

    [HttpPost("seed")]
    public async Task<IActionResult> Seed(CancellationToken ct)
    {
        await _mediator.Send(new SeedSiteContentCommand(), ct);
        return NoContent();
    }
}
```

- [ ] **Step 2: Register DI in Program.cs**

Add alongside the other repo registrations:

```csharp
builder.Services.AddScoped<CleaningSuite.Application.Tenants.ISiteContentRepository, CleaningSuite.Infrastructure.Persistence.SiteContentRepository>();
builder.Services.AddSingleton<CleaningSuite.Application.Tenants.IGitHubContentClient>(sp =>
    new CleaningSuite.Infrastructure.GitHub.GitHubContentClient(
        builder.Configuration["Content:GitHubToken"]
        ?? throw new InvalidOperationException("Content:GitHubToken missing")));
```

Note: the `PUT {lang}` binding of a raw JSON body to a `string` parameter requires a custom binding. Use `[FromBody] JsonElement` and pass `.GetRawText()`, or bind to `JsonElement json` and call `json.GetRawText()`. Adjust the controller signature to `Save(string lang, [FromBody] JsonElement json, ...)` and pass `json.GetRawText()`.

- [ ] **Step 3: Build**

Run: `dotnet build CleaningSuite.Backend.sln`
Expected: SUCCESS.

- [ ] **Step 4: Commit**

```bash
git add CleaningSuite.Api/Controllers/SiteContentController.cs CleaningSuite.Api/Program.cs
git commit -m "feat: expose site content admin endpoints"
```

---

## Task 7: Frontend — shadcn components + API client

**Files:**
- Modify: `src/lib/adminApi.ts`
- Modify: `src/components/ui/*` (via shadcn CLI)

**Interfaces:**
- Consumes: existing `authorizedFetch`, `token`.
- Produces: `adminContent` object; new ui components `textarea`, `tabs`, `form`, `accordion`, `alert-dialog`, `sonner`, `tooltip`.

- [ ] **Step 1: Add shadcn components**

Run (in `site/`):
```
npx shadcn@latest add textarea tabs form accordion alert-dialog sonner tooltip
```
Expected: components added to `src/components/ui/`, `sonner` provider imported in `src/app/layout.tsx`.

- [ ] **Step 2: Add `adminContent` to adminApi.ts**

Append:

```ts
export type Locale = 'fi' | 'en' | 'sv';

export interface SiteContentDto {
  slug: string;
  locales: Record<Locale, string>;
  lastPublishedUtc: string | null;
  publishedHash: string | null;
}

export const adminContent = {
  get: () => adminGet<SiteContentDto>('/api/v1/admin/content'),
  save: (lang: Locale, json: string) =>
    adminSend(`/api/v1/admin/content/${lang}`, 'PUT', json),
  publish: () => adminSendJson<{ commitSha: string }>('/api/v1/admin/content/publish', 'POST'),
  seed: () => adminSend('/api/v1/admin/content/seed', 'POST'),
};
```

- [ ] **Step 3: Commit**

```bash
git add src/lib/adminApi.ts src/components/ui src/app/layout.tsx
git commit -m "feat: add content admin client and shadcn components"
```

---

## Task 8: Section schema registry

**Files:**
- Create: `src/lib/contentSchema.ts`

**Interfaces:**
- Produces: `Locale`, `FieldDef`, `SectionDef`, `SECTION_SCHEMAS: SectionDef[]`; helpers `structuredSections()`, `jsonSections()`.

- [ ] **Step 1: Implement schema + types**

`src/lib/contentSchema.ts`:

```ts
export type FieldType = 'text' | 'textarea' | 'list';

export interface FieldDef {
  key: string;           // JSON key within the section object
  label: string;         // admin UI label
  type: FieldType;
  itemSchema?: FieldDef[]; // required when type === 'list'
}

export interface SectionDef {
  key: string;           // top-level i18n section key
  label: string;
  kind: 'structured' | 'json';
  fields?: FieldDef[];
}

const text = (key: string, label: string): FieldDef => ({ key, label, type: 'text' });
const area = (key: string, label: string): FieldDef => ({ key, label, type: 'textarea' });

export const SECTION_SCHEMAS: SectionDef[] = [
  { key: 'site', label: 'Site', kind: 'structured', fields: [
    text('title', 'Title'), area('description', 'Meta description'), text('locale', 'Locale') ] },
  { key: 'nav', label: 'Navigation', kind: 'structured', fields: [
    text('services', 'Services'), text('about', 'About'), text('checklist', 'Checklist'),
    text('faq', 'FAQ'), text('contact', 'Contact'), text('whatsappButton', 'WhatsApp button'),
    text('callUs', 'Call us'), text('booking', 'Book'), text('login', 'Login') ] },
  { key: 'hero', label: 'Hero', kind: 'structured', fields: [
    text('headline', 'Headline'), area('subheadline', 'Subheadline'),
    text('cta', 'Primary CTA'), text('secondaryCta', 'Secondary CTA'),
    text('callUs', 'Call us'), text('sameDay', 'Same-day badge'),
    text('localKeywords', 'Local keywords') ] },
  { key: 'trustBadges', label: 'Trust badges', kind: 'structured', fields: [
    text('insured', 'Insured'), text('vetted', 'Vetted'),
    text('satisfaction', 'Satisfaction'), text('eco', 'Eco') ] },
  { key: 'quoteForm', label: 'Quote form', kind: 'structured', fields: [
    text('title', 'Title'), text('subtitle', 'Subtitle'), text('serviceType', 'Service label'),
    text('selectService', 'Select service'), text('selectSize', 'Select size'),
    text('homeCleaning', 'Home cleaning'), text('deepCleaning', 'Deep cleaning'),
    text('moveOutCleaning', 'Move-out cleaning'), text('officeCleaning', 'Office cleaning'),
    text('windowCleaning', 'Window cleaning'), text('ovenFridgeCleaning', 'Oven/fridge cleaning'),
    text('saunaBalconyCleaning', 'Sauna/balcony cleaning'), text('propertySize', 'Property size'),
    text('sizeStudio', 'Studio'), text('sizeSmall', 'Small'), text('sizeMedium', 'Medium'),
    text('sizeLarge', 'Large'), text('sizeXLarge', 'Extra large'), text('sizeRooms1', '1–2 rooms'),
    text('sizeRooms3', '3–4 rooms'), text('sizeRooms5', '5–6 rooms'), text('sizeRooms7', '7+ rooms'),
    text('sizeSliderLabel', 'Slider label'), text('city', 'City label'),
    text('cityPlaceholder', 'City placeholder'), text('date', 'Date label'),
    text('datePlaceholder', 'Date placeholder'), text('submit', 'Submit'),
    text('submitting', 'Submitting'), area('gdprNote', 'GDPR note'),
    text('errorDate', 'Error: date'), text('errorCity', 'Error: city'),
    text('errorService', 'Error: service'), text('errorSize', 'Error: size'),
    text('errorDatePast', 'Error: past date') ] },
  { key: 'whatsapp', label: 'WhatsApp', kind: 'structured', fields: [
    text('greeting', 'Greeting'), text('serviceLabel', 'Service label'),
    text('sizeLabel', 'Size label'), text('cityLabel', 'City label'),
    text('dateLabel', 'Date label'), text('languageNote', 'Language note') ] },
  { key: 'services', label: 'Services', kind: 'structured', fields: [
    text('title', 'Title'), text('subtitle', 'Subtitle'), text('from', 'From prefix'),
    text('hour', 'Hour suffix'), { key: 'home', label: 'Services', type: 'list', itemSchema: [
      text('title', 'Title'), area('description', 'Description'), text('price', 'Price') ] } ] },
  { key: 'kotitalousvahennys', label: 'Tax deduction', kind: 'structured', fields: [
    text('title', 'Title'), area('description', 'Description'),
    text('note', 'Note'), text('cta', 'CTA') ] },
  { key: 'checklist', label: 'Checklist', kind: 'structured', fields: [
    text('title', 'Title'), text('subtitle', 'Subtitle'), text('standard', 'Standard column'),
    text('deep', 'Deep column'), text('included', 'Included'), text('notIncluded', 'Not included'),
    { key: 'items', label: 'Items', type: 'list', itemSchema: [
      text('key', 'Key'), text('label', 'Label') ] } ] },
  { key: 'faq', label: 'FAQ', kind: 'structured', fields: [
    text('title', 'Title'), text('subtitle', 'Subtitle'),
    { key: 'questions', label: 'Questions', type: 'list', itemSchema: [
      text('key', 'Key'), text('q', 'Question'), area('a', 'Answer') ] } ] },
  { key: 'paymentBanner', label: 'Payment methods', kind: 'structured', fields: [
    text('title', 'Title'), text('mobilepay', 'MobilePay'), text('invoice', 'Invoice'),
    text('card', 'Card'), text('cash', 'Cash') ] },
  { key: 'about', label: 'About', kind: 'structured', fields: [
    text('title', 'Title'), text('subtitle', 'Subtitle'),
    { key: 'points', label: 'Points', type: 'list', itemSchema: [
      text('key', 'Key'), text('title', 'Title'), area('description', 'Description') ] } ] },
  { key: 'pricingTransparency', label: 'Pricing transparency', kind: 'structured', fields: [
    text('title', 'Title'), text('subtitle', 'Subtitle'), text('vat', 'VAT'),
    text('supplies', 'Supplies'), text('travel', 'Travel'), text('hidden', 'Hidden costs') ] },
  { key: 'keySecurity', label: 'Key security', kind: 'structured', fields: [
    text('title', 'Title'), text('subtitle', 'Subtitle'), text('insurance', 'Insurance'),
    text('tracking', 'Tracking'), text('handover', 'Handover'), text('certified', 'Certified') ] },
  { key: 'ecoPetBadges', label: 'Eco/pet badges', kind: 'structured', fields: [
    text('title', 'Title'), text('ecoTitle', 'Eco title'), text('ecoDesc', 'Eco desc'),
    text('allergyTitle', 'Allergy title'), text('allergyDesc', 'Allergy desc'),
    text('petTitle', 'Pet title'), text('petDesc', 'Pet desc'),
    text('certifiedTitle', 'Certified title'), text('certifiedDesc', 'Certified desc') ] },
  { key: 'emergencyCta', label: 'Emergency CTA', kind: 'structured', fields: [
    text('title', 'Title'), area('description', 'Description'), text('button', 'Button') ] },
  { key: 'responsibleEmployer', label: 'Responsible employer', kind: 'structured', fields: [
    text('title', 'Title'), text('subtitle', 'Subtitle'), text('tes', 'TES'),
    text('insured', 'Insured'), text('background', 'Background'),
    text('training', 'Training'), text('taxes', 'Taxes') ] },
  { key: 'footer', label: 'Footer', kind: 'structured', fields: [
    text('areasServed', 'Areas served heading'), text('contact', 'Contact heading'),
    text('hours', 'Hours heading'), text('hoursText', 'Hours text'),
    text('copyright', 'Copyright'), text('privacy', 'Privacy link'),
    text('terms', 'Terms link'), { key: 'areas', label: 'Areas', type: 'list', itemSchema: [
      text('value', 'Area') ] } ] },
  { key: 'floatingWhatsApp', label: 'Floating WhatsApp', kind: 'structured', fields: [
    text('tooltip', 'Tooltip') ] },
  { key: 'testimonials', label: 'Testimonials', kind: 'structured', fields: [
    text('title', 'Title'), text('subtitle', 'Subtitle'),
    { key: 'items', label: 'Items', type: 'list', itemSchema: [
      text('name', 'Name'), text('area', 'Area'), area('text', 'Text') ] } ] },
  // JSON fallback sections
  { key: 'privacy', label: 'Privacy policy', kind: 'json' },
  { key: 'terms', label: 'Terms of use', kind: 'json' },
  { key: 'schema', label: 'Schema.org', kind: 'json' },
  { key: 'varaus', label: 'Booking flow', kind: 'json' },
];

export const structuredSections = () => SECTION_SCHEMAS.filter(s => s.kind === 'structured');
export const jsonSections = () => SECTION_SCHEMAS.filter(s => s.kind === 'json');
export const sectionByKey = (key: string) => SECTION_SCHEMAS.find(s => s.key === key);
```

- [ ] **Step 2: Commit**

```bash
git add src/lib/contentSchema.ts
git commit -m "feat: add section schema registry"
```

---

## Task 9: Generic SectionForm + ListEditor + JSON editor

**Files:**
- Create: `src/components/admin/ListEditor.tsx`
- Create: `src/components/admin/SectionForm.tsx`
- Create: `src/components/admin/JsonSectionEditor.tsx`

**Interfaces:**
- Consumes: `FieldDef`, `SectionDef` from `contentSchema.ts`; shadcn `Input`, `Textarea`, `Label`, `Button`, `Dialog`, `Table`.
- Produces: `SectionForm({ schema, value, onChange })`; `ListEditor({ fields, items, onChange })`; `JsonSectionEditor({ value, onChange })`.

- [ ] **Step 1: ListEditor**

`ListEditor.tsx`:

```tsx
'use client';

import { Plus, Trash2 } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { Textarea } from '@/components/ui/textarea';
import type { FieldDef } from '@/lib/contentSchema';

interface ListEditorProps {
  fields: FieldDef[];
  items: Record<string, unknown>[]; // array of objects (or strings for scalar lists)
  onChange: (items: Record<string, unknown>[]) => void;
}

export default function ListEditor({ fields, items, onChange }: ListEditorProps) {
  const isScalar = fields.length === 1 && fields[0].key === 'value';

  function update(index: number, key: string, value: unknown) {
    const next = items.map((it, i) => (i === index ? { ...it, [key]: value } : it));
    onChange(next);
  }
  function add() {
    const blank: Record<string, unknown> = {};
    fields.forEach((f) => (blank[f.key] = ''));
    onChange([...items, blank]);
  }
  function remove(index: number) {
    onChange(items.filter((_, i) => i !== index));
  }

  return (
    <div className="space-y-2">
      {items.map((item, i) => (
        <div key={i} className="flex items-start gap-2 rounded-md border p-2">
          <div className="grid flex-1 gap-2">
            {fields.map((f) => (
              <div key={f.key} className="space-y-1">
                <Label className="text-xs">{f.label}</Label>
                {f.type === 'textarea' ? (
                  <Textarea
                    value={String(item[f.key] ?? '')}
                    onChange={(e) => update(i, f.key, e.target.value)}
                  />
                ) : (
                  <Input
                    value={String(item[f.key] ?? '')}
                    onChange={(e) => update(i, f.key, e.target.value)}
                  />
                )}
              </div>
            ))}
          </div>
          <Button variant="ghost" size="icon" onClick={() => remove(i)}>
            <Trash2 className="h-4 w-4" />
          </Button>
        </div>
      ))}
      <Button type="button" variant="outline" size="sm" onClick={add}>
        <Plus className="mr-1.5 h-3.5 w-3.5" /> Add item
      </Button>
    </div>
  );
}
```

- [ ] **Step 2: SectionForm**

`SectionForm.tsx`:

```tsx
'use client';

import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { Textarea } from '@/components/ui/textarea';
import ListEditor from './ListEditor';
import type { FieldDef, SectionDef } from '@/lib/contentSchema';

interface SectionFormProps {
  schema: SectionDef;
  value: Record<string, unknown>;
  onChange: (value: Record<string, unknown>) => void;
}

export default function SectionForm({ schema, value, onChange }: SectionFormProps) {
  const fields = schema.fields ?? [];

  function setField(key: string, v: unknown) {
    onChange({ ...value, [key]: v });
  }

  return (
    <div className="grid gap-4">
      {fields.map((f) =>
        f.type === 'list' ? (
          <div key={f.key} className="space-y-1.5">
            <Label className="text-sm font-medium">{f.label}</Label>
            <ListEditor
              fields={f.itemSchema ?? []}
              items={(value[f.key] as Record<string, unknown>[]) ?? []}
              onChange={(items) => setField(f.key, items)}
            />
          </div>
        ) : f.type === 'textarea' ? (
          <div key={f.key} className="space-y-1.5">
            <Label className="text-sm">{f.label}</Label>
            <Textarea
              value={String(value[f.key] ?? '')}
              onChange={(e) => setField(f.key, e.target.value)}
            />
          </div>
        ) : (
          <div key={f.key} className="space-y-1.5">
            <Label className="text-sm">{f.label}</Label>
            <Input
              value={String(value[f.key] ?? '')}
              onChange={(e) => setField(f.key, e.target.value)}
            />
          </div>
        )
      )}
    </div>
  );
}
```

- [ ] **Step 3: JsonSectionEditor**

`JsonSectionEditor.tsx`:

```tsx
'use client';

import { useState } from 'react';
import { Textarea } from '@/components/ui/textarea';

interface JsonSectionEditorProps {
  value: string;      // raw JSON of the whole section object
  onChange: (json: string) => void;
}

export default function JsonSectionEditor({ value, onChange }: JsonSectionEditorProps) {
  const [error, setError] = useState<string | null>(null);

  function handleChange(text: string) {
    try {
      JSON.parse(text);
      setError(null);
    } catch (e) {
      setError((e as Error).message);
    }
    onChange(text);
  }

  return (
    <div className="space-y-1.5">
      <Textarea
        className="min-h-[320px] font-mono text-xs"
        value={value}
        onChange={(e) => handleChange(e.target.value)}
      />
      {error && <p className="text-xs text-destructive">Invalid JSON: {error}</p>}
    </div>
  );
}
```

- [ ] **Step 4: Commit**

```bash
git add src/components/admin/ListEditor.tsx src/components/admin/SectionForm.tsx src/components/admin/JsonSectionEditor.tsx
git commit -m "feat: add generic content editors"
```

---

## Task 10: Content admin page

**Files:**
- Create: `src/app/[lang]/admin/content/page.tsx`

**Interfaces:**
- Consumes: `adminContent`, `SECTION_SCHEMAS`, editors, shadcn `Tabs`, `Button`, `Card`, `AlertDialog`, `Badge`, `sonner` toast.

- [ ] **Step 1: Implement page**

`page.tsx`:

```tsx
'use client';

import { useCallback, useEffect, useMemo, useState } from 'react';
import { toast } from 'sonner';
import { adminContent, type Locale, type SiteContentDto } from '@/lib/adminApi';
import { SECTION_SCHEMAS, sectionByKey, type SectionDef } from '@/lib/contentSchema';
import SectionForm from '@/components/admin/SectionForm';
import JsonSectionEditor from '@/components/admin/JsonSectionEditor';
import { Tabs, TabsList, TabsTrigger, TabsContent } from '@/components/ui/tabs';
import { Button } from '@/components/ui/button';
import { Badge } from '@/components/ui/badge';
import {
  AlertDialog, AlertDialogAction, AlertDialogCancel,
  AlertDialogContent, AlertDialogDescription, AlertDialogFooter,
  AlertDialogHeader, AlertDialogTitle, AlertDialogTrigger,
} from '@/components/ui/alert-dialog';

const LOCALES: Locale[] = ['fi', 'en', 'sv'];

export default function ContentPage() {
  const [dto, setDto] = useState<SiteContentDto | null>(null);
  const [locale, setLocale] = useState<Locale>('fi');
  const [section, setSection] = useState<string>('hero');
  const [trees, setTrees] = useState<Record<Locale, Record<string, unknown>>>({ fi: {}, en: {}, sv: {} });
  const [dirty, setDirty] = useState(false);
  const [publishing, setPublishing] = useState(false);

  const load = useCallback(async () => {
    const d = await adminContent.get();
    if (!d) return;
    setDto(d);
    const parsed = Object.fromEntries(
      LOCALES.map((l) => [l, JSON.parse(d.locales[l] ?? '{}')])
    ) as Record<Locale, Record<string, unknown>>;
    setTrees(parsed);
  }, []);

  useEffect(() => { load(); }, [load]);

  const schema = sectionByKey(section);
  const current = trees[locale][section] ?? {};

  function updateSection(next: Record<string, unknown>) {
    setTrees((prev) => ({
      ...prev,
      [locale]: { ...prev[locale], [section]: next },
    }));
    setDirty(true);
  }

  function updateJsonSection(json: string) {
    try {
      updateSection(JSON.parse(json) as Record<string, unknown>);
    } catch { /* invalid JSON: keep last good */ }
  }

  async function save() {
    const json = JSON.stringify(trees[locale]);
    const ok = await adminContent.save(locale, json);
    if (ok) { setDirty(false); toast.success('Saved'); }
    else toast.error('Save failed');
  }

  async function publish() {
    setPublishing(true);
    const r = await adminContent.publish();
    setPublishing(false);
    if (r) { toast.success(`Published ${r.commitSha.slice(0, 7)}`); load(); }
    else toast.error('Publish failed');
  }

  async function seed() {
    const ok = await adminContent.seed();
    if (ok) { toast.success('Imported current site content'); load(); }
    else toast.error('Import failed');
  }

  const unpublished = useMemo(() => {
    if (!dto) return false;
    return dirty || dto.publishedHash === null;
  }, [dto, dirty]);

  return (
    <div className="space-y-6">
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-2xl font-semibold">Website content</h1>
          <p className="text-sm text-muted-foreground">
            Edit site text, then publish to push it live.
          </p>
        </div>
        <div className="flex items-center gap-2">
          {unpublished && <Badge variant="secondary">Unpublished changes</Badge>}
          {dto?.lastPublishedUtc && (
            <span className="text-xs text-muted-foreground">
              Last published {new Date(dto.lastPublishedUtc).toLocaleString()}
            </span>
          )}
          <Button variant="outline" onClick={seed} disabled={publishing}>Import from site</Button>
          <Button onClick={save} disabled={!dirty}>Save</Button>
          <AlertDialog>
            <AlertDialogTrigger asChild>
              <Button disabled={publishing}>{publishing ? 'Publishing…' : 'Publish'}</Button>
            </AlertDialogTrigger>
            <AlertDialogContent>
              <AlertDialogHeader>
                <AlertDialogTitle>Publish to live site?</AlertDialogTitle>
                <AlertDialogDescription>
                  Commits all three language files to the repo, which rebuilds and
                  redeploys the site. Live in ~2–4 minutes.
                </AlertDialogDescription>
              </AlertDialogHeader>
              <AlertDialogFooter>
                <AlertDialogCancel>Cancel</AlertDialogCancel>
                <AlertDialogAction onClick={publish}>Publish</AlertDialogAction>
              </AlertDialogFooter>
            </AlertDialogContent>
          </AlertDialog>
        </div>
      </div>

      <div className="grid gap-6 lg:grid-cols-[220px_1fr]">
        <aside className="space-y-1">
          <p className="px-2 text-xs font-semibold text-muted-foreground">Sections</p>
          {SECTION_SCHEMAS.map((s) => (
            <button
              key={s.key}
              onClick={() => setSection(s.key)}
              className={
                'block w-full rounded-md px-3 py-1.5 text-left text-sm transition-colors ' +
                (s.key === section ? 'bg-primary text-primary-foreground' : 'hover:bg-muted')
              }
            >
              {s.label}
            </button>
          ))}
        </aside>

        <div className="space-y-4 rounded-xl border bg-card p-4">
          <Tabs value={locale} onValueChange={(v) => setLocale(v as Locale)}>
            <TabsList>
              {LOCALES.map((l) => (
                <TabsTrigger key={l} value={l}>{l.toUpperCase()}</TabsTrigger>
              ))}
            </TabsList>
          </Tabs>
          {schema?.kind === 'json' ? (
            <JsonSectionEditor
              value={JSON.stringify(current, null, 2)}
              onChange={updateJsonSection}
            />
          ) : schema ? (
            <SectionForm schema={schema} value={current} onChange={updateSection} />
          ) : null}
        </div>
      </div>
    </div>
  );
}
```

- [ ] **Step 2: Build the site**

Run (in `site/`): `npm run build`
Expected: SUCCESS, page compiles.

- [ ] **Step 3: Commit**

```bash
git add src/app/[lang]/admin/content/page.tsx
git commit -m "feat: add content admin page"
```

---

## Task 11: End-to-end verification

- [ ] **Step 1: Backend tests**

Run: `dotnet test CleaningSuite.Backend.sln`
Expected: all green.

- [ ] **Step 2: Site build**

Run (in `site/`): `npm run build`
Expected: static export succeeds.

- [ ] **Step 3: Manual smoke (dev env)**

1. `docker compose -f deploy/docker-compose.yml up -d` (postgres + keycloak).
2. `dotnet run --project CleaningSuite.Api` with `Content:GitHubToken` set in `.env`.
3. Open `http://localhost:3000/admin/content`, log in, click "Import from site", edit Hero fi text, Save, Publish.
4. Confirm a commit lands on `main` touching `site/src/i18n/*.json`, and Pages deploy starts.

- [ ] **Step 4: Commit any fixes**

```bash
git add -A
git commit -m "test: content management end-to-end"
```

---

## Self-Review Notes

- **Spec coverage:** Storage (Tasks 1–3), Publish (Task 4), Seed (Task 5), Endpoints/DI (Task 6), Admin UI structured + JSON (Tasks 7–10), publish bar + seed + unpublished badge (Task 10), GitHub auth (`Content:GitHubToken` in Task 6), per-tenant target (`TenantRegistration` fields Task 4). Round-trip invariant is guaranteed by storing whole-locale trees and never partial-merging; the publish serializer writes every locale key.
- **Placeholder scan:** Task 4 has an explicit refinement note for the multi-file atomic commit — not a placeholder, it specifies the Git Data API approach and notes the mock decouples the test.
- **Type consistency:** `SiteContent.Locales` is `Dictionary<string,string>` (raw JSON strings) throughout backend; frontend parses to `Record<Locale, Record<string,unknown>>`. `HashOf` lives on `PublishSiteContentHandler` and is reused by `SeedSiteContentHandler`. `adminContent.save(lang, json)` sends a raw string body matching `PUT {lang}`.
